using SmarcivaZip.Core.Safety;
using SmarcivaZip.Core.SevenZip;

namespace SmarcivaZip.Core.Extraction;

public sealed record ExtractProgress(string CurrentFile, ulong BytesDone, ulong BytesTotal, int FilesDone, int FilesTotal)
{
    public double Fraction => BytesTotal == 0 ? 0 : Math.Clamp((double)BytesDone / BytesTotal, 0, 1);
}

public sealed record ExtractError(string EntryPath, string Message);

/// <summary>
/// 7z.dll が展開中に呼び出してくるコールバック。
/// 書き込み先の決定は ExtractPlan で済ませてあるので、ここは
/// 「計画どおりにストリームを渡す」ことだけに集中する。
/// </summary>
internal sealed class ExtractCallback : IArchiveExtractCallback, ICryptoGetTextPassword, IDisposable
{
    private const int ErrorAbort = unchecked((int)0x80004004); // E_ABORT

    private readonly Dictionary<uint, PlannedEntry> _plan;
    private readonly ExtractOptions _options;
    private readonly string? _password;
    private readonly IProgress<ExtractProgress>? _progress;
    private readonly CancellationToken _cancellationToken;
    private readonly int _totalFiles;

    private OutStreamWrapper? _currentStream;
    private FileStream? _currentFile;
    private PlannedEntry? _currentEntry;
    private string? _currentPath;

    private ulong _totalBytes;
    private ulong _completedBytes;
    private int _completedFiles;

    public List<ExtractError> Errors { get; } = [];
    public List<string> ExtractedFiles { get; } = [];
    public bool WrongPassword { get; private set; }

    public ExtractCallback(
        ExtractPlan plan,
        ExtractOptions options,
        string? password,
        IProgress<ExtractProgress>? progress,
        CancellationToken cancellationToken)
    {
        _plan = plan.Extractable.ToDictionary(p => p.Entry.Index);
        _options = options;
        _password = password;
        _progress = progress;
        _cancellationToken = cancellationToken;
        _totalFiles = plan.Extractable.Count(p => !p.Entry.IsDirectory);
        _totalBytes = plan.TotalBytes;
    }

    public int SetTotal(ulong total)
    {
        if (total > 0) _totalBytes = total;
        return 0;
    }

    public int SetCompleted(in ulong completeValue)
    {
        if (_cancellationToken.IsCancellationRequested) return ErrorAbort;

        _completedBytes = completeValue;
        ReportProgress();
        return 0;
    }

    public int GetStream(uint index, out ISequentialOutStream? outStream, AskMode askExtractMode)
    {
        outStream = null;
        CloseCurrentFile();

        if (_cancellationToken.IsCancellationRequested) return ErrorAbort;
        if (askExtractMode != AskMode.Extract) return 0;
        if (!_plan.TryGetValue(index, out PlannedEntry? planned)) return 0;
        if (planned.TargetPath is null) return 0;

        _currentEntry = planned;

        try
        {
            if (planned.Entry.IsDirectory)
            {
                Directory.CreateDirectory(PathSanitizer.ToExtendedLengthPath(planned.TargetPath));
                return 0;
            }

            string? targetPath = ResolveConflict(planned.TargetPath);
            if (targetPath is null) return 0; // スキップ

            string? parent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(PathSanitizer.ToExtendedLengthPath(parent));

            _currentPath = targetPath;
            _currentFile = new FileStream(
                PathSanitizer.ToExtendedLengthPath(targetPath),
                FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1 << 16, useAsync: false);

            _currentStream = new OutStreamWrapper(_currentFile, leaveOpen: true);
            outStream = _currentStream;
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
        {
            Errors.Add(new ExtractError(planned.Entry.Path, ex.Message));
            _currentEntry = null;
            return 0;
        }
    }

    /// <summary>既存ファイルがあったときの扱いを上書きポリシーに従って決める。</summary>
    private string? ResolveConflict(string targetPath)
    {
        if (!File.Exists(PathSanitizer.ToExtendedLengthPath(targetPath))) return targetPath;

        return _options.OverwritePolicy switch
        {
            OverwritePolicy.Overwrite => targetPath,
            OverwritePolicy.Skip => null,
            _ => PathSanitizer.MakeUniquePath(targetPath)
        };
    }

    public int PrepareOperation(AskMode askExtractMode) => 0;

    public int SetOperationResult(OperationResult resultEOperationResult)
    {
        PlannedEntry? entry = _currentEntry;
        string? path = _currentPath;

        CloseCurrentFile();

        if (entry is null) return 0;

        switch (resultEOperationResult)
        {
            case OperationResult.Ok:
                if (path is not null)
                {
                    ApplyMetadata(path, entry.Entry);
                    ExtractedFiles.Add(path);
                }
                if (!entry.Entry.IsDirectory)
                {
                    _completedFiles++;
                    ReportProgress();
                }
                break;

            case OperationResult.WrongPassword:
                WrongPassword = true;
                Errors.Add(new ExtractError(entry.Entry.Path, "パスワードが違います。"));
                break;

            default:
                Errors.Add(new ExtractError(entry.Entry.Path, DescribeFailure(resultEOperationResult)));
                break;
        }

        _currentEntry = null;
        _currentPath = null;
        return 0;
    }

    private static string DescribeFailure(OperationResult result) => result switch
    {
        OperationResult.UnsupportedMethod => "この圧縮方式には対応していません。",
        OperationResult.DataError => "データが壊れています。",
        OperationResult.CrcError => "CRC が一致しません（ファイルが破損している可能性があります）。",
        OperationResult.Unavailable => "データを読み取れませんでした。",
        OperationResult.UnexpectedEnd => "アーカイブが途中で終わっています。",
        OperationResult.DataAfterEnd => "アーカイブの末尾に余分なデータがあります。",
        OperationResult.IsNotArc => "アーカイブとして認識できませんでした。",
        OperationResult.HeadersError => "ヘッダが壊れています。",
        _ => $"展開に失敗しました ({result})。"
    };

    private void ApplyMetadata(string path, ArchiveEntry entry)
    {
        if (!_options.PreserveTimestamps) return;

        try
        {
            string extended = PathSanitizer.ToExtendedLengthPath(path);
            if (entry.LastWriteTime is { } modified) File.SetLastWriteTime(extended, modified);
            if (entry.CreationTime is { } created) File.SetCreationTime(extended, created);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            // タイムスタンプが付けられなくても展開自体は成功している。
        }
    }

    private void CloseCurrentFile()
    {
        _currentStream?.Dispose();
        _currentStream = null;
        _currentFile?.Dispose();
        _currentFile = null;
    }

    private void ReportProgress()
    {
        _progress?.Report(new ExtractProgress(
            _currentEntry?.Entry.Path ?? string.Empty,
            _completedBytes, _totalBytes, _completedFiles, _totalFiles));
    }

    public int CryptoGetTextPassword(out string result)
    {
        result = _password ?? string.Empty;
        if (_password is null)
        {
            WrongPassword = true;
            return unchecked((int)0x80004005);
        }
        return 0;
    }

    public void Dispose() => CloseCurrentFile();
}
