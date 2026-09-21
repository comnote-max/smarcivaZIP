using System.Runtime.InteropServices;

namespace SmarcivaZip.Core.Safety;

/// <summary>
/// ファイルをごみ箱へ送る。完全削除は行わない。
/// 「展開したら書庫を消す」設定を使うユーザーでも、間違いを取り消せるようにするため。
/// </summary>
public static class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT fileOp);

    public static bool TryMoveToRecycleBin(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return false;

        // pFrom は二重の NUL で終端する必要がある（複数ファイルを列挙できる形式のため）。
        var operation = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = Path.GetFullPath(path) + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
        };

        try
        {
            return SHFileOperation(ref operation) == 0 && !operation.fAnyOperationsAborted;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }
}
