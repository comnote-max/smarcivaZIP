using System.Text;
using SmarcivaZip.Core.Compression;
using SmarcivaZip.Core.Encodings;
using SmarcivaZip.Core.Extraction;
using SmarcivaZip.Core.SevenZip;
using Xunit;

namespace SmarcivaZip.Tests;

/// <summary>
/// 7z.dll を実際に読み込んで行う結合テスト。
/// native\win-x64\7z.dll が配置されていることが前提（tools\fetch-7zip.ps1 で取得）。
/// </summary>
public sealed class SevenZipIntegrationTests : IDisposable
{
    private readonly string _workDir;

    public SevenZipIntegrationTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "smarcivazip-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); }
        catch (IOException) { /* テストの後始末に失敗しても結果には影響しない */ }
    }

    private string Path_(params string[] parts) => Path.Combine([_workDir, .. parts]);

    [Fact]
    public void 主要フォーマットのハンドラが揃っている()
    {
        var names = SevenZipLibrary.Instance.Handlers
            .Select(h => h.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("zip", names);
        Assert.Contains("7z", names);
        Assert.Contains("tar", names);
        Assert.Contains("gzip", names);
        Assert.Contains("xz", names);

        // Lhaplus に無かった形式。この 2 つが本プロジェクトの目玉。
        Assert.Contains("rar", names);
        Assert.Contains("rar5", names);

        // 古い書庫の展開に必要。
        Assert.Contains("lzh", names);

        // macOS 由来の書庫。
        Assert.Contains("dmg", names);
        Assert.Contains("hfs", names);
    }

    [Fact]
    public void RAR5は展開専用として公開されている()
    {
        HandlerInfo? rar5 = SevenZipLibrary.Instance.FindHandler("rar5");

        Assert.NotNull(rar5);
        // RAR での圧縮は unRAR ライセンスにより行わない。書き込み不可であることを確認する。
        Assert.False(rar5.CanUpdate);
    }

    [Fact]
    public void ZIPを作って展開すると中身が一致する()
    {
        string sourceDir = Path_("src");
        Directory.CreateDirectory(Path.Combine(sourceDir, "サブフォルダ"));
        File.WriteAllText(Path.Combine(sourceDir, "説明.txt"), "こんにちは", Encoding.UTF8);
        File.WriteAllText(Path.Combine(sourceDir, "サブフォルダ", "data.bin"), "0123456789");

        var compressService = new CompressService();
        CompressResult compressed = compressService.Compress(
            [sourceDir],
            new CompressOptions { Format = OutputFormat.Zip, OutputDirectory = _workDir });

        Assert.True(File.Exists(compressed.ArchivePath));

        var extractService = new ExtractService();
        ExtractResult extracted = extractService.ExtractFile(
            compressed.ArchivePath,
            new ExtractOptions { OutputDirectory = Path_("out"), OpenFolderAfterExtract = false });

        Assert.True(extracted.Succeeded, string.Join("; ", extracted.Errors.Select(e => e.Message)));

        string root = extracted.DestinationRoot;
        Assert.Equal("こんにちは", File.ReadAllText(Path.Combine(root, "src", "説明.txt")));
        Assert.Equal("0123456789", File.ReadAllText(Path.Combine(root, "src", "サブフォルダ", "data.bin")));
    }

    [Fact]
    public void 七z形式でパスワード付きアーカイブを作って展開できる()
    {
        string file = Path_("secret.txt");
        File.WriteAllText(file, "機密データ", Encoding.UTF8);

        var compressService = new CompressService();
        CompressResult compressed = compressService.Compress(
            [file],
            new CompressOptions
            {
                Format = OutputFormat.SevenZip,
                OutputDirectory = _workDir,
                Password = "p@ssw0rd-日本語",
                EncryptHeaders = true
            });

        var extractService = new ExtractService();
        ExtractResult extracted = extractService.ExtractFile(
            compressed.ArchivePath,
            new ExtractOptions
            {
                OutputDirectory = Path_("out7z"),
                Password = "p@ssw0rd-日本語",
                OpenFolderAfterExtract = false
            });

        Assert.True(extracted.Succeeded, string.Join("; ", extracted.Errors.Select(e => e.Message)));
        Assert.Equal("機密データ",
            File.ReadAllText(Path.Combine(extracted.DestinationRoot, "secret.txt")));
    }

    [Fact]
    public void TARGZを二段階で作って一度に展開できる()
    {
        string sourceDir = Path_("tarsrc");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(sourceDir, "b.txt"), "beta");

        var compressService = new CompressService();
        CompressResult compressed = compressService.Compress(
            [sourceDir],
            new CompressOptions { Format = OutputFormat.TarGz, OutputDirectory = _workDir });

        Assert.EndsWith(".tar.gz", compressed.ArchivePath);

        var extractService = new ExtractService();
        ExtractResult extracted = extractService.ExtractFile(
            compressed.ArchivePath,
            new ExtractOptions { OutputDirectory = Path_("outtar"), OpenFolderAfterExtract = false });

        Assert.True(extracted.Succeeded, string.Join("; ", extracted.Errors.Select(e => e.Message)));

        // 中間の .tar が残っていないこと（Lhaplus と同じく一発で最後まで展開する）。
        Assert.Empty(Directory.GetFiles(extracted.DestinationRoot, "*.tar", SearchOption.AllDirectories));
        Assert.Equal("alpha",
            File.ReadAllText(Path.Combine(extracted.DestinationRoot, "tarsrc", "a.txt")));
    }

    [Fact]
    public void CP932で書かれた文字化けZIPを正しい名前で展開する()
    {
        // 2000 年代の日本語 Windows が作った ZIP を再現する。
        // UTF-8 フラグが無く、名前は CP932 の生バイト。
        string archive = Path_("legacy.zip");
        var entries = new List<(byte[], byte[])>
        {
            (TestZipBuilder.EncodeName(CodePageInfo.ShiftJis, "写真/夏の思い出.txt"), "natsu"u8.ToArray()),
            (TestZipBuilder.EncodeName(CodePageInfo.ShiftJis, "資料/第1章 はじめに.txt"), "chapter"u8.ToArray())
        };

        TestZipBuilder.Write(archive, entries, utf8Flag: false);

        using ArchiveReader reader = ArchiveReader.Open(archive);

        Assert.Equal(CodePageInfo.ShiftJis, reader.DetectedCodePage);
        Assert.Contains(reader.Entries, e => e.Path == "写真/夏の思い出.txt");
        Assert.Contains(reader.Entries, e => e.Path == "資料/第1章 はじめに.txt");

        var extractService = new ExtractService();
        ExtractResult result = extractService.Extract(
            reader,
            new ExtractOptions { OutputDirectory = Path_("legacy-out"), OpenFolderAfterExtract = false });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.True(File.Exists(Path.Combine(result.DestinationRoot, "写真", "夏の思い出.txt")));
    }

    [Fact]
    public void コードページを手動指定して開き直せる()
    {
        string archive = Path_("manual.zip");
        TestZipBuilder.Write(archive,
            [(TestZipBuilder.EncodeName(CodePageInfo.ShiftJis, "設定.ini"), "x"u8.ToArray())],
            utf8Flag: false);

        using ArchiveReader auto = ArchiveReader.Open(archive);
        using ArchiveReader forced = ArchiveReader.Open(archive,
            new ArchiveOpenOptions { ForcedCodePage = CodePageInfo.Gbk });

        Assert.Equal("設定.ini", auto.Entries[0].Path);
        // GBK で読むとわざと化ける。UI の手動切り替えが効いていることの確認。
        Assert.NotEqual("設定.ini", forced.Entries[0].Path);
    }

    [Fact]
    public void macOS製ZIPのNFD名をNFCに直しメタデータを除外する()
    {
        // macOS が作る ZIP の再現。UTF-8 フラグあり・名前は NFD・__MACOSX 付き。
        string archive = Path_("mac.zip");
        string decomposedName = "ガ" + "ラス.txt"; // ガラス.txt の NFD 表現

        TestZipBuilder.Write(archive,
        [
            (Encoding.UTF8.GetBytes(decomposedName), "glass"u8.ToArray()),
            (Encoding.UTF8.GetBytes("__MACOSX/._" + decomposedName), "junk"u8.ToArray()),
            (Encoding.UTF8.GetBytes(".DS_Store"), "junk"u8.ToArray())
        ], utf8Flag: true);

        using ArchiveReader reader = ArchiveReader.Open(archive);
        Assert.Contains(reader.Entries, e => e.Path.StartsWith("ガラス", StringComparison.Ordinal));

        var extractService = new ExtractService();
        ExtractResult result = extractService.Extract(
            reader,
            new ExtractOptions { OutputDirectory = Path_("mac-out"), OpenFolderAfterExtract = false });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Message)));

        string root = result.DestinationRoot;
        Assert.True(File.Exists(Path.Combine(root, "ガラス.txt")));
        Assert.False(Directory.Exists(Path.Combine(root, "__MACOSX")));
        Assert.False(File.Exists(Path.Combine(root, ".DS_Store")));
    }

    [Fact]
    public void 展開先の外に出ようとするエントリを拒否する()
    {
        string archive = Path_("evil.zip");
        TestZipBuilder.Write(archive,
        [
            (Encoding.ASCII.GetBytes("../../evil.txt"), "pwned"u8.ToArray()),
            (Encoding.ASCII.GetBytes("safe.txt"), "ok"u8.ToArray())
        ], utf8Flag: false);

        using ArchiveReader reader = ArchiveReader.Open(archive);
        var extractService = new ExtractService();

        ExtractResult result = extractService.Extract(
            reader,
            new ExtractOptions { OutputDirectory = Path_("evil-out"), OpenFolderAfterExtract = false });

        Assert.Single(result.RejectedEntries);
        Assert.True(File.Exists(Path.Combine(result.DestinationRoot, "safe.txt")));
        Assert.False(File.Exists(Path.Combine(_workDir, "evil.txt")));
    }

    [Fact]
    public void ルート直下が単一フォルダなら二重フォルダを作らない()
    {
        string sourceDir = Path_("single");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "x.txt"), "x");

        var compressService = new CompressService();
        CompressResult compressed = compressService.Compress(
            [sourceDir], new CompressOptions { Format = OutputFormat.Zip, OutputDirectory = _workDir });

        string outDir = Path_("single-out");
        Directory.CreateDirectory(outDir);

        var extractService = new ExtractService();
        ExtractResult result = extractService.ExtractFile(
            compressed.ArchivePath,
            new ExtractOptions { OutputDirectory = outDir, OpenFolderAfterExtract = false });

        // single\x.txt という構造なので、さらに single フォルダで包まない。
        Assert.Equal(outDir, result.DestinationRoot);
        Assert.True(File.Exists(Path.Combine(outDir, "single", "x.txt")));
    }

    [Fact]
    public void ルート直下に複数ある場合はフォルダにまとめる()
    {
        string a = Path_("a.txt");
        string b = Path_("b.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");

        var compressService = new CompressService();
        CompressResult compressed = compressService.Compress(
            [a, b],
            new CompressOptions
            {
                Format = OutputFormat.Zip,
                OutputDirectory = _workDir,
                OutputFileName = "bundle.zip"
            });

        string outDir = Path_("bundle-out");
        Directory.CreateDirectory(outDir);

        var extractService = new ExtractService();
        ExtractResult result = extractService.ExtractFile(
            compressed.ArchivePath,
            new ExtractOptions { OutputDirectory = outDir, OpenFolderAfterExtract = false });

        // 展開先に散らからないよう bundle フォルダを作る（tar bomb 対策）。
        Assert.Equal(Path.Combine(outDir, "bundle"), result.DestinationRoot);
        Assert.True(File.Exists(Path.Combine(outDir, "bundle", "a.txt")));
    }
}
