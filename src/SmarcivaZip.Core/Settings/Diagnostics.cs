using System.Diagnostics;

namespace SmarcivaZip.Core.Settings;

/// <summary>
/// 不具合報告用のログ。
///
/// GUI アプリは標準出力を持たないので、何か起きても利用者に伝える手段が無い。
/// 例外は必ずここに残し、Issue にそのまま貼れるようにしておく。
/// 詳細トレースは環境変数 SMARCIVAZIP_TRACE=1 のときだけ記録する。
/// </summary>
public static class Diagnostics
{
    private static readonly Lock Gate = new();

    public static bool TraceEnabled { get; } =
        Environment.GetEnvironmentVariable("SMARCIVAZIP_TRACE") == "1";

    public static string LogPath => Path.Combine(AppSettings.SettingsDirectory, "smarcivazip.log");

    public static void Trace(string message)
    {
        if (!TraceEnabled) return;
        Write("TRACE", message);
    }

    public static void Error(string context, Exception exception)
        => Write("ERROR", $"{context}: {exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.SettingsDirectory);
                TrimIfLarge();

                File.AppendAllText(LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ログが書けないこと自体でアプリを止めるわけにはいかない。
            Debug.WriteLine(message);
        }
    }

    /// <summary>ログが無限に伸びないよう、1 MB を超えたら作り直す。</summary>
    private static void TrimIfLarge()
    {
        var info = new FileInfo(LogPath);
        if (info.Exists && info.Length > 1024 * 1024) info.Delete();
    }
}
