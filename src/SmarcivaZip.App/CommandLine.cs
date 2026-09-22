using SmarcivaZip.Core.Localization;

namespace SmarcivaZip.App;

public enum AppMode
{
    /// <summary>引数なし。設定画面を出す。</summary>
    Settings,

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

    public static CommandLine Parse(string[] args)
    {
        AppMode mode = AppMode.Settings;
        string? formatId = null;
        bool askPassword = false;
        bool forceFolder = false;
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

                case "--help" or "-h" or "/?":
                    mode = AppMode.Help;
                    modeSpecified = true;
                    break;

                default:
                    if (!arg.StartsWith('-')) paths.Add(arg);
                    break;
            }
        }

        // 関連付けからの起動は "SmarcivaZip.exe <アーカイブ>" という形で来る。
        if (!modeSpecified && paths.Count > 0) mode = AppMode.Extract;

        var result = new CommandLine
        {
            Mode = mode,
            FormatId = formatId,
            AskPassword = askPassword,
            ForceOutputFolder = forceFolder
        };

        result.Paths.AddRange(paths);
        return result;
    }

    public static string HelpText => Strings.Get("CommandLine_Help");

}
