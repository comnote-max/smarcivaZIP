using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SmarcivaZip.Core.SevenZip;

namespace SmarcivaZip.Core.Compression;

public sealed record CompressProgress(string CurrentFile, ulong BytesDone, ulong BytesTotal, int FilesDone, int FilesTotal)
{
    public double Fraction => BytesTotal == 0 ? 0 : Math.Clamp((double)BytesDone / BytesTotal, 0, 1);
}

/// <summary>
/// 7z.dll が圧縮中に「次は何を入れるのか」を尋ねてくるコールバック。
///
/// 7-Zip の挙動で注意が要る点が 2 つある。
///
/// 1. ZIP の更新処理はマルチスレッドで動く。メインスレッドが SetOperationResult を
///    返した「後」にワーカースレッドが実際のファイル読み取りを行うため、
///    SetOperationResult を「読み終わった合図」として扱うとストリームを
///    早く閉じすぎて壊れる。
/// 2. GetStream はディレクトリに対しては呼ばれない。
///
/// そこで入力ストリームは <see cref="FileInStream"/> に任せる。
/// あれは OS ハンドルをいつ閉じられても、次に読まれたら黙って開き直す。
/// ここでは同時に開くハンドル数に上限を設けるだけで、
/// 実際の破棄は圧縮処理が終わってからまとめて行う。
/// </summary>
internal sealed class UpdateCallback : IArchiveUpdateCallback, ICryptoGetTextPassword2, IDisposable
{
    private const int ErrorAbort = unchecked((int)0x80004004); // E_ABORT

    /// <summary>「既存エントリの流用ではなく新規追加」を表す番兵。</summary>
    private const uint NewItemIndex = uint.MaxValue;

    /// <summary>同時に開いておく OS ハンドルの上限。超えた分は古い順に閉じる。</summary>
    private const int MaxOpenHandles = 64;

    private readonly IReadOnlyList<CompressItem> _items;
    private readonly string? _password;
    private readonly IProgress<CompressProgress>? _progress;
    private readonly CancellationToken _cancellationToken;

    private readonly ConcurrentDictionary<uint, FileInStream> _streams = new();
    private readonly ConcurrentQueue<FileInStream> _handleOrder = new();
    private readonly ConcurrentBag<string> _failedItems = [];

    private ulong _totalBytes;
    private ulong _completedBytes;
    private int _completedFiles;
    private volatile string _currentName = string.Empty;

    public IReadOnlyList<string> FailedItems => _failedItems.ToList();

    public UpdateCallback(
        IReadOnlyList<CompressItem> items,
        string? password,
        IProgress<CompressProgress>? progress,
        CancellationToken cancellationToken)
    {
        _items = items;
        _password = password;
        _progress = progress;
        _cancellationToken = cancellationToken;
        _totalBytes = items.Where(i => !i.IsDirectory).Aggregate(0UL, (sum, i) => sum + i.Size);
    }

    public int SetTotal(ulong total)
    {
        if (total > 0) Interlocked.Exchange(ref _totalBytes, total);
        return 0;
    }

    public int SetCompleted(in ulong completeValue)
    {
        if (_cancellationToken.IsCancellationRequested) return ErrorAbort;

        Interlocked.Exchange(ref _completedBytes, completeValue);
        ReportProgress();
        return 0;
    }

    public int GetUpdateItemInfo(uint index, ref int newData, ref int newProperties, ref uint indexInArchive)
    {
        newData = 1;
        newProperties = 1;
        indexInArchive = NewItemIndex;
        return 0;
    }

    public int GetProperty(uint index, ItemPropId propId, ref PropVariant value)
    {
        if (index >= _items.Count)
        {
            value = PropVariant.Empty;
            return 0;
        }

        CompressItem item = _items[(int)index];

        value = propId switch
        {
            ItemPropId.Path => PropVariant.FromString(Marshal.StringToBSTR(item.ArchivePath)),
            ItemPropId.IsFolder => PropVariant.FromBool(item.IsDirectory),
            ItemPropId.Size => PropVariant.FromUInt64(item.Size),
            ItemPropId.Attributes => PropVariant.FromUInt32((uint)item.Attributes),
            ItemPropId.LastWriteTime => PropVariant.FromFileTime(item.LastWriteTime),
            ItemPropId.CreationTime => PropVariant.FromFileTime(item.CreationTime),
            ItemPropId.IsAnti => PropVariant.FromBool(false),
            _ => PropVariant.Empty
        };

        return 0;
    }

    public int GetStream(uint index, out ISequentialInStream? inStream)
    {
        inStream = null;

        if (_cancellationToken.IsCancellationRequested) return ErrorAbort;
        if (index >= _items.Count) return 0;

        CompressItem item = _items[(int)index];
        _currentName = item.ArchivePath;

        if (item.IsDirectory) return 0;

        if (!File.Exists(item.SourcePath))
        {
            // 一覧を作ってから圧縮するまでの間に消えたファイル。全体は止めない。
            _failedItems.Add(item.ArchivePath);
            return 0;
        }

        var stream = new FileInStream(item.SourcePath);
        _streams[index] = stream;
        _handleOrder.Enqueue(stream);
        TrimOpenHandles();

        inStream = stream;
        return 0;
    }

    /// <summary>
    /// 開いたハンドルが増えすぎたら古い順に閉じる。
    /// 閉じても FileInStream が必要に応じて開き直すので動作は変わらない。
    /// </summary>
    private void TrimOpenHandles()
    {
        while (_handleOrder.Count > MaxOpenHandles && _handleOrder.TryDequeue(out FileInStream? oldest))
        {
            oldest.Park();
        }
    }

    public int SetOperationResult(int operationResult)
    {
        // ここでストリームを閉じてはいけない（クラスの説明を参照）。
        Interlocked.Increment(ref _completedFiles);
        ReportProgress();
        return 0;
    }

    public int CryptoGetTextPassword2(out int passwordIsDefined, out string password)
    {
        passwordIsDefined = _password is null ? 0 : 1;
        password = _password ?? string.Empty;
        return 0;
    }

    private void ReportProgress()
    {
        _progress?.Report(new CompressProgress(
            _currentName,
            Interlocked.Read(ref _completedBytes),
            Interlocked.Read(ref _totalBytes),
            Volatile.Read(ref _completedFiles),
            _items.Count));
    }

    public void Dispose()
    {
        foreach (FileInStream stream in _streams.Values) stream.Dispose();
        _streams.Clear();
        _handleOrder.Clear();
    }
}
