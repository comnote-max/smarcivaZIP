using System.Runtime.InteropServices;

namespace SmarcivaZip.Core.SevenZip;

internal static class NativeMethods
{
    internal delegate int CreateObjectDelegate(in Guid classId, in Guid interfaceId, out IntPtr outObject);
    internal delegate int GetNumberOfFormatsDelegate(out uint numFormats);
    internal delegate int GetHandlerProperty2Delegate(uint formatIndex, HandlerPropId propId, ref PropVariant value);
    internal delegate int GetNumberOfMethodsDelegate(out uint numMethods);
    internal delegate int GetMethodPropertyDelegate(uint index, uint propId, ref PropVariant value);
    internal delegate int SetLargePageModeDelegate();

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint SysStringByteLen(IntPtr bstr);

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PropVariant pvar);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetDllDirectoryW(string? path);
}
