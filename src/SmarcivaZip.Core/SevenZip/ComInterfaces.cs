using System.Runtime.InteropServices;

namespace SmarcivaZip.Core.SevenZip;

// 7-Zip の IID は {23170F69-40C1-278A-0000-000<group><index>0000} という規則で並んでいる。
// メソッドの宣言順は 7-Zip の IArchive.h / IStream.h / IPassword.h の定義順と
// 完全に一致していなければならない（vtable 順に呼ばれるため）。

[ComImport, Guid("23170F69-40C1-278A-0000-000000050000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IProgress
{
    [PreserveSig] int SetTotal(ulong total);
    [PreserveSig] int SetCompleted(in ulong completeValue);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000300010000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISequentialInStream
{
    [PreserveSig] int Read([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] data, uint size, IntPtr processedSize);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000300020000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISequentialOutStream
{
    [PreserveSig] int Write([In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] data, uint size, IntPtr processedSize);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000300030000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInStream
{
    [PreserveSig] int Read([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] data, uint size, IntPtr processedSize);
    [PreserveSig] int Seek(long offset, uint seekOrigin, IntPtr newPosition);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000300040000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IOutStream
{
    [PreserveSig] int Write([In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] data, uint size, IntPtr processedSize);
    [PreserveSig] int Seek(long offset, uint seekOrigin, IntPtr newPosition);
    [PreserveSig] int SetSize(long newSize);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000600100000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IArchiveOpenCallback
{
    [PreserveSig] int SetTotal(IntPtr files, IntPtr bytes);
    [PreserveSig] int SetCompleted(IntPtr files, IntPtr bytes);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000500100000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ICryptoGetTextPassword
{
    [PreserveSig] int CryptoGetTextPassword([MarshalAs(UnmanagedType.BStr)] out string password);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000500110000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ICryptoGetTextPassword2
{
    [PreserveSig] int CryptoGetTextPassword2(out int passwordIsDefined, [MarshalAs(UnmanagedType.BStr)] out string password);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000600200000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IArchiveExtractCallback
{
    // IProgress
    [PreserveSig] int SetTotal(ulong total);
    [PreserveSig] int SetCompleted(in ulong completeValue);
    // IArchiveExtractCallback
    [PreserveSig] int GetStream(uint index,
        [MarshalAs(UnmanagedType.Interface)] out ISequentialOutStream? outStream, AskMode askExtractMode);
    [PreserveSig] int PrepareOperation(AskMode askExtractMode);
    [PreserveSig] int SetOperationResult(OperationResult resultEOperationResult);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000600800000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IArchiveUpdateCallback
{
    // IProgress
    [PreserveSig] int SetTotal(ulong total);
    [PreserveSig] int SetCompleted(in ulong completeValue);
    // IArchiveUpdateCallback
    [PreserveSig] int GetUpdateItemInfo(uint index, ref int newData, ref int newProperties, ref uint indexInArchive);
    [PreserveSig] int GetProperty(uint index, ItemPropId propId, ref PropVariant value);
    [PreserveSig] int GetStream(uint index, [MarshalAs(UnmanagedType.Interface)] out ISequentialInStream? inStream);
    [PreserveSig] int SetOperationResult(int operationResult);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000600030000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISetProperties
{
    [PreserveSig] int SetProperties(IntPtr names, IntPtr values, uint numProperties);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000600600000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInArchive
{
    [PreserveSig] int Open(IInStream stream, in ulong maxCheckStartPosition,
        [MarshalAs(UnmanagedType.Interface)] IArchiveOpenCallback? openArchiveCallback);
    [PreserveSig] int Close();
    [PreserveSig] int GetNumberOfItems(out uint numItems);
    [PreserveSig] int GetProperty(uint index, ItemPropId propId, ref PropVariant value);
    [PreserveSig] int Extract([MarshalAs(UnmanagedType.LPArray)] uint[]? indices, uint numItems, int testMode,
        [MarshalAs(UnmanagedType.Interface)] IArchiveExtractCallback extractCallback);
    [PreserveSig] int GetArchiveProperty(ItemPropId propId, ref PropVariant value);
    [PreserveSig] int GetNumberOfProperties(out uint numProperties);
    [PreserveSig] int GetPropertyInfo(uint index,
        [MarshalAs(UnmanagedType.BStr)] out string name, out ItemPropId propId, out ushort varType);
    [PreserveSig] int GetNumberOfArchiveProperties(out uint numProperties);
    [PreserveSig] int GetArchivePropertyInfo(uint index,
        [MarshalAs(UnmanagedType.BStr)] out string name, out ItemPropId propId, out ushort varType);
}

[ComImport, Guid("23170F69-40C1-278A-0000-000600A00000")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IOutArchive
{
    [PreserveSig] int UpdateItems([MarshalAs(UnmanagedType.Interface)] ISequentialOutStream outStream,
        uint numItems, [MarshalAs(UnmanagedType.Interface)] IArchiveUpdateCallback updateCallback);
    [PreserveSig] int GetFileTimeType(IntPtr type);
}
