using System.Runtime.InteropServices;
using SmarcivaZip.Core.Safety;
using SmarcivaZip.Core.SevenZip;
using SmarcivaZip.Core.Localization;

namespace SmarcivaZip.Core.Compression;

public sealed class CompressException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class CompressOptions
{
    public required OutputFormat Format { get; init; }

    public CompressionLevel Level { get; init; } = CompressionLevel.Normal;

    /// <summary>出力先フォルダ。null なら最初の入力と同じ場所。</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>出力ファイル名。null なら入力から自動生成する。</summary>
    public string? OutputFileName { get; set; }

    public string? Password { get; set; }

    /// <summary>7z でファイル名も隠す（ヘッダ暗号化）。</summary>
    public bool EncryptHeaders { get; set; }

    /// <summary>同名ファイルがあるとき上書きせず別名にする。</summary>
    public bool AvoidOverwrite { get; set; } = true;
}

public sealed record CompressResult(string ArchivePath, int ItemCount, IReadOnlyList<string> FailedItems)
{
    public bool HasWarnings => FailedItems.Count > 0;
}

public sealed class CompressService
{
    public CompressResult Compress(
        IReadOnlyList<string> inputPaths,
        CompressOptions options,
        IProgress<CompressProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (inputPaths.Count == 0) throw new CompressException(Strings.Get("CompressError_NoInput"));

        List<CompressItem> items = CompressItemCollector.Collect(inputPaths, cancellationToken);
        if (items.Count == 0) throw new CompressException(Strings.Get("CompressError_NoFiles"));

        string outputPath = ResolveOutputPath(inputPaths, options);

        Directory.CreateDirectory(PathSanitizer.ToExtendedLengthPath(
            Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory()));

        return options.Format.IsTwoStage
            ? CompressTwoStage(items, outputPath, options, progress, cancellationToken)
            : CompressSingleStage(items, outputPath, options, progress, cancellationToken);
    }

    private static string ResolveOutputPath(IReadOnlyList<string> inputPaths, CompressOptions options)
    {
        string directory = options.OutputDirectory
                           ?? Path.GetDirectoryName(Path.GetFullPath(inputPaths[0]))
                           ?? Directory.GetCurrentDirectory();

        string fileName = options.OutputFileName
                          ?? CompressItemCollector.SuggestArchiveName(inputPaths, options.Format);

        string path = Path.Combine(directory, PathSanitizer.SanitizeSegment(fileName));

        return options.AvoidOverwrite ? PathSanitizer.MakeUniquePath(path) : path;
    }

    private CompressResult CompressSingleStage(
        IReadOnlyList<CompressItem> items,
        string outputPath,
        CompressOptions options,
        IProgress<CompressProgress>? progress,
        CancellationToken cancellationToken)
    {
        WriteArchive(items, outputPath, options.Format.HandlerName,
            BuildProperties(options), options.Password, progress, cancellationToken,
            out IReadOnlyList<string> failed);

        return new CompressResult(outputPath, items.Count, failed);
    }

    /// <summary>
    /// tar.gz / tar.xz / tar.zst の作成。
    /// 一度 tar にまとめてから単一ストリーム圧縮をかける、という UNIX 流の二段構成を
    /// 一時ファイル経由で行う。一時ファイルは必ず後始末する。
    /// </summary>
    private CompressResult CompressTwoStage(
        IReadOnlyList<CompressItem> items,
        string outputPath,
        CompressOptions options,
        IProgress<CompressProgress>? progress,
        CancellationToken cancellationToken)
    {
        string temporaryTar = Path.Combine(
            Path.GetTempPath(), $"smarcivazip-{Guid.NewGuid():N}.tar");

        try
        {
            WriteArchive(items, temporaryTar, options.Format.HandlerName, [], null,
                progress, cancellationToken, out IReadOnlyList<string> failed);

            cancellationToken.ThrowIfCancellationRequested();

            var tarItem = new CompressItem
            {
                ArchivePath = Path.GetFileNameWithoutExtension(outputPath) + ".tar",
                SourcePath = temporaryTar,
                IsDirectory = false,
                Size = (ulong)new FileInfo(temporaryTar).Length,
                LastWriteTime = DateTime.Now,
                CreationTime = DateTime.Now,
                Attributes = FileAttributes.Normal
            };

            WriteArchive([tarItem], outputPath, options.Format.TarStageHandler!,
                BuildProperties(options), null, progress, cancellationToken, out _);

            return new CompressResult(outputPath, items.Count, failed);
        }
        finally
        {
            try { if (File.Exists(temporaryTar)) File.Delete(temporaryTar); }
            catch (IOException) { /* 後始末に失敗しても本処理には影響しない */ }
        }
    }

    private static void WriteArchive(
        IReadOnlyList<CompressItem> items,
        string outputPath,
        string handlerName,
        IReadOnlyList<(string Name, PropVariant Value)> properties,
        string? password,
        IProgress<CompressProgress>? progress,
        CancellationToken cancellationToken,
        out IReadOnlyList<string> failedItems)
    {
        SevenZipLibrary library = SevenZipLibrary.Instance;

        HandlerInfo handler = library.FindHandler(handlerName)
            ?? throw new CompressException(
                Strings.Format("CompressError_FormatUnsupported", handlerName));

        if (!handler.CanUpdate)
            throw new CompressException(Strings.Format("CompressError_FormatReadOnly", handler.Name));

        IOutArchive archive = library.CreateOutArchive(handler.ClassId);

        try
        {
            if (properties.Count > 0) PropertyHelper.SetProperties(archive, properties);

            using var callback = new UpdateCallback(items, password, progress, cancellationToken);
            using var fileStream = new FileStream(
                PathSanitizer.ToExtendedLengthPath(outputPath),
                FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1 << 16, useAsync: false);
            using var outStream = new OutStreamWrapper(fileStream, leaveOpen: true);

            int hr = archive.UpdateItems(outStream, (uint)items.Count, callback);

            if (hr != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new CompressException(
                    Marshal.GetExceptionForHR(hr)?.Message
                        ?? Strings.Format("CompressError_Failed", hr.ToString("X8")));
            }

            failedItems = callback.FailedItems;
        }
        catch (OperationCanceledException)
        {
            TryDeletePartialOutput(outputPath);
            throw;
        }
        catch (COMException ex)
        {
            TryDeletePartialOutput(outputPath);
            throw new CompressException(Strings.Get("CompressError_Generic"), ex);
        }
        finally
        {
            Marshal.FinalReleaseComObject(archive);
        }
    }

    private static void TryDeletePartialOutput(string path)
    {
        try
        {
            string extended = PathSanitizer.ToExtendedLengthPath(path);
            if (File.Exists(extended)) File.Delete(extended);
        }
        catch (IOException) { /* 消せなくても致命的ではない */ }
    }

    /// <summary>
    /// 7-Zip のコマンドラインで言う -m オプション相当を組み立てる。
    /// </summary>
    private static List<(string Name, PropVariant Value)> BuildProperties(CompressOptions options)
    {
        var properties = new List<(string, PropVariant)>
        {
            ("x", PropVariant.FromUInt32((uint)options.Level))
        };

        if (options.Password is not null && options.Format.Id == OutputFormat.Zip.Id)
        {
            // ZIP の既定の暗号化 (ZipCrypto) は 1990 年代の方式で、既に破られている。
            // パスワードを付ける場合は必ず AES-256 にする。
            properties.Add(("em", PropVariant.FromString(Marshal.StringToBSTR("AES256"))));
        }

        if (options.EncryptHeaders && options.Format.SupportsHeaderEncryption)
        {
            properties.Add(("he", PropVariant.FromBool(true)));
        }

        foreach ((string name, string value) in options.Format.ExtraProperties)
        {
            properties.Add((name, PropVariant.FromString(Marshal.StringToBSTR(value))));
        }

        return properties;
    }
}
