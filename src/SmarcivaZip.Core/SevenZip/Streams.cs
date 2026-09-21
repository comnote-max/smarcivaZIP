using System.Runtime.InteropServices;

namespace SmarcivaZip.Core.SevenZip;

/// <summary>
/// .NET の <see cref="Stream"/> を 7z.dll に読み取り用として渡すためのラッパー。
/// processedSize は NULL が渡されることがあるので必ず IntPtr.Zero を判定する。
/// </summary>
internal sealed class InStreamWrapper(Stream stream, bool leaveOpen) : IInStream, ISequentialInStream, IDisposable
{
    private const int Fail = unchecked((int)0x80004005); // E_FAIL

    public int Read(byte[] data, uint size, IntPtr processedSize)
    {
        int read = 0;
        try
        {
            if (size > 0) read = stream.Read(data, 0, (int)Math.Min(size, (uint)data.Length));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return Fail;
        }

        if (processedSize != IntPtr.Zero) Marshal.WriteInt32(processedSize, read);
        return 0;
    }

    public int Seek(long offset, uint seekOrigin, IntPtr newPosition)
    {
        long position;
        try
        {
            position = stream.Seek(offset, (SeekOrigin)seekOrigin);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or ObjectDisposedException)
        {
            return Fail;
        }

        if (newPosition != IntPtr.Zero) Marshal.WriteInt64(newPosition, position);
        return 0;
    }

    public void Dispose()
    {
        if (!leaveOpen) stream.Dispose();
    }
}

/// <summary>
/// 圧縮元ファイルを 7z.dll に渡すためのストリーム。
///
/// 7-Zip の ZIP 更新処理はマルチスレッドで動き、メインスレッドが
/// SetOperationResult を返した「後」にワーカースレッドが実際の読み取りを行う。
/// つまり SetOperationResult は「もう読み終わった」という合図にならない。
///
/// かといって全ファイルのハンドルを開きっぱなしにすると、
/// 数万ファイルのアーカイブでハンドルを使い切ってしまう。
///
/// そこで、このクラスは論理的な位置だけを保持し、OS のハンドルは
/// いつ閉じても構わない作りにする（Park）。閉じた後に読まれたら
/// 黙って開き直して位置を復元するので、7-Zip から見れば常に開いている。
/// </summary>
internal sealed class FileInStream : IInStream, ISequentialInStream, IDisposable
{
    private const int Fail = unchecked((int)0x80004005);

    private readonly string _path;
    private readonly Lock _gate = new();
    private FileStream? _handle;
    private long _position;
    private bool _disposed;

    public FileInStream(string path)
    {
        _path = path;
    }

    /// <summary>OS のハンドルだけを解放する。論理位置は保持するので、次の読み取りで開き直せる。</summary>
    public bool Park()
    {
        lock (_gate)
        {
            if (_handle is null) return false;
            _handle.Dispose();
            _handle = null;
            return true;
        }
    }

    public bool IsOpen
    {
        get { lock (_gate) return _handle is not null; }
    }

    private FileStream EnsureOpen()
    {
        if (_handle is not null) return _handle;

        _handle = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 1 << 16, useAsync: false);

        if (_position != 0) _handle.Seek(_position, SeekOrigin.Begin);
        return _handle;
    }

    public int Read(byte[] data, uint size, IntPtr processedSize)
    {
        int read = 0;

        lock (_gate)
        {
            if (_disposed) return Fail;

            try
            {
                if (size > 0)
                {
                    FileStream handle = EnsureOpen();
                    read = handle.Read(data, 0, (int)Math.Min(size, (uint)data.Length));
                    _position += read;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                return Fail;
            }
        }

        if (processedSize != IntPtr.Zero) Marshal.WriteInt32(processedSize, read);
        return 0;
    }

    public int Seek(long offset, uint seekOrigin, IntPtr newPosition)
    {
        lock (_gate)
        {
            if (_disposed) return Fail;

            try
            {
                FileStream handle = EnsureOpen();
                _position = handle.Seek(offset, (SeekOrigin)seekOrigin);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException
                                          or ArgumentException or ObjectDisposedException)
            {
                return Fail;
            }
        }

        if (newPosition != IntPtr.Zero) Marshal.WriteInt64(newPosition, _position);
        return 0;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _handle?.Dispose();
            _handle = null;
        }
    }
}

/// <summary>
/// 7z.dll が展開結果を書き込む先。1 エントリにつき 1 インスタンス。
/// </summary>
internal sealed class OutStreamWrapper(Stream stream, bool leaveOpen) : IOutStream, ISequentialOutStream, IDisposable
{
    private const int Fail = unchecked((int)0x80004005);

    public long BytesWritten { get; private set; }

    public int Write(byte[] data, uint size, IntPtr processedSize)
    {
        try
        {
            int count = (int)Math.Min(size, (uint)data.Length);
            stream.Write(data, 0, count);
            BytesWritten += count;
            if (processedSize != IntPtr.Zero) Marshal.WriteInt32(processedSize, count);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return Fail;
        }
    }

    public int Seek(long offset, uint seekOrigin, IntPtr newPosition)
    {
        long position;
        try
        {
            position = stream.Seek(offset, (SeekOrigin)seekOrigin);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException
                                      or ArgumentException or ObjectDisposedException)
        {
            return Fail;
        }

        if (newPosition != IntPtr.Zero) Marshal.WriteInt64(newPosition, position);
        return 0;
    }

    public int SetSize(long newSize)
    {
        try
        {
            stream.SetLength(newSize);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            return Fail;
        }
    }

    public void Dispose()
    {
        if (!leaveOpen) stream.Dispose();
    }
}
