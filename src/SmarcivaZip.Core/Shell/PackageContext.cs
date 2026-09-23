using System.Runtime.InteropServices;

namespace SmarcivaZip.Core.Shell;

/// <summary>
/// MSIX パッケージ（Microsoft Store 版）として動いているかどうか。
///
/// ストア版では関連付けも右クリックメニューもパッケージの定義で Windows が管理する。
/// レジストリに自分で書く従来の登録は、パッケージの中では書き込みが仮想化されて
/// エクスプローラーからは見えないので、やっても効かない。どちらの流儀で動くかをここで決める。
/// </summary>
public static class PackageContext
{
    private const int ErrorInsufficientBuffer = 122;

    private static readonly Lazy<string?> FamilyNameValue = new(QueryFamilyName);

    /// <summary>パッケージとして動いていれば true。</summary>
    public static bool IsPackaged => FamilyName is not null;

    /// <summary>パッケージファミリー名。パッケージ外では null。</summary>
    public static string? FamilyName => FamilyNameValue.Value;

    /// <summary>
    /// このパッケージ専用のデータフォルダ（ApplicationData の LocalFolder と同じ場所）。
    /// 右クリックメニューの部品（別プロセス）とアプリの両方が、同じ場所として解決できる。
    /// </summary>
    public static string? LocalStatePath => FamilyName is null
        ? null
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages", FamilyName, "LocalState");

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFamilyName(ref int packageFamilyNameLength, char[]? packageFamilyName);

    private static string? QueryFamilyName()
    {
        try
        {
            int length = 0;
            int result = GetCurrentPackageFamilyName(ref length, null);

            // パッケージ外では APPMODEL_ERROR_NO_PACKAGE (15700) が返る。
            if (result != ErrorInsufficientBuffer || length <= 0) return null;

            var buffer = new char[length];
            if (GetCurrentPackageFamilyName(ref length, buffer) != 0) return null;

            return new string(buffer, 0, Math.Max(0, length - 1));
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }
}
