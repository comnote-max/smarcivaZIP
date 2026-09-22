using System.Runtime.InteropServices;
using SmarcivaZip.Core.Safety;
using SmarcivaZip.Core.SevenZip;
using SmarcivaZip.Core.Localization;

namespace SmarcivaZip.Core.Extraction;

public sealed record ExtractResult(
    bool Succeeded,
    string DestinationRoot,
    int FilesExtracted,
    IReadOnlyList<ExtractError> Errors,
    IReadOnlyList<PlannedEntry> RejectedEntries,
    bool WrongPassword,
    bool Cancelled)
{
    public bool HasWarnings => Errors.Count > 0 || RejectedEntries.Count > 0;
}

/// <summary>展開処理の入口。UI はこのクラスだけを呼べばよい。</summary>
public sealed class ExtractService
{
    /// <summary>Zip Bomb を検出したとき、続行してよいか呼び出し側に尋ねる。</summary>
    public Func<BombAssessment, bool>? ConfirmSuspiciousArchive { get; init; }

    public ExtractResult Extract(
        ArchiveReader reader,
        ExtractOptions options,
        IProgress<ExtractProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ExtractPlan plan = ExtractPlan.Create(reader, options);

        if (!ConfirmBombCheck(reader, plan))
        {
            return new ExtractResult(false, plan.DestinationRoot, 0, [], plan.Rejected, false, Cancelled: true);
        }

        Directory.CreateDirectory(PathSanitizer.ToExtendedLengthPath(plan.DestinationRoot));

        using var callback = new ExtractCallback(plan, options, options.Password, progress, cancellationToken);

        uint[] indices = plan.Extractable.Select(p => p.Entry.Index).ToArray();
        int hr = indices.Length == 0
            ? 0
            : reader.Archive.Extract(indices, (uint)indices.Length, 0, callback);

        bool cancelled = cancellationToken.IsCancellationRequested;

        if (hr != 0 && !cancelled && !callback.WrongPassword)
        {
            // 展開そのものが失敗した。個別エラーが記録されていなければ
            // HRESULT から分かる範囲の情報を残す。
            if (callback.Errors.Count == 0)
            {
                callback.Errors.Add(new ExtractError(
                    Path.GetFileName(reader.ArchivePath),
                    Marshal.GetExceptionForHR(hr)?.Message
                        ?? Strings.Format("Error_ExtractFailedHr", hr.ToString("X8"))));
            }
        }

        if (options.PropagateMarkOfTheWeb && !cancelled)
        {
            string? zone = MarkOfTheWeb.Read(reader.ArchivePath);
            if (MarkOfTheWeb.IsUntrusted(zone))
                MarkOfTheWeb.ApplyToAll(callback.ExtractedFiles, zone!);
        }

        return new ExtractResult(
            Succeeded: !cancelled && callback.Errors.Count == 0,
            plan.DestinationRoot,
            callback.ExtractedFiles.Count,
            callback.Errors,
            plan.Rejected,
            callback.WrongPassword,
            cancelled);
    }

    private bool ConfirmBombCheck(ArchiveReader reader, ExtractPlan plan)
    {
        ulong archiveSize;
        try
        {
            archiveSize = (ulong)new FileInfo(reader.ArchivePath).Length;
        }
        catch (IOException)
        {
            return true;
        }

        BombAssessment assessment = ArchiveBombDetector.Assess(
            archiveSize, plan.TotalBytes, plan.Entries.Count);

        if (!assessment.IsSuspicious) return true;

        return ConfirmSuspiciousArchive?.Invoke(assessment) ?? true;
    }

    /// <summary>
    /// アーカイブを開いて展開するところまで一気にやる。
    /// tar.gz のような二重構造は中間ファイルを残さず 2 段階で展開する。
    /// </summary>
    public ExtractResult ExtractFile(
        string archivePath,
        ExtractOptions options,
        ArchiveOpenOptions? openOptions = null,
        IProgress<ExtractProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        openOptions ??= new ArchiveOpenOptions();
        openOptions.Password ??= options.Password;

        using ArchiveReader reader = ArchiveReader.Open(archivePath, openOptions);
        ExtractResult result = Extract(reader, options, progress, cancellationToken);

        if (result.Succeeded && options.UnwrapNestedTar)
        {
            result = UnwrapNestedTar(result, options, progress, cancellationToken);
        }

        if (result.Succeeded && options.DeleteArchiveAfterExtract)
        {
            RecycleBin.TryMoveToRecycleBin(archivePath);
        }

        return result;
    }

    /// <summary>
    /// foo.tar.gz は gzip で 1 個の foo.tar が出てくるだけなので、
    /// もう一段展開して中間の .tar を消す。Lhaplus と同じ体感にするための処理。
    /// </summary>
    private ExtractResult UnwrapNestedTar(
        ExtractResult result,
        ExtractOptions options,
        IProgress<ExtractProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (result.FilesExtracted != 1) return result;

        string[] produced = Directory.Exists(result.DestinationRoot)
            ? Directory.GetFiles(result.DestinationRoot)
            : [];

        if (produced.Length != 1) return result;

        string inner = produced[0];
        if (!inner.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) return result;

        var innerOptions = new ExtractOptions
        {
            OutputDirectory = result.DestinationRoot,
            FolderMode = OutputFolderMode.Never,
            OverwritePolicy = options.OverwritePolicy,
            ExcludeMacMetadata = options.ExcludeMacMetadata,
            ExcludeWindowsMetadata = options.ExcludeWindowsMetadata,
            PreserveTimestamps = options.PreserveTimestamps,
            AllowSymbolicLinks = options.AllowSymbolicLinks,
            PropagateMarkOfTheWeb = false,
            UnwrapNestedTar = false
        };

        try
        {
            using ArchiveReader innerReader = ArchiveReader.Open(inner, new ArchiveOpenOptions());
            ExtractResult innerResult = Extract(innerReader, innerOptions, progress, cancellationToken);

            if (!innerResult.Succeeded) return result;

            innerReader.Dispose();
            File.Delete(PathSanitizer.ToExtendedLengthPath(inner));

            return innerResult with { DestinationRoot = result.DestinationRoot };
        }
        catch (Exception ex) when (ex is ArchiveOpenException or IOException or UnauthorizedAccessException)
        {
            // 中身が tar でなかった、あるいは消せなかった。1 段目の結果をそのまま返す。
            return result;
        }
    }
}
