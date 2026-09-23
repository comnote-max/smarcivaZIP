#requires -Version 5.1
<#
.SYNOPSIS
    ストア版の右クリックメニュー部品を、エクスプローラーと同じ呼び方で直接呼び出して確かめる。

.DESCRIPTION
    MSIX を登録（build-msix.ps1 -Register）したあとで使う。
    CLSID から部品を作り、項目の一覧と、選んだものに応じた表示・非表示を出力する。
    -InvokeTitle を付けると、その名前の項目を実行する（実際にアプリが起動して処理する）。

    PowerShell の COM の扱いでは IExplorerCommand への型変換がうまくいかないので、
    呼び出しはすべて C# 側で行う。

.EXAMPLE
    ./tools/test-shellext.ps1 -Paths C:\temp\a.zip, C:\temp\note.txt
    ./tools/test-shellext.ps1 -Paths C:\temp\note.txt -InvokeTitle 'ZIP に圧縮'
#>
param(
    [string[]]$Paths = @(),
    [string]$InvokeTitle = ''
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

[ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerCommand
{
    void GetTitle(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] out string name);
    void GetIcon(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] out string icon);
    void GetToolTip(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] out string tip);
    void GetCanonicalName(out Guid name);
    void GetState(IntPtr items, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow, out uint state);
    void Invoke(IntPtr items, IntPtr bindCtx);
    void GetFlags(out uint flags);
    void EnumSubCommands(out IEnumExplorerCommand commands);
}

[ComImport, Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEnumExplorerCommand
{
    [PreserveSig] int Next(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IExplorerCommand[] commands, out uint fetched);
    [PreserveSig] int Skip(uint count);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out IEnumExplorerCommand copy);
}

public static class MenuTester
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    static extern int SHCreateShellItemArrayFromIDLists(uint count, IntPtr[] pidls, out IntPtr array);

    static IntPtr CreateItems(string[] paths)
    {
        if (paths.Length == 0) return IntPtr.Zero;
        var pidls = new IntPtr[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            uint ignored;
            Marshal.ThrowExceptionForHR(SHParseDisplayName(paths[i], IntPtr.Zero, out pidls[i], 0, out ignored));
        }
        IntPtr array;
        Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)pidls.Length, pidls, out array));
        foreach (IntPtr pidl in pidls) Marshal.FreeCoTaskMem(pidl);
        return array;
    }

    public static string Run(string[] paths, string invokeTitle)
    {
        var report = new StringBuilder();
        Type type = Type.GetTypeFromCLSID(new Guid("F25B0869-E77F-4627-AAA1-A00B930517F8"));
        var root = (IExplorerCommand)Activator.CreateInstance(type);
        IntPtr items = CreateItems(paths);

        string title, icon;
        uint flags;
        root.GetTitle(items, out title);
        root.GetFlags(out flags);
        root.GetIcon(items, out icon);
        report.AppendLine("root: '" + title + "' flags=" + flags + " icon=" + icon);

        IEnumExplorerCommand commands;
        root.EnumSubCommands(out commands);
        var buffer = new IExplorerCommand[1];
        uint fetched;
        IExplorerCommand target = null;

        while (commands.Next(1, buffer, out fetched) == 0 && fetched == 1)
        {
            IExplorerCommand command = buffer[0];
            uint itemFlags;
            command.GetFlags(out itemFlags);
            if ((itemFlags & 8) != 0) { report.AppendLine("  ------"); continue; }

            string itemTitle;
            uint state;
            command.GetTitle(items, out itemTitle);
            command.GetState(items, true, out state);
            // EXPCMDSTATE: 0 = 表示、2 = ECS_HIDDEN（非表示）
            report.AppendLine(string.Format("  {0,-7} {1}", (state & 2) != 0 ? "HIDDEN" : "shown", itemTitle));
            if (invokeTitle.Length > 0 && itemTitle == invokeTitle) target = command;
        }

        if (invokeTitle.Length > 0)
        {
            if (target == null) throw new InvalidOperationException("No menu item titled '" + invokeTitle + "'.");
            target.Invoke(items, IntPtr.Zero);
            report.AppendLine("invoked: " + invokeTitle);
        }

        return report.ToString();
    }
}
'@

[MenuTester]::Run([string[]]$Paths, $InvokeTitle)
