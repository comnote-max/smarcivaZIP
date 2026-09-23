using System.Buffers.Binary;
using System.Text;

namespace SmarcivaZip.Core.Encodings;

/// <summary>LZH の 1 エントリぶんの、デコード前の名前とサイズ。</summary>
public sealed record LzhEntryNameInfo(byte[] DirectoryRaw, byte[] NameRaw, ulong OriginalSize, bool IsDirectory)
{
    /// <summary>
    /// 指定のエンコーディングでパスを組み立てる。区切りは '\'。
    ///
    /// ディレクトリ拡張ヘッダの区切り 0xFF はバイトのまま切ってから各部分をデコードする。
    /// 0xFF は CP932 の 2 バイト目に来ないので、切っても文字を割らない。
    /// 名前の中の '\'（0x5C）はデコード後に扱う。CP932 では「表」(0x95 0x5C) のように
    /// 2 バイト目が 0x5C になる文字があり、バイトのまま切ると文字が割れるため。
    /// </summary>
    public string Decode(Encoding encoding)
    {
        var parts = new List<string>();

        foreach (byte[] segment in Split(DirectoryRaw, 0xFF))
        {
            if (segment.Length > 0) parts.Add(encoding.GetString(segment));
        }

        if (NameRaw.Length > 0) parts.Add(encoding.GetString(NameRaw));

        return string.Join('\\', parts).Replace('/', '\\').Trim('\\');
    }

    private static IEnumerable<byte[]> Split(byte[] bytes, byte separator)
    {
        int start = 0;
        for (int i = 0; i <= bytes.Length; i++)
        {
            if (i == bytes.Length || bytes[i] == separator)
            {
                yield return bytes[start..i];
                start = i + 1;
            }
        }
    }
}

/// <summary>
/// LZH のヘッダを自前で読み、ファイル名の生バイト列を取り出す。
///
/// 7z.dll は LZH のファイル名を Windows のシステムのコードページで読む。
/// 日本語版 Windows ではそれが CP932 なので偶然正しく見えるが、
/// 英語版なら CP1252/CP437 で読まれて日本語名が化ける。LZH の名前は
/// ほぼ例外なく CP932 なので、生バイト列から CP932（か利用者の指定）で読み直す。
///
/// 対応するのはヘッダレベル 0 / 1 / 2。レベル 3 や自己解凍形式など、
/// 読めないものは null を返し、呼び出し側は 7z.dll の名前をそのまま使う。
/// </summary>
public static class LzhHeaders
{
    /// <summary>エントリ数の上限。壊れた書庫で延々と辿り続けないため。</summary>
    private const int MaxEntries = 200_000;

    public static IReadOnlyList<LzhEntryNameInfo>? TryRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Read(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<LzhEntryNameInfo>? Read(Stream stream)
    {
        var entries = new List<LzhEntryNameInfo>();
        long length = stream.Length;
        long position = 0;

        while (position < length)
        {
            if (entries.Count >= MaxEntries) return null;

            // ヘッダの大きさが 0 なら書庫の終わり。
            byte first = ReadAt(stream, position, 1)?[0] ?? 0;
            if (first == 0) break;

            // レベルは 21 バイト目にあり、先頭 22 バイトはどのレベルでも同じ並び。
            byte[]? common = ReadAt(stream, position, 22);
            if (common is null || !IsMethod(common.AsSpan(2, 5))) return null;

            int level = common[20];
            ulong packedSize = BinaryPrimitives.ReadUInt32LittleEndian(common.AsSpan(7));
            ulong originalSize = BinaryPrimitives.ReadUInt32LittleEndian(common.AsSpan(11));
            bool isDirectory = common.AsSpan(2, 5).SequenceEqual("-lhd-"u8);

            LzhEntryNameInfo? entry;
            long next;

            switch (level)
            {
                case 0:
                case 1:
                {
                    int headerSize = first + 2;
                    byte[]? header = ReadAt(stream, position, headerSize);
                    if (header is null || headerSize < 24) return null;

                    int nameLength = header[21];
                    if (22 + nameLength > headerSize) return null;
                    byte[] name = header[22..(22 + nameLength)];
                    byte[] directory = [];

                    if (level == 1)
                    {
                        // 拡張ヘッダは本体の直後に続き、そのサイズは「圧縮後サイズ」に含まれる。
                        // 最初の拡張ヘッダの大きさは、本体の最後の 2 バイトにある。
                        int nextSize = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(headerSize - 2));
                        long extPosition = position + headerSize;
                        if (!ReadExtensions(stream, extPosition, nextSize, ref name, ref directory, out _))
                            return null;
                    }

                    entry = new LzhEntryNameInfo(directory, name, originalSize, isDirectory);
                    next = position + headerSize + (long)packedSize;
                    break;
                }

                case 2:
                {
                    int totalSize = BinaryPrimitives.ReadUInt16LittleEndian(common.AsSpan(0));
                    byte[]? header = ReadAt(stream, position, 26);
                    if (header is null || totalSize < 26) return null;

                    byte[] name = [];
                    byte[] directory = [];
                    int nextSize = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(24));
                    if (!ReadExtensions(stream, position + 26, nextSize, ref name, ref directory, out _))
                        return null;

                    entry = new LzhEntryNameInfo(directory, name, originalSize, isDirectory);
                    next = position + totalSize + (long)packedSize;
                    break;
                }

                default:
                    return null;
            }

            entries.Add(entry);
            if (next <= position) return null;
            position = next;
        }

        return entries;
    }

    /// <summary>
    /// 拡張ヘッダを辿る。各ヘッダは [種類 1][データ][次のヘッダの大きさ 2] で、
    /// 大きさには自身の 3 バイトぶんが含まれる。0x01 がファイル名、0x02 がディレクトリ名。
    /// </summary>
    private static bool ReadExtensions(
        Stream stream, long position, int size, ref byte[] name, ref byte[] directory, out long end)
    {
        end = position;
        int guard = 0;

        while (size != 0)
        {
            if (size < 3 || ++guard > 1024) return false;

            byte[]? ext = ReadAt(stream, position, size);
            if (ext is null) return false;

            byte type = ext[0];
            byte[] data = ext[1..(size - 2)];
            if (type == 0x01) name = data;
            else if (type == 0x02) directory = data;

            position += size;
            size = BinaryPrimitives.ReadUInt16LittleEndian(ext.AsSpan(size - 2));
        }

        end = position;
        return true;
    }

    /// <summary>"-lh5-" や "-lhd-"、"-lzs-"、"-pm2-" のような 5 バイトの圧縮法の名前か。</summary>
    private static bool IsMethod(ReadOnlySpan<byte> method)
        => method.Length == 5 && method[0] == (byte)'-' && method[4] == (byte)'-'
           && (method[1] == (byte)'l' || method[1] == (byte)'p');

    private static byte[]? ReadAt(Stream stream, long position, int count)
    {
        if (position < 0 || position + count > stream.Length) return null;

        var buffer = new byte[count];
        stream.Position = position;
        stream.ReadExactly(buffer);
        return buffer;
    }
}
