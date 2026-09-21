using System.Runtime.InteropServices;
using System.Text;
using SmarcivaZip.Core.Encodings;
using SmarcivaZip.Core.SevenZip;

namespace SmarcivaZip.Core.Extraction;

public sealed class ArchiveOpenException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class ArchiveOpenOptions
{
    /// <summary>null なら自動判定。値があればそのコードページでファイル名を読む。</summary>
    public int? ForcedCodePage { get; set; }

    /// <summary>macOS 由来の NFD 名を NFC に正規化する。</summary>
    public bool NormalizeToNfc { get; set; } = true;

    public string? Password { get; set; }
}

/// <summary>
/// 1 つのアーカイブを開き、エントリ一覧の取得と展開を行う。
///
/// ファイル名の文字化け対策はここで完結させる。ZIP については
/// 7z.dll のデコード結果を信用せず、自前で読んだセントラルディレクトリの
/// 生バイト列から名前を作り直す（対応関係は CRC とサイズで検証する）。
/// </summary>
public sealed class ArchiveReader : IDisposable
{
    private readonly IInArchive _archive;
    private readonly InStreamWrapper _stream;
    private readonly FileStream _fileStream;
    private readonly ZipCentralDirectory? _centralDirectory;
    private bool _disposed;

    public string ArchivePath { get; }
    public HandlerInfo Handler { get; }
    public IReadOnlyList<ArchiveEntry> Entries { get; }

    /// <summary>実際に採用したファイル名のコードページ。UTF-16 系の形式では null。</summary>
    public int? DetectedCodePage { get; }

    /// <summary>自動判定の確信度 (0.0-1.0)。手動指定時や判定不要時は 1.0。</summary>
    public double CodePageConfidence { get; }

    public IReadOnlyList<EncodingGuess> CodePageCandidates { get; }

    public bool IsMacArchive { get; }

    public ulong TotalUncompressedSize => Entries.Where(e => !e.IsDirectory)
                                                 .Aggregate(0UL, (sum, e) => sum + e.Size);

    private ArchiveReader(
        string archivePath,
        HandlerInfo handler,
        IInArchive archive,
        InStreamWrapper stream,
        FileStream fileStream,
        IReadOnlyList<ArchiveEntry> entries,
        int? detectedCodePage,
        double confidence,
        IReadOnlyList<EncodingGuess> candidates,
        bool isMacArchive,
        ZipCentralDirectory? centralDirectory)
    {
        _centralDirectory = centralDirectory;
        ArchivePath = archivePath;
        Handler = handler;
        _archive = archive;
        _stream = stream;
        _fileStream = fileStream;
        Entries = entries;
        DetectedCodePage = detectedCodePage;
        CodePageConfidence = confidence;
        CodePageCandidates = candidates;
        IsMacArchive = isMacArchive;
    }

    public static ArchiveReader Open(string archivePath, ArchiveOpenOptions? options = null)
    {
        options ??= new ArchiveOpenOptions();
        CodePageInfo.EnsureEncodingProviderRegistered();

        if (!File.Exists(archivePath))
            throw new ArchiveOpenException($"ファイルが見つかりません: {archivePath}");

        // ZIP なら先にセントラルディレクトリを読んで、文字コードを推定しておく。
        ZipCentralDirectory? centralDirectory = ZipCentralDirectory.TryRead(archivePath);
        EncodingDetectionResult? detection = null;
        int? codePage = options.ForcedCodePage;

        if (centralDirectory is not null)
        {
            detection = DetectZipCodePage(centralDirectory);
            codePage ??= detection.CodePage;
        }

        SevenZipLibrary library = SevenZipLibrary.Instance;
        byte[] header = ReadHeader(archivePath);

        foreach (HandlerInfo handler in RankHandlers(library, archivePath, header))
        {
            FileStream? fileStream = null;
            InStreamWrapper? streamWrapper = null;
            IInArchive? archive = null;

            try
            {
                fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                streamWrapper = new InStreamWrapper(fileStream, leaveOpen: true);
                archive = library.CreateInArchive(handler.ClassId);

                ApplyCodePage(archive, handler, codePage);

                var openCallback = new ArchiveOpenCallback(options.Password);
                ulong maxCheckStartPosition = 1 << 23;

                if (archive.Open(streamWrapper, in maxCheckStartPosition, openCallback) != 0) throw new IOException();
                if (archive.GetNumberOfItems(out uint itemCount) != 0) throw new IOException();
                if (itemCount == 0 && !IsEmptyArchiveAcceptable(handler)) throw new IOException();

                IReadOnlyList<ArchiveEntry> entries = ReadEntries(
                    archive, itemCount, handler, centralDirectory, codePage,
                    options.NormalizeToNfc, out bool isMac);

                return new ArchiveReader(
                    archivePath, handler, archive, streamWrapper, fileStream, entries,
                    centralDirectory is null ? null : codePage,
                    detection?.Confidence ?? 1.0,
                    detection?.Ranked ?? [],
                    isMac || (centralDirectory?.LooksLikeMacArchive ?? false),
                    centralDirectory);
            }
            catch (Exception ex) when (ex is IOException or COMException or InvalidOperationException)
            {
                try { archive?.Close(); } catch (COMException) { /* 開けていない */ }
                if (archive is not null) Marshal.FinalReleaseComObject(archive);
                streamWrapper?.Dispose();
                fileStream?.Dispose();
            }
        }

        throw new ArchiveOpenException(
            $"対応していない形式か、ファイルが壊れています: {Path.GetFileName(archivePath)}");
    }

    /// <summary>
    /// ZIP のファイル名から文字コードを推定する。
    /// EFS フラグが立っているエントリは UTF-8 確定なので判定対象から外す。
    /// </summary>
    private static EncodingDetectionResult DetectZipCodePage(ZipCentralDirectory centralDirectory)
    {
        var ambiguous = centralDirectory.Entries
            .Where(e => !e.HasUtf8Flag && e.UnicodePathExtra is null)
            .Select(e => e.RawName)
            .ToList();

        if (ambiguous.Count == 0)
            return new EncodingDetectionResult(CodePageInfo.Utf8, 1.0, [], AllAscii: true);

        return EncodingDetector.Detect(ambiguous);
    }

    /// <summary>
    /// 7-Zip のハンドラにファイル名のコードページを指示する。
    /// zip / lzh ハンドラは "cp" プロパティを解釈する。
    /// 解釈しないハンドラに渡しても単に無視されるだけで害はない。
    /// </summary>
    private static void ApplyCodePage(IInArchive archive, HandlerInfo handler, int? codePage)
    {
        if (codePage is null) return;
        if (!handler.MatchesExtension("zip") && !handler.MatchesExtension("lzh")
            && !handler.Name.Equals("zip", StringComparison.OrdinalIgnoreCase)
            && !handler.Name.Equals("lzh", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            PropertyHelper.SetProperties(archive, [("cp", PropVariant.FromUInt32((uint)codePage.Value))]);
        }
        catch (COMException)
        {
            // このハンドラは cp を受け付けなかった。自前の名前差し替えで補う。
        }
    }

    private static bool IsEmptyArchiveAcceptable(HandlerInfo handler)
        => handler.Name.Equals("zip", StringComparison.OrdinalIgnoreCase);

    private static byte[] ReadHeader(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[Math.Min(1024, stream.Length)];
            _ = stream.Read(buffer, 0, buffer.Length);
            return buffer;
        }
        catch (IOException)
        {
            return [];
        }
    }

    /// <summary>
    /// 試すハンドラの順番を決める。拡張子一致 → シグネチャ一致 → その他、の順。
    /// 拡張子が嘘をついている（.zip なのに中身は rar など）ケースにも対応できる。
    /// </summary>
    private static IEnumerable<HandlerInfo> RankHandlers(SevenZipLibrary library, string path, byte[] header)
    {
        string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

        var byExtension = library.Handlers.Where(h => h.MatchesExtension(extension)).ToList();
        var bySignature = library.Handlers.Where(h => h.MatchesSignature(header)).ToList();

        var seen = new HashSet<Guid>();

        foreach (HandlerInfo handler in byExtension.Concat(bySignature))
        {
            if (seen.Add(handler.ClassId)) yield return handler;
        }

        // ここまでで開けなかった場合の最後の手段。シグネチャを持たない形式
        // （tar など）はここで拾われる。
        foreach (HandlerInfo handler in library.Handlers)
        {
            if (seen.Add(handler.ClassId)) yield return handler;
        }
    }

    private static IReadOnlyList<ArchiveEntry> ReadEntries(
        IInArchive archive,
        uint itemCount,
        HandlerInfo handler,
        ZipCentralDirectory? centralDirectory,
        int? codePage,
        bool normalizeToNfc,
        out bool isMacArchive)
    {
        var entries = new List<ArchiveEntry>((int)itemCount);
        isMacArchive = false;

        // ZIP は自前で読んだ名前を正とする。ただし 7z.dll 側と同じ並びである
        // 保証はないので、CRC とサイズが全件一致した場合にだけ差し替える。
        IReadOnlyList<string>? repairedNames = centralDirectory is null
            ? null
            : BuildRepairedZipNames(archive, itemCount, centralDirectory, codePage);

        for (uint i = 0; i < itemCount; i++)
        {
            string rawPath = PropertyHelper.GetItemString(archive, i, ItemPropId.Path)
                             ?? PropertyHelper.GetItemString(archive, i, ItemPropId.Name)
                             ?? $"item{i}";

            string path = repairedNames is not null ? repairedNames[(int)i] : rawPath;

            if (normalizeToNfc && NameNormalizer.IsDecomposed(path))
            {
                isMacArchive = true;
                path = NameNormalizer.ToNfc(path);
            }

            entries.Add(new ArchiveEntry
            {
                Index = i,
                Path = path,
                RawPath = rawPath,
                IsDirectory = PropertyHelper.GetItemBool(archive, i, ItemPropId.IsFolder),
                Size = PropertyHelper.GetItemUInt64(archive, i, ItemPropId.Size),
                PackedSize = PropertyHelper.GetItemUInt64(archive, i, ItemPropId.PackedSize),
                IsEncrypted = PropertyHelper.GetItemBool(archive, i, ItemPropId.Encrypted),
                LastWriteTime = PropertyHelper.GetItemDateTime(archive, i, ItemPropId.LastWriteTime),
                CreationTime = PropertyHelper.GetItemDateTime(archive, i, ItemPropId.CreationTime),
                Attributes = PropertyHelper.GetItemUInt32OrNull(archive, i, ItemPropId.Attributes),
                Crc = PropertyHelper.GetItemUInt32OrNull(archive, i, ItemPropId.Crc),
                SymbolicLinkTarget = PropertyHelper.GetItemString(archive, i, ItemPropId.SymLink)
            });
        }

        // 単一ファイル形式（gz / bz2 / xz など）は名前を持たないことがある。
        // その場合は拡張子を取り除いたアーカイブ名を内側のファイル名として使う。
        if (entries.Count == 1 && string.IsNullOrEmpty(entries[0].Path))
        {
            entries[0].Path = "data";
        }

        _ = handler;
        return entries;
    }

    /// <summary>
    /// セントラルディレクトリの生バイト列から名前を作り直し、
    /// 7z.dll のエントリ順と一致することを CRC とサイズで検証する。
    /// 一致しなければ null を返し、7z.dll の名前をそのまま使う。
    /// </summary>
    private static IReadOnlyList<string>? BuildRepairedZipNames(
        IInArchive archive, uint itemCount, ZipCentralDirectory centralDirectory, int? codePage)
    {
        if (centralDirectory.Entries.Count != itemCount) return null;

        Encoding fallback = CodePageInfo.GetLenientEncoding(codePage ?? CodePageInfo.Utf8)
                            ?? Encoding.UTF8;
        var utf8Strict = new UTF8Encoding(false, throwOnInvalidBytes: true);

        var names = new List<string>((int)itemCount);

        for (uint i = 0; i < itemCount; i++)
        {
            ZipEntryNameInfo info = centralDirectory.Entries[(int)i];

            uint? actualCrc = PropertyHelper.GetItemUInt32OrNull(archive, i, ItemPropId.Crc);
            ulong actualSize = PropertyHelper.GetItemUInt64(archive, i, ItemPropId.Size);

            // ディレクトリエントリは CRC もサイズも 0 なので照合の役に立たない。
            // ファイルだけを照合し、1 件でも食い違えば差し替えを諦める。
            bool isDirectoryEntry = info.UncompressedSize == 0 && info.Crc32 == 0;
            if (!isDirectoryEntry)
            {
                if (actualCrc is not null && actualCrc.Value != info.Crc32) return null;
                if (actualSize != info.UncompressedSize) return null;
            }

            names.Add(DecodeZipName(info, fallback, utf8Strict));
        }

        return names;
    }

    private static string DecodeZipName(ZipEntryNameInfo info, Encoding fallback, Encoding utf8Strict)
    {
        // 1. Info-ZIP Unicode Path Extra Field があればそれが最も信頼できる。
        if (info.UnicodePathExtra is not null) return info.UnicodePathExtra;

        // 2. EFS フラグが立っていれば UTF-8 確定。
        if (info.HasUtf8Flag)
        {
            try { return utf8Strict.GetString(info.RawName); }
            catch (DecoderFallbackException) { /* フラグが嘘だった。判定結果に従う。 */ }
        }

        // 3. ASCII のみならどのコードページでも同じ。
        if (EncodingDetector.IsPureAscii(info.RawName))
            return Encoding.ASCII.GetString(info.RawName);

        // 4. 判定したコードページでデコードする。
        return fallback.GetString(info.RawName);
    }

    /// <summary>
    /// 指定したコードページで読んだ場合のファイル名を返す。プレビュー画面用。
    ///
    /// ZIP なら生バイト列を持っているので、開き直さずに文字列変換だけで済む。
    /// ドロップダウンを切り替えるたびにアーカイブを開き直すのは、
    /// エントリ数が多いアーカイブでは目に見えて重くなる。
    /// </summary>
    public IReadOnlyList<string> PreviewNames(int codePage, bool normalizeToNfc, int limit = 500)
    {
        if (_centralDirectory is null)
        {
            return Entries.Take(limit)
                          .Select(e => normalizeToNfc ? NameNormalizer.ToNfc(e.Path) : e.Path)
                          .ToList();
        }

        Encoding decoder = CodePageInfo.GetLenientEncoding(codePage) ?? Encoding.UTF8;
        var utf8Strict = new UTF8Encoding(false, throwOnInvalidBytes: true);

        return _centralDirectory.Entries
            .Take(limit)
            .Select(info =>
            {
                string name = DecodeZipName(info, decoder, utf8Strict);
                return normalizeToNfc ? NameNormalizer.ToNfc(name) : name;
            })
            .ToList();
    }

    /// <summary>コードページを変えて開き直す。プレビューで決めた設定を適用するときに使う。</summary>
    public ArchiveReader Reopen(int codePage, bool normalizeToNfc)
    {
        return Open(ArchivePath, new ArchiveOpenOptions
        {
            ForcedCodePage = codePage,
            NormalizeToNfc = normalizeToNfc
        });
    }

    internal IInArchive Archive => _archive;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _archive.Close(); } catch (COMException) { /* すでに閉じている */ }
        Marshal.FinalReleaseComObject(_archive);
        _stream.Dispose();
        _fileStream.Dispose();
    }
}

/// <summary>ヘッダが暗号化されたアーカイブを開くときにパスワードを供給する。</summary>
internal sealed class ArchiveOpenCallback(string? password) : IArchiveOpenCallback, ICryptoGetTextPassword
{
    public int SetTotal(IntPtr files, IntPtr bytes) => 0;

    public int SetCompleted(IntPtr files, IntPtr bytes) => 0;

    public int CryptoGetTextPassword(out string result)
    {
        result = password ?? string.Empty;
        return password is null ? unchecked((int)0x80004005) : 0;
    }
}
