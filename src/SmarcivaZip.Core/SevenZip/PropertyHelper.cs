using System.Runtime.InteropServices;

namespace SmarcivaZip.Core.SevenZip;

/// <summary>
/// IInArchive / ISetProperties とのやりとりで毎回必要になる PROPVARIANT の
/// 確保・解放をまとめたもの。解放を忘れると BSTR がリークするので必ずここを通す。
/// </summary>
internal static class PropertyHelper
{
    public static object? GetItemProperty(IInArchive archive, uint index, ItemPropId id)
    {
        PropVariant value = PropVariant.Empty;
        try
        {
            return archive.GetProperty(index, id, ref value) != 0 ? null : value.GetObject();
        }
        finally { value.Clear(); }
    }

    public static string? GetItemString(IInArchive archive, uint index, ItemPropId id)
        => GetItemProperty(archive, index, id) as string;

    public static bool GetItemBool(IInArchive archive, uint index, ItemPropId id, bool fallback = false)
    {
        PropVariant value = PropVariant.Empty;
        try
        {
            return archive.GetProperty(index, id, ref value) != 0 ? fallback : value.AsBool(fallback);
        }
        finally { value.Clear(); }
    }

    public static ulong GetItemUInt64(IInArchive archive, uint index, ItemPropId id, ulong fallback = 0)
    {
        PropVariant value = PropVariant.Empty;
        try
        {
            return archive.GetProperty(index, id, ref value) != 0 ? fallback : value.AsUInt64(fallback);
        }
        finally { value.Clear(); }
    }

    public static uint? GetItemUInt32OrNull(IInArchive archive, uint index, ItemPropId id)
    {
        PropVariant value = PropVariant.Empty;
        try
        {
            if (archive.GetProperty(index, id, ref value) != 0 || value.IsEmpty) return null;
            return value.AsUInt32();
        }
        finally { value.Clear(); }
    }

    public static DateTime? GetItemDateTime(IInArchive archive, uint index, ItemPropId id)
    {
        PropVariant value = PropVariant.Empty;
        try
        {
            return archive.GetProperty(index, id, ref value) != 0 ? null : value.ToDateTime();
        }
        finally { value.Clear(); }
    }

    public static object? GetArchiveProperty(IInArchive archive, ItemPropId id)
    {
        PropVariant value = PropVariant.Empty;
        try
        {
            return archive.GetArchiveProperty(id, ref value) != 0 ? null : value.GetObject();
        }
        finally { value.Clear(); }
    }

    /// <summary>
    /// 7-Zip のハンドラに "-m" 相当の設定を渡す。
    /// 展開側では zip の "cp"（ファイル名のコードページ）、
    /// 圧縮側では "x"（圧縮レベル）や "m"（アルゴリズム）などを指定する。
    /// </summary>
    public static void SetProperties(object handler, IReadOnlyList<(string Name, PropVariant Value)> properties)
    {
        if (handler is not ISetProperties setter || properties.Count == 0) return;

        int count = properties.Count;
        IntPtr namesBlock = Marshal.AllocHGlobal(IntPtr.Size * count);
        var nameStrings = new IntPtr[count];
        IntPtr valuesBlock = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>() * count);

        try
        {
            for (int i = 0; i < count; i++)
            {
                nameStrings[i] = Marshal.StringToBSTR(properties[i].Name);
                Marshal.WriteIntPtr(namesBlock, IntPtr.Size * i, nameStrings[i]);
                Marshal.StructureToPtr(properties[i].Value,
                    valuesBlock + Marshal.SizeOf<PropVariant>() * i, false);
            }

            setter.SetProperties(namesBlock, valuesBlock, (uint)count);
        }
        finally
        {
            foreach (IntPtr bstr in nameStrings)
            {
                if (bstr != IntPtr.Zero) Marshal.FreeBSTR(bstr);
            }

            // 値が文字列の場合、呼び出し側が確保した BSTR もここで解放する。
            foreach ((_, PropVariant value) in properties)
            {
                if (value.Type == System.Runtime.InteropServices.VarEnum.VT_BSTR
                    && value.PointerValue != IntPtr.Zero)
                {
                    Marshal.FreeBSTR(value.PointerValue);
                }
            }
            Marshal.FreeHGlobal(namesBlock);
            Marshal.FreeHGlobal(valuesBlock);
        }
    }
}
