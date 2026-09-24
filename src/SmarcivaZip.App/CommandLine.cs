using System.IO;
using SmarcivaZip.Core.Localization;

namespace SmarcivaZip.App;

public enum AppMode
{
    /// <summary>引数なし。設定画面を出す。</summary>
    Settings,

    /// <summary>
    /// 動作の指定が無くパスだけある。書庫なら展開、それ以外なら圧縮（<see cref="Core.Shell.DropAction"/>）。
    /// アイコンへのドロップと、関連付けからのダブルクリックがこれになる。
    /// </summary>
    Auto,

    /// <summary>アーカイブを展開する。</summary>
    Extract,

    /// <summary>展開前に文字コードのプレビューを必ず出す。</summary>
    ExtractWithPreview,

    /// <summary>圧縮する。</summary>
    Compress,

    Register,
    Unregister,
    Help
}

/// <summary>
/// コマンドラインの解釈。
/// 右クリックメニューのレジストリ登録がそのままここの引数を叩くので、
/// 形は ShellRegistration と対で決まっている。
/// </summary>
public sealed class CommandLine
{
    public AppMode Mode { get; private init; } = AppMode.Settings;
    public List<string> Paths { get; } = [];
    public string? FormatId { get; private init; }
    public bool AskPassword { get; private init; }

    /// <summary>展開先フォルダを必ず作る（--extract-to-folder）。</summary>
    public bool ForceOutputFolder { get; private init; }

    /// <summary>
    /// 完了や失敗をダイアログで知らせず、終了コードだけで返す（--quiet）。
    /// インストーラとアンインストーラが登録・解除に使う。サイレントインストールの途中で
    /// 「登録しました」の OK 待ちになると、そこで止まって先に進まなくなるため。
    /// </summary>
    public bool Quiet { get; private init; }

    /// <summary>
    /// パスの一覧をファイルで受け取った（--paths-from）。
    ///
    /// ストア版の右クリックメニューは、選ばれた全ファイルを一度に渡してくる。
    /// 数百個を選ぶとコマンドラインの長さ（32767 文字）を超えるので、一覧はファイルで受け取る。
    /// この場合は選択がすでに揃っているので、他のプロセスを待って束ねる必要が無い。
    /// </summary>
    public bool PathsComplete { get; private init; }

    public static CommandLine Parse(string[] args)
    {
        AppMode mode = AppMode.Settings;
        string? formatId = null;
        bool askPassword = false;
        bool forceFolder = false;
        bool quiet = false;
        bool pathsComplete = false;
        var paths = new List<string>();
        bool modeSpecified = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            switch (arg)
            {
                case "--extract" or "-x":
                    mode = AppMode.Extract;
                    modeSpecified = true;
                    break;

                case "--extract-to-folder":
                    mode = AppMode.Extract;
                    forceFolder = true;
                    modeSpecified = true;
                    break;

                case "--extract-preview":
                    mode = AppMode.ExtractWithPreview;
                    modeSpecified = true;
                    break;

                case "--compress" or "-c":
                    mode = AppMode.Compress;
                    modeSpecified = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-')) formatId = args[++i];
                    break;

                case "--password" or "-p":
                    askPassword = true;
                    break;

                case "--settings":
                    mode = AppMode.Settings;
                    modeSpecified = true;
                    break;

                case "--register":
                    mode = AppMode.Register;
                    modeSpecified = true;
                    break;

                case "--unregister":
                    mode = AppMode.Unregister;
                    modeSpecified = true;
                    break;

                case "--quiet" or "-q":
                    quiet = true;
                    break;

                case "--paths-from":
                    if (i + 1 < args.Length)
                    {
                        paths.AddRange(ReadPathList(args[++i]));
                        pathsComplete = true;
                    }
                    break;

                case "--help" or "-h" or "/?":
                    mode = AppMode.Help;
                    modeSpecified = true;
                    break;

                default:
                    if (!arg.StartsWith('-')) paths.Add(arg);
                    break;
            }
        }

        // 関連付けからの起動は "SmarcivaZip.exe <アーカイブ>"、アイコンへのドロップは
        // "SmarcivaZip.exe <パス...>" という形で来る。どちらにするかは中身を見て決める。
        if (!modeSpecified && paths.Count > 0) mode = AppMode.Auto;

        var result = new CommandLine
        {
            Mode = mode,
            FormatId = formatId,
            AskPassword = askPassword,
            ForceOutputFolder = forceFolder,
            Quiet = quiet,
            PathsComplete = pathsComplete
        };

        result.Paths.AddRange(paths);
        return result;
    }

    /// <summary>
    /// 1 行 1 パスの UTF-8 ファイルを読み、読み終えたら消す。
    /// 一時フォルダに作られた使い捨てのファイルなので、残しておく理由が無い。
    /// </summary>
    private static IEnumerable<string> ReadPathList(string listPath)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(listPath, System.Text.Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        try { File.Delete(listPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 残っても害は無い */ }

        return lines.Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
    }

    public static string HelpText => Strings.Get("CommandLine_Help");

}
