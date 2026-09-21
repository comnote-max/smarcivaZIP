using System.Buffers.Binary;
using System.Text;

namespace SmarcivaZip.Tests;

/// <summary>
/// テスト用に「わざと文字化けする ZIP」を作る。
///
/// System.IO.Compression の ZipArchive は常に UTF-8 フラグを立てるため、
/// 昔の日本語 Windows が作った「CP932 の名前 + フラグ無し」という
/// 肝心のケースを再現できない。そこでバイト列を直接組み立てる。
/// </summary>
internal static class TestZipBuilder
{
    /// <param name="entries">(ファイル名のバイト列, 中身)</param>
    /// <param name="utf8Flag">汎用目的ビット 11 を立てるか</param>
    public static void Write(string path, IReadOnlyList<(byte[] NameBytes, byte[] Content)> entries, bool utf8Flag)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);

        var centralRecords = new List<byte[]>();
        ushort flags = (ushort)(utf8Flag ? 0x0800 : 0x0000);

        foreach ((byte[] nameBytes, byte[] content) in entries)
        {
            long localOffset = stream.Position;
            uint crc = Crc32(content);

            var local = new byte[30];
            BinaryPrimitives.WriteUInt32LittleEndian(local, 0x04034B50);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(4), 20);      // version needed
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(6), flags);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(8), 0);       // stored
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(10), 0);      // time
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(12), 0x2100); // 1996-08-01
            BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(14), crc);
            BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(18), (uint)content.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(22), (uint)content.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(26), (ushort)nameBytes.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(28), 0);      // extra length

            stream.Write(local);
            stream.Write(nameBytes);
            stream.Write(content);

            var central = new byte[46 + nameBytes.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(central, 0x02014B50);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(4), 20);    // version made by (MS-DOS)
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(6), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(8), flags);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(10), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(12), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(14), 0x2100);
            BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(16), crc);
            BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(20), (uint)content.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(24), (uint)content.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(28), (ushort)nameBytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(42), (uint)localOffset);
            nameBytes.CopyTo(central.AsSpan(46));

            centralRecords.Add(central);
        }

        long centralStart = stream.Position;
        foreach (byte[] record in centralRecords) stream.Write(record);
        long centralSize = stream.Position - centralStart;

        var eocd = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(eocd, 0x06054B50);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(8), (ushort)entries.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(10), (ushort)entries.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(12), (uint)centralSize);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(16), (uint)centralStart);
        stream.Write(eocd);
    }

    public static byte[] EncodeName(int codePage, string name)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(codePage).GetBytes(name);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }
}
