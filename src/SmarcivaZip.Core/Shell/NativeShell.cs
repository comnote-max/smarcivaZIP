using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SmarcivaZip.Core.Shell;

public static class NativeShell
{
    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    public enum AssocString
    {
        Command = 1,
        FriendlyAppName = 4
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryStringW(
        int flags, int str, string assoc, string? extra, System.Text.StringBuilder? output, ref uint size);

    /// <summary>
    /// その拡張子をダブルクリックしたときに Windows が実際に使うもの（コマンドやアプリ名）を尋ねる。
    /// 利用者の選択（UserChoice）も含めて Windows 自身が解決した結果なので、レジストリを
    /// 自前で読み解くより確か。答えが無ければ null。
    /// </summary>
    public static string? QueryAssociation(string extension, AssocString what)
    {
        try
        {
            uint size = 1024;
            var buffer = new System.Text.StringBuilder((int)size);
            int hr = AssocQueryStringW(0, (int)what, extension, "open", buffer, ref size);
            return hr == 0 && buffer.Length > 0 ? buffer.ToString() : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    /// <summary>関連付けが変わったことをエクスプローラーへ通知する。</summary>
    public static void NotifyAssociationChanged()
    {
        try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); }
        catch (DllNotFoundException) { /* 通知できなくても登録自体は有効 */ }
    }

    /// <summary>エクスプローラーでフォルダを開く。パスを指定すればその項目を選択した状態で開く。</summary>
    public static void RevealInExplorer(string path)
    {
        try
        {
            bool isDirectory = Directory.Exists(path);
            string arguments = isDirectory ? $"\"{path}\"" : $"/select,\"{path}\"";

            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // エクスプローラーが開けなくても処理自体は成功している。
        }
    }

    /// <summary>既定のブラウザーでページを開く。https 以外は開かない。</summary>
    public static void OpenWebPage(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 既定のブラウザーが無い環境。開けないだけで、アプリは使い続けられる。
        }
    }

    /// <summary>
    /// Windows の「既定のアプリ」設定を、できれば smarcivaZIP のページで開く。
    /// そのページの「既定値に設定」を 1 回押せば、対応する拡張子がまとめて smarcivaZIP になる。
    /// 一覧に載っていなければ（登録前など）、既定のアプリの最初のページを開く。
    /// </summary>
    public static void OpenDefaultAppsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo(DefaultAppsUri()) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    private static string DefaultAppsUri()
    {
        const string page = "ms-settings:defaultapps";

        // ストア版はパッケージのアプリ ID（パッケージファミリー名!アプリ ID）で指す。
        if (PackageContext.FamilyName is { } family)
            return $"{page}?registeredAUMID={Uri.EscapeDataString(family + "!smarcivaZIP")}";

        return ShellRegistration.HasDefaultAppsEntry()
            ? $"{page}?registeredAppUser={Uri.EscapeDataString(ShellRegistration.RegisteredAppName)}"
            : page;
    }
}
