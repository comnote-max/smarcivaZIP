using System.Buffers.Binary;
using System.Text;

namespace SmarcivaZip.Core.Encodings;

public sealed class ZipEntryNameInfo
{
    public required byte[] RawName { get; init; }

    /// <summary>汎用目的ビット 11 (EFS)。立っていればファイル名は UTF-8 と保証される。</summary>
    public required bool HasUtf8Flag { get; init; }

    /// <summary>Info-ZIP Unicode Path Extra Field (0x7075) に入っていた UTF-8 名。</summary>
    public string? UnicodePathExtra { get; init; }

    /// <summary>version made by の上位バイト。0 = FAT/MS-DOS, 3 = UNIX, 19 = macOS。</summary>
    public required byte HostOs { get; init; }

    /// <summary>7z.dll 側のエントリ順との対応付けを検証するために使う。</summary>
    public required uint Crc32 { get; init; }

    public required ulong UncompressedSize { get; init; }
    public required ulong CompressedSize { get; init; }
    public required bool IsEncrypted { get; init; }
}

/// <summary>
/// ZIP のセントラルディレクトリを自前で読む。
///
/// 7z.dll はファイル名を「デコード済みの文字列」で返すため、生バイト列を見られない。
/// 文字コードを推定するには生バイトが要るので、ここだけ自前実装する。
/// 推定したコードページは ISetProperties 経由で 7z.dll の zip ハンドラに渡す。
/// </summary>
public sealed class ZipCentralDirectory
{
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint Zip64EndOfCentralDirectorySignature = 0x06064B50;
    private const uint Zip64LocatorSignature = 0x07064B50;
    private const uint CentralFileHeaderSignature = 0x02014B50;

    private const int EndOfCentralDirectoryLength = 22;
    private const int MaxCommentLength = 0xFFFF;

    public required IReadOnlyList<ZipEntryNameInfo> Entries { get; init; }

    /// <summary>
    /// macOS の Finder / ditto が作った ZIP かどうか。
    /// そうであればファイル名は UTF-8 NFD である可能性が高く、NFC 正規化が要る。
    /// </summary>
    public bool LooksLikeMacArchive { get; private set; }

    public static ZipCentralDirectory? TryRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Read(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private static ZipCentralDirectory? Read(Stream stream)
    {
        if (!TryLocateCentralDirectory(stream, out long offset, out long size, out long entryCount))
            return null;

        if (size <= 0 || size > 256L * 1024 * 1024) return null;

        var buffer = new byte[size];
        stream.Seek(offset, SeekOrigin.Begin);
        if (!TryReadExactly(stream, buffer)) return null;

        var entries = new List<ZipEntryNameInfo>((int)Math.Min(entryCount, 100_000));
        int position = 0;

        while (position + 46 <= buffer.Length)
        {
            var span = buffer.AsSpan(position);
            if (BinaryPrimitives.ReadUInt32LittleEndian(span) != CentralFileHeaderSignature) break;

            ushort versionMadeBy = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(span[8..]);
            uint crc32 = BinaryPrimitives.ReadUInt32LittleEndian(span[16..]);
            uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(span[20..]);
            uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(span[24..]);
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[28..]);
            int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(span[30..]);
            int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(span[32..]);

            int headerLength = 46 + nameLength + extraLength + commentLength;
            if (position + headerLength > buffer.Length) break;

            byte[] rawName = buffer[(position + 46)..(position + 46 + nameLength)];
            ReadOnlySpan<byte> extra = span.Slice(46 + nameLength, extraLength);

            entries.Add(new ZipEntryNameInfo
            {
                RawName = rawName,
                HasUtf8Flag = (flags & 0x0800) != 0,
                IsEncrypted = (flags & 0x0001) != 0,
                UnicodePathExtra = ReadUnicodePathExtra(extra),
                HostOs = (byte)(versionMadeBy >> 8),
                Crc32 = crc32,
                CompressedSize = compressedSize,
                UncompressedSize = uncompressedSize == uint.MaxValue
                    ? ReadZip64UncompressedSize(extra) ?? uncompressedSize
                    : uncompressedSize
            });

            position += headerLength;
        }

        if (entries.Count == 0) return null;

        var result = new ZipCentralDirectory { Entries = entries };
        result.LooksLikeMacArchive = DetectMacArchive(entries);
        return result;
    }

    /// <summary>
    /// Info-ZIP Unicode Path Extra Field (0x7075):
    /// version(1) + 元の名前の CRC32(4) + UTF-8 の名前(可変)。
    /// 文字化けした ZIP でも、ここに正しい名前が入っていることがある。
    /// </summary>
    private static string? ReadUnicodePathExtra(ReadOnlySpan<byte> extra)
    {
        int position = 0;
        while (position + 4 <= extra.Length)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra[position..]);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(extra[(position + 2)..]);
            int dataStart = position + 4;
            if (dataStart + length > extra.Length) break;

            if (id == 0x7075 && length > 5)
            {
                ReadOnlySpan<byte> name = extra.Slice(dataStart + 5, length - 5);
                try
                {
                    return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(name);
                }
                catch (DecoderFallbackException)
                {
                    return null;
                }
            }

            position = dataStart + length;
        }

        return null;
    }

    private static ulong? ReadZip64UncompressedSize(ReadOnlySpan<byte> extra)
    {
        int position = 0;
        while (position + 4 <= extra.Length)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra[position..]);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(extra[(position + 2)..]);
            int dataStart = position + 4;
            if (dataStart + length > extra.Length) break;

            if (id == 0x0001 && length >= 8)
                return BinaryPrimitives.ReadUInt64LittleEndian(extra[dataStart..]);

            position = dataStart + length;
        }

        return null;
    }

    private static bool DetectMacArchive(List<ZipEntryNameInfo> entries)
    {
        foreach (ZipEntryNameInfo entry in entries)
        {
            // __MACOSX/ や .DS_Store は ASCII なので生バイトのまま判定できる。
            string ascii = Encoding.ASCII.GetString(entry.RawName);
            if (ascii.StartsWith("__MACOSX/", StringComparison.Ordinal)) return true;
            if (ascii.EndsWith(".DS_Store", StringComparison.Ordinal)) return true;

            // macOS の zip は version made by の OS を 3 (UNIX) または 19 (Darwin) にする。
            // それに加えて名前が NFD なら、ほぼ macOS 製と断定してよい。
            if (entry.HostOs is 3 or 19 && entry.HasUtf8Flag && NameNormalizer.IsDecomposed(entry.RawName))
                return true;
        }

        return false;
    }

    private static bool TryLocateCentralDirectory(Stream stream, out long offset, out long size, out long entryCount)
    {
        offset = 0;
        size = 0;
        entryCount = 0;

        long fileLength = stream.Length;
        if (fileLength < EndOfCentralDirectoryLength) return false;

        int searchLength = (int)Math.Min(fileLength, EndOfCentralDirectoryLength + MaxCommentLength);
        var tail = new byte[searchLength];
        stream.Seek(fileLength - searchLength, SeekOrigin.Begin);
        if (!TryReadExactly(stream, tail)) return false;

        int eocdIndex = -1;
        for (int i = searchLength - EndOfCentralDirectoryLength; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == EndOfCentralDirectorySignature)
            {
                eocdIndex = i;
                break;
            }
        }

        if (eocdIndex < 0) return false;

        var eocd = tail.AsSpan(eocdIndex);
        entryCount = BinaryPrimitives.ReadUInt16LittleEndian(eocd[10..]);
        size = BinaryPrimitives.ReadUInt32LittleEndian(eocd[12..]);
        offset = BinaryPrimitives.ReadUInt32LittleEndian(eocd[16..]);

        bool needsZip64 = size == uint.MaxValue || offset == uint.MaxValue || entryCount == ushort.MaxValue;
        if (needsZip64 && TryReadZip64(stream, tail, eocdIndex, fileLength - searchLength,
                out long z64Offset, out long z64Size, out long z64Count))
        {
            offset = z64Offset;
            size = z64Size;
            entryCount = z64Count;
        }

        return offset >= 0 && offset + size <= fileLength;
    }

    private static bool TryReadZip64(Stream stream, byte[] tail, int eocdIndex, long tailBaseOffset,
        out long offset, out long size, out long entryCount)
    {
        offset = 0;
        size = 0;
        entryCount = 0;

        int locatorIndex = eocdIndex - 20;
        if (locatorIndex < 0) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(locatorIndex)) != Zip64LocatorSignature) return false;

        long zip64EocdOffset = BinaryPrimitives.ReadInt64LittleEndian(tail.AsSpan(locatorIndex + 8));
        if (zip64EocdOffset < 0 || zip64EocdOffset + 56 > stream.Length) return false;

        var record = new byte[56];
        stream.Seek(zip64EocdOffset, SeekOrigin.Begin);
        if (!TryReadExactly(stream, record)) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(record) != Zip64EndOfCentralDirectorySignature) return false;

        entryCount = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(32));
        size = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(40));
        offset = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(48));
        _ = tailBaseOffset;
        return true;
    }

    private static bool TryReadExactly(Stream stream, byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int chunk = stream.Read(buffer, read, buffer.Length - read);
            if (chunk == 0) return false;
            read += chunk;
        }
        return true;
    }
}
