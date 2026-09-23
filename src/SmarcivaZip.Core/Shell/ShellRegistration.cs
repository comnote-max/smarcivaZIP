using Microsoft.Win32;
using SmarcivaZip.Core.Compression;
using SmarcivaZip.Core.Localization;

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
    /// <summary>ProgID の共通の接頭辞。解除のときはこれで始まるものをすべて消す。</summary>
    public const string ProgIdPrefix = "smarcivaZIP.";

    private const string MenuKeyName = "smarcivaZIP";
    private const string ClassesRoot = @"Software\Classes";

    /// <summary>アイコンを置くフォルダ名（実行ファイルからの相対）。</summary>
    private const string IconDirectoryName = "Icons";

    /// <summary>メニューに出す解凍の動作。</summary>
    private static readonly (string Verb, string LabelKey, string Argument)[] ExtractVerbs =
    [
        ("10extract-auto", "Menu_ExtractHere", "--extract"),
        ("11extract-folder", "Menu_ExtractToFolder", "--extract-to-folder"),
        ("12extract-preview", "Menu_ExtractPreview", "--extract-preview")
    ];

    public static bool IsRegistered() => FindRegisteredProgIds().Count > 0;

    /// <summary>登録済みの ProgID を列挙する。</summary>
    private static IReadOnlyList<string> FindRegisteredProgIds()
    {
        try
        {
            using RegistryKey? classes = Registry.CurrentUser.OpenSubKey(ClassesRoot);
            if (classes is null) return [];

            return classes.GetSubKeyNames()
                .Where(name => name.StartsWith(ProgIdPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
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
        IReadOnlyList<CompressMenuItem> compressMenuItems, IReadOnlyList<string>? knownExtensions = null)
    {
        // 使う分類の ProgID だけを作る。使わないものまで作ると、
        // 解除し忘れたときにレジストリに残り続ける。
        foreach (ArchiveFileType type in ArchiveFileType.UsedBy(extensions))
        {
            RegisterProgId(executablePath, type);
        }

        RegisterFileAssociations(extensions);

        if (knownExtensions is not null)
        {
            var selected = extensions.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
            RemoveFileAssociations(knownExtensions.Where(e => !selected.Contains(Normalize(e))));
        }

        RegisterContextMenu(executablePath, compressMenuItems, extensions);
        NotifyShell();
    }

    private static string Normalize(string extension) => extension.TrimStart('.').ToLowerInvariant();

    /// <summary>
    /// 関連付けたつもりでも、実際にはダブルクリックで別のアプリが開く拡張子を返す。
    ///
    /// どのアプリを既定にするかは UserChoice というハッシュで保護された領域が握っており、
    /// アプリ側からは変更できない（変更すべきでもない）。
    /// 黙って効かないままにすると「登録したのに開かない」と受け取られるので、
    /// どの拡張子が誰に取られているかを名指しで返す。
    /// </summary>
    public static IReadOnlyList<(string Extension, string Owner)> FindExtensionsOwnedByOthers(
        IReadOnlyList<string> extensions)
    {
        var owned = new List<(string, string)>();

        foreach (string extension in extensions)
        {
            string normalized = Normalize(extension);
            if (normalized.Length == 0) continue;

            string? owner = ResolveCurrentOwner(normalized);
            if (owner is not null && !owner.StartsWith(ProgIdPrefix, StringComparison.OrdinalIgnoreCase))
                owned.Add((normalized, owner));
        }

        return owned;
    }

    /// <summary>
    /// その拡張子を今どのアプリが開くか。
    /// UserChoice があればそれが最優先で、無ければ HKCU / HKCR の既定値を見る。
    /// </summary>
    private static string? ResolveCurrentOwner(string extension)
    {
        try
        {
            using (RegistryKey? choice = Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.{extension}\UserChoice"))
            {
                if (choice?.GetValue("ProgId") is string chosen && chosen.Length > 0) return chosen;
            }

            using (RegistryKey? user = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\.{extension}"))
            {
                if (user?.GetValue(null) is string userDefault && userDefault.Length > 0) return userDefault;
            }

            using RegistryKey? machine = Registry.ClassesRoot.OpenSubKey($".{extension}");
            if (machine?.GetValue(null) is string machineDefault && machineDefault.Length > 0)
                return machineDefault;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
        }

        return null;
    }

    /// <summary>
    /// smarcivaZIP が書いたものを残らず取り除く。アンインストール時にも呼ばれる。
    ///
    /// 関連付けた拡張子は設定ファイルの一覧に頼らず、レジストリを総当たりして探す。
    /// 設定から外したあとの拡張子や、設定ファイルが消えた後の状態でも取り残さないためで、
    /// アンインストーラが「一覧に載っていた分だけ消す」では、きれいに消えたことにならない。
    /// </summary>
    public static void Unregister()
    {
        using (RegistryKey? classes = Registry.CurrentUser.OpenSubKey(ClassesRoot, writable: true))
        {
            if (classes is null) return;

            // 過去の版が作った ProgID も残さず消せるよう、接頭辞で総当たりする。
            foreach (string progId in FindRegisteredProgIds())
            {
                DeleteSubKeyTreeIfExists(classes, progId);
            }

            DeleteSubKeyTreeIfExists(classes, $@"*\shell\{MenuKeyName}");
            DeleteSubKeyTreeIfExists(classes, $@"Directory\shell\{MenuKeyName}");

            // 「プログラムから開く」で実行ファイルを直接選ぶと Windows が作る登録。
            // 本体を消したあとに残ると、一覧に開けない smarcivaZIP が出続ける。
            DeleteSubKeyTreeIfExists(classes, @"Applications\SmarcivaZip.exe");

            RemoveFileAssociations(classes.GetSubKeyNames().Where(name => name.StartsWith('.')));
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

            string keyPath = $@"{ClassesRoot}\.{normalized}";
            bool touched = false;

            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true))
            {
                if (key is null) continue;

                using (RegistryKey? progIds = key.OpenSubKey("OpenWithProgids", writable: true))
                {
                    if (progIds is not null)
                    {
                        foreach (string name in progIds.GetValueNames())
                        {
                            if (!name.StartsWith(ProgIdPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                            progIds.DeleteValue(name, throwOnMissingValue: false);
                            touched = true;
                        }
                    }
                }

                // 既定として自分を書いていた場合だけ消す。他のアプリの設定は触らない。
                if (key.GetValue(null) is string current
                    && current.StartsWith(ProgIdPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    key.DeleteValue(string.Empty, throwOnMissingValue: false);
                    touched = true;
                }
            }

            // 自分が作った空の入れ物も片付ける。ただし自分が触った拡張子に限り、
            // 中身が本当に空のときだけにする。他のアプリが何か置いていれば残る。
            if (touched)
            {
                DeleteKeyIfEmpty($@"{keyPath}\OpenWithProgids");
                DeleteKeyIfEmpty(keyPath);
            }
        }
    }

    /// <summary>値もサブキーも無いキーだけを消す。</summary>
    private static void DeleteKeyIfEmpty(string path)
    {
        try
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(path))
            {
                if (key is null || key.ValueCount > 0 || key.SubKeyCount > 0) return;
            }

            Registry.CurrentUser.DeleteSubKey(path, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            // 消せなくても害は無い。空のキーが残るだけ。
        }
    }

    private static void RegisterProgId(string executablePath, ArchiveFileType type)
    {
        using RegistryKey progId = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{type.ProgId}");
        progId.SetValue(null, type.DisplayName);
        progId.SetValue("FriendlyTypeName", type.FriendlyName);

        using (RegistryKey icon = progId.CreateSubKey("DefaultIcon"))
        {
            icon.SetValue(null, $"\"{ResolveIconPath(executablePath, type)}\"");
        }

        using RegistryKey command = progId.CreateSubKey(@"shell\open\command");
        command.SetValue(null, $"\"{executablePath}\" \"%1\"");
    }

    /// <summary>
    /// その分類のアイコンファイルの場所。
    ///
    /// アイコンは実行ファイルに埋め込まず、隣の Icons フォルダに置いている。
    /// exe に複数のアイコンを埋め込むには Win32 リソースを自前で用意する必要があり、
    /// .NET SDK からは素直に扱えないため。外に出しておくと利用者が差し替えられる利点もある。
    /// 見つからない場合は実行ファイル自身のアイコンに落とす。
    /// </summary>
    private static string ResolveIconPath(string executablePath, ArchiveFileType type)
    {
        string? directory = Path.GetDirectoryName(executablePath);
        if (directory is null) return executablePath + ",0";

        string icon = Path.Combine(directory, IconDirectoryName, type.IconFileName);
        return File.Exists(icon) ? icon : executablePath + ",0";
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

            string progId = ArchiveFileType.ForExtension(normalized).ProgId;

            using RegistryKey key = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\.{normalized}");
            using RegistryKey progIds = key.CreateSubKey("OpenWithProgids");

            // 前回の登録で別の ProgID を割り当てていた場合、それを先に落とす。
            // 残すと「プログラムから開く」に smarcivaZIP が二重に並ぶ。
            // 分類を作り変えたときや、旧版から更新したときに起きる。
            foreach (string existing in progIds.GetValueNames())
            {
                if (existing.StartsWith(ProgIdPrefix, StringComparison.OrdinalIgnoreCase)
                    && !existing.Equals(progId, StringComparison.OrdinalIgnoreCase))
                {
                    progIds.DeleteValue(existing, throwOnMissingValue: false);
                }
            }

            progIds.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);

            // 既定も古い ProgID のままなら、新しい方へ付け替える。
            if (key.GetValue(null) is string current
                && current.StartsWith(ProgIdPrefix, StringComparison.OrdinalIgnoreCase)
                && !current.Equals(progId, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(null, progId);
            }

            // まだ誰も関連付けていない拡張子（.lzh など）なら、そのまま既定にできる。
            if (key.GetValue(null) is null or "") key.SetValue(null, progId);
        }
    }

    private static void RegisterContextMenu(
        string executablePath, IReadOnlyList<CompressMenuItem> compressMenuItems,
        IReadOnlyList<string> extensions)
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

            // 並び順は設定どおり。レジストリはキー名の昇順で並ぶので、
            // 意図した順番を保つために連番を頭に付ける。
            int order = 0;
            foreach (CompressMenuItem item in compressMenuItems)
            {
                AddVerb(items, executablePath,
                    verb: $"{order:D2}compress-{item.Id}",
                    label: item.Label,
                    arguments: item.Arguments);
                order++;
            }

            // ファイルに対してだけ解凍系を出す。フォルダには意味が無い。
            if (target == "*")
            {
                string appliesTo = BuildAppliesToQuery(extensions);

                foreach ((string verb, string labelKey, string argument) in ExtractVerbs)
                {
                    AddVerb(items, executablePath, verb, Strings.Get(labelKey), argument, appliesTo);
                }
            }

            // 設定画面は選択中のファイルと関係ないので、パスを渡さない。
            AddVerb(items, executablePath, "90settings", Strings.Get("Menu_Settings"),
                "--settings", takesPath: false);
        }
    }

    private static void AddVerb(
        RegistryKey parent, string executablePath,
        string verb, string label, string arguments,
        string? appliesTo = null, bool takesPath = true)
    {
        using RegistryKey key = parent.CreateSubKey(verb);
        key.SetValue("MUIVerb", label);

        if (appliesTo is not null) key.SetValue("AppliesTo", appliesTo);

        // 複数選択したとき、Windows はファイルごとにコマンドを起動する。
        // アプリ側で短時間だけ束ねて 1 つのアーカイブにまとめる
        // （SelectionCoalescer 参照）。
        key.SetValue("MultiSelectModel", "Document");

        using RegistryKey command = key.CreateSubKey("command");

        command.SetValue(null, takesPath
            ? $"\"{executablePath}\" {arguments} \"%1\""
            : $"\"{executablePath}\" {arguments}");
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
