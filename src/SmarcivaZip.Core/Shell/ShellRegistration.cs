using Microsoft.Win32;
using SmarcivaZip.Core.Compression;

namespace SmarcivaZip.Core.Shell;

/// <summary>
/// 関連付けと右クリックメニューの登録。
///
/// すべて HKEY_CURRENT_USER\Software\Classes に書くので管理者権限は要らない。
/// COM のシェル拡張 DLL も使わない（エクスプローラーにコードを読み込ませないので、
/// 不具合を起こしてもエクスプローラーごと巻き込まない）。
///
/// Windows 11 では、この方式のメニューは「その他のオプションを確認」の下に入る。
/// 第一階層に出すには MSIX の Sparse Package と IExplorerCommand が必要で、
/// コード署名証明書も要るため、それは v2 の課題としている。
/// </summary>
public static class ShellRegistration
{
    public const string ProgId = "smarcivaZIP.Archive";
    private const string MenuKeyName = "smarcivaZIP";
    private const string ClassesRoot = @"Software\Classes";

    /// <summary>メニューに出す解凍の動作。</summary>
    private static readonly (string Verb, string Label, string Argument)[] ExtractVerbs =
    [
        ("10extract-auto", "ここに解凍", "--extract"),
        ("11extract-folder", "フォルダを作って解凍", "--extract-to-folder"),
        ("12extract-preview", "文字コードを選んで解凍...", "--extract-preview")
    ];

    public static bool IsRegistered()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{ProgId}");
        return key is not null;
    }

    /// <summary>
    /// 登録を行う。
    /// </summary>
    /// <param name="extensions">関連付けたい拡張子（ドット無し）。</param>
    /// <param name="knownExtensions">
    /// 設定画面の一覧に並んでいる拡張子すべて。ここに含まれていて
    /// <paramref name="extensions"/> に無いものは、チェックが外されたとみなして
    /// 関連付けを解除する。これをやらないと、一度チェックを入れた拡張子を
    /// 外しても関連付けが残り続けてしまう。
    /// </param>
    public static void Register(string executablePath, IReadOnlyList<string> extensions,
        IReadOnlyList<OutputFormat> formats, IReadOnlyList<string>? knownExtensions = null)
    {
        RegisterProgId(executablePath);
        RegisterFileAssociations(extensions);

        if (knownExtensions is not null)
        {
            var selected = extensions.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
            RemoveFileAssociations(knownExtensions.Where(e => !selected.Contains(Normalize(e))));
        }

        RegisterContextMenu(executablePath, formats, extensions);
        NotifyShell();
    }

    private static string Normalize(string extension) => extension.TrimStart('.').ToLowerInvariant();

    public static void Unregister(IReadOnlyList<string> extensions)
    {
        using (RegistryKey? classes = Registry.CurrentUser.OpenSubKey(ClassesRoot, writable: true))
        {
            if (classes is null) return;

            DeleteSubKeyTreeIfExists(classes, ProgId);
            DeleteSubKeyTreeIfExists(classes, $@"*\shell\{MenuKeyName}");
            DeleteSubKeyTreeIfExists(classes, $@"Directory\shell\{MenuKeyName}");

            RemoveFileAssociations(extensions);
        }

        NotifyShell();
    }

    /// <summary>指定した拡張子から smarcivaZIP の関連付けだけを取り除く。</summary>
    private static void RemoveFileAssociations(IEnumerable<string> extensions)
    {
        foreach (string extension in extensions)
        {
            string normalized = Normalize(extension);
            if (normalized.Length == 0) continue;

            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                $@"{ClassesRoot}\.{normalized}", writable: true);
            if (key is null) continue;

            using (RegistryKey? progIds = key.OpenSubKey("OpenWithProgids", writable: true))
            {
                progIds?.DeleteValue(ProgId, throwOnMissingValue: false);
            }

            // 既定として自分を書いていた場合だけ消す。他のアプリの設定は触らない。
            if (key.GetValue(null) as string == ProgId) key.DeleteValue(string.Empty, throwOnMissingValue: false);
        }
    }

    private static void RegisterProgId(string executablePath)
    {
        using RegistryKey progId = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{ProgId}");
        progId.SetValue(null, "アーカイブ");
        progId.SetValue("FriendlyTypeName", "アーカイブ (smarcivaZIP)");

        using (RegistryKey icon = progId.CreateSubKey("DefaultIcon"))
        {
            icon.SetValue(null, $"\"{executablePath}\",0");
        }

        using RegistryKey command = progId.CreateSubKey(@"shell\open\command");
        command.SetValue(null, $"\"{executablePath}\" \"%1\"");
    }

    /// <summary>
    /// 拡張子との関連付け。
    ///
    /// OpenWithProgids は「候補として出す」という控えめな登録で、必ず成功する。
    /// 既定のアプリそのものは UserChoice というハッシュ保護された領域が握っており、
    /// アプリ側から書き換えるのは Windows の意図に反するので行わない。
    /// 既定にしたいときは設定アプリを開いてもらう。
    /// </summary>
    private static void RegisterFileAssociations(IReadOnlyList<string> extensions)
    {
        foreach (string extension in extensions)
        {
            string normalized = Normalize(extension);
            if (normalized.Length == 0) continue;

            using RegistryKey key = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\.{normalized}");
            using RegistryKey progIds = key.CreateSubKey("OpenWithProgids");
            progIds.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);

            // まだ誰も関連付けていない拡張子（.lzh など）なら、そのまま既定にできる。
            if (key.GetValue(null) is null or "") key.SetValue(null, ProgId);
        }
    }

    private static void RegisterContextMenu(
        string executablePath, IReadOnlyList<OutputFormat> formats, IReadOnlyList<string> extensions)
    {
        foreach (string target in new[] { "*", "Directory" })
        {
            using RegistryKey menu =
                Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{target}\shell\{MenuKeyName}");

            menu.SetValue("MUIVerb", "smarcivaZIP");
            menu.SetValue("Icon", $"\"{executablePath}\",0");

            // SubCommands を空文字にすると、shell サブキーの中身が
            // カスケードメニューとして展開される。COM 実装は不要。
            menu.SetValue("SubCommands", string.Empty);

            using RegistryKey items = menu.CreateSubKey("shell");

            // 既存の項目を消してから作り直す（形式が増減したときに残骸を残さない）。
            foreach (string existing in items.GetSubKeyNames()) items.DeleteSubKeyTree(existing);

            int order = 0;
            foreach (OutputFormat format in formats)
            {
                AddVerb(items, executablePath,
                    verb: $"{order:D2}compress-{format.Id}",
                    label: $"{format.DisplayName} に圧縮",
                    arguments: $"--compress {format.Id}");
                order++;

                if (format.SupportsPassword)
                {
                    AddVerb(items, executablePath,
                        verb: $"{order:D2}compress-{format.Id}-password",
                        label: $"{format.DisplayName} に圧縮（パスワード）",
                        arguments: $"--compress {format.Id} --password");
                    order++;
                }
            }

            // ファイルに対してだけ解凍系を出す。フォルダには意味が無い。
            if (target == "*")
            {
                string appliesTo = BuildAppliesToQuery(extensions);

                foreach ((string verb, string label, string argument) in ExtractVerbs)
                {
                    AddVerb(items, executablePath, verb, label, argument, appliesTo);
                }
            }

            AddVerb(items, executablePath, "90settings", "設定...", "--settings");
        }
    }

    private static void AddVerb(
        RegistryKey parent, string executablePath,
        string verb, string label, string arguments, string? appliesTo = null)
    {
        using RegistryKey key = parent.CreateSubKey(verb);
        key.SetValue("MUIVerb", label);

        if (appliesTo is not null) key.SetValue("AppliesTo", appliesTo);

        // 複数選択したとき、Windows はファイルごとにコマンドを起動する。
        // アプリ側で短時間だけ束ねて 1 つのアーカイブにまとめる
        // （SelectionCoalescer 参照）。
        key.SetValue("MultiSelectModel", "Document");

        using RegistryKey command = key.CreateSubKey("command");
        command.SetValue(null, $"\"{executablePath}\" {arguments} \"%1\"");
    }

    /// <summary>
    /// 解凍系の項目を、アーカイブらしい拡張子のときだけ表示するための条件式。
    /// Windows のプロパティシステムが解釈する。
    /// </summary>
    private static string BuildAppliesToQuery(IReadOnlyList<string> extensions)
    {
        IEnumerable<string> terms = extensions
            .Select(Normalize)
            .Where(e => e.Length > 0)
            .Distinct()
            .Select(e => $"System.FileName:\"*.{e}\"");

        return string.Join(" OR ", terms);
    }

    private static void DeleteSubKeyTreeIfExists(RegistryKey parent, string path)
    {
        try { parent.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
        catch (UnauthorizedAccessException) { /* 消せなくても続行する */ }
    }

    /// <summary>エクスプローラーに関連付けが変わったことを伝える。</summary>
    private static void NotifyShell() => NativeShell.NotifyAssociationChanged();
}
