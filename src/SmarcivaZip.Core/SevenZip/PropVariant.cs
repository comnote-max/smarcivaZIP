using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace SmarcivaZip.Core.SevenZip;

/// <summary>
/// COM の PROPVARIANT。7z.dll はプロパティの取得・設定をすべてこの型で行う。
/// x64 の実際のサイズは 24 バイトなので、呼び出し先が書き込む領域を必ず確保するため
/// 明示レイアウトで 24 バイトに固定している（小さすぎるとスタックを壊す）。
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct PropVariant
{
    [FieldOffset(0)] public ushort VarType;
    [FieldOffset(8)] public IntPtr PointerValue;
    [FieldOffset(8)] public byte ByteValue;
    [FieldOffset(8)] public short Int16Value;
    [FieldOffset(8)] public ushort UInt16Value;
    [FieldOffset(8)] public int Int32Value;
    [FieldOffset(8)] public uint UInt32Value;
    [FieldOffset(8)] public long Int64Value;
    [FieldOffset(8)] public ulong UInt64Value;
    [FieldOffset(8)] public FILETIME FileTimeValue;
    [FieldOffset(16)] private long _tail;

    public VarEnum Type => (VarEnum)VarType;
    public bool IsEmpty => VarType is (ushort)VarEnum.VT_EMPTY or (ushort)VarEnum.VT_NULL;

    public object? GetObject()
    {
        switch (Type)
        {
            case VarEnum.VT_EMPTY:
            case VarEnum.VT_NULL:
                return null;
            case VarEnum.VT_BSTR:
            case VarEnum.VT_LPWSTR:
                return PointerValue == IntPtr.Zero ? null : Marshal.PtrToStringBSTR(PointerValue);
            case VarEnum.VT_BOOL:
                return Int16Value != 0;
            case VarEnum.VT_UI1:
                return ByteValue;
            case VarEnum.VT_I2: return Int16Value;
            case VarEnum.VT_UI2: return UInt16Value;
            case VarEnum.VT_I4: case VarEnum.VT_INT: return Int32Value;
            case VarEnum.VT_UI4: case VarEnum.VT_UINT: return UInt32Value;
            case VarEnum.VT_I8: return Int64Value;
            case VarEnum.VT_UI8: return UInt64Value;
            case VarEnum.VT_FILETIME:
                return ToDateTime();
            default:
                return null;
        }
    }

    public DateTime? ToDateTime()
    {
        if (Type != VarEnum.VT_FILETIME) return null;
        long ticks = ((long)FileTimeValue.dwHighDateTime << 32) | (uint)FileTimeValue.dwLowDateTime;
        if (ticks <= 0) return null;
        try { return DateTime.FromFileTimeUtc(ticks).ToLocalTime(); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    public string? AsString() => GetObject() as string;

    public bool AsBool(bool fallback = false)
        => GetObject() switch { bool b => b, int i => i != 0, uint u => u != 0, _ => fallback };

    public ulong AsUInt64(ulong fallback = 0) => GetObject() switch
    {
        ulong v => v,
        long v => (ulong)v,
        uint v => v,
        int v => (ulong)v,
        ushort v => v,
        byte v => v,
        _ => fallback
    };

    public uint AsUInt32(uint fallback = 0) => (uint)AsUInt64(fallback);

    /// <summary>
    /// 7-Zip はシグネチャなどの生バイト列を BSTR に詰めて返す。
    /// 文字列としてではなくバイト列として読み出す。
    /// </summary>
    public byte[] AsBytes()
    {
        if (Type != VarEnum.VT_BSTR || PointerValue == IntPtr.Zero) return [];
        int byteLen = (int)NativeMethods.SysStringByteLen(PointerValue);
        if (byteLen <= 0) return [];
        var buffer = new byte[byteLen];
        Marshal.Copy(PointerValue, buffer, 0, byteLen);
        return buffer;
    }

    public Guid AsGuid()
    {
        var bytes = AsBytes();
        return bytes.Length == 16 ? new Guid(bytes) : Guid.Empty;
    }

    public void Clear()
    {
        if (Type is VarEnum.VT_BSTR or VarEnum.VT_LPWSTR or VarEnum.VT_LPSTR
            or VarEnum.VT_UNKNOWN or VarEnum.VT_DISPATCH or VarEnum.VT_CLSID)
        {
            NativeMethods.PropVariantClear(ref this);
        }
        VarType = (ushort)VarEnum.VT_EMPTY;
        Int64Value = 0;
        _tail = 0;
    }

    public static PropVariant FromString(IntPtr bstr)
        => new() { VarType = (ushort)VarEnum.VT_BSTR, PointerValue = bstr };

    public static PropVariant FromUInt32(uint value)
        => new() { VarType = (ushort)VarEnum.VT_UI4, UInt32Value = value };

    public static PropVariant FromUInt64(ulong value)
        => new() { VarType = (ushort)VarEnum.VT_UI8, UInt64Value = value };

    public static PropVariant FromBool(bool value)
        => new() { VarType = (ushort)VarEnum.VT_BOOL, Int16Value = (short)(value ? -1 : 0) };

    public static PropVariant FromFileTime(DateTime value)
    {
        long ft = value.ToUniversalTime().ToFileTimeUtc();
        return new PropVariant
        {
            VarType = (ushort)VarEnum.VT_FILETIME,
            FileTimeValue = new FILETIME
            {
                dwLowDateTime = (int)(ft & 0xFFFFFFFF),
                dwHighDateTime = (int)(ft >> 32)
            }
        };
    }

    public static PropVariant Empty => new() { VarType = (ushort)VarEnum.VT_EMPTY };
}
