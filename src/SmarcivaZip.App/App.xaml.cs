using System.IO;
using System.Windows;
using Microsoft.Win32;
using SmarcivaZip.App.Views;
using SmarcivaZip.Core.Compression;
using SmarcivaZip.Core.Extraction;
using SmarcivaZip.Core.Safety;
using SmarcivaZip.Core.SevenZip;
using SmarcivaZip.Core.Settings;
using SmarcivaZip.Core.Shell;
using SmarcivaZip.Core.Localization;

namespace SmarcivaZip.App;

public partial class App : Application
{
    private AppSettings _settings = new();

    /// <summary>
    /// 7z.dll の COM オブジェクトを扱う専用スレッド。
    /// WPF の UI スレッドは STA なので、そこで作った COM オブジェクトを
    /// 別スレッドから使うと QueryInterface に失敗して落ちる。
    /// 生成から破棄まで全部ここに任せる。
    /// </summary>
    private readonly ComWorker _archiveWorker = new();

    /// <summary>
    /// レジストリに書き込む自分自身のパス。
    /// 単一ファイル発行だと Assembly.Location は空になるので使えない。
    /// </summary>
    public static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SmarcivaZip.exe");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = AppSettings.Load();

        // ウィンドウが作られる前に表示言語を決める。
        // XAML のリソース参照は生成時に解決されるため、後から変えても反映されない。
        AppLanguage.Apply(_settings.Language);
        ApplyTextDirection();
        _settings.ApplyToDetector();

        // ストア版では、右クリックメニューの中身を今の設定と表示言語で書き直しておく。
        // メニューの部品はこのファイルを読んで項目を並べるだけなので、言語を切り替えた後も
        // アプリを一度起動すれば追従する。
        RefreshModernMenu(_settings);

        CommandLine command = CommandLine.Parse(e.Args);
        Diagnostics.Trace($"start mode={command.Mode} paths={string.Join(" | ", command.Paths)}");

        // 失敗したことを呼び出し元（インストーラなど）が知る手段は終了コードしかない。
        int exitCode = 0;

        try
        {
            await RunAsync(command);
        }
        catch (SevenZipNotFoundException ex)
        {
            exitCode = 1;
            Diagnostics.Error("startup", ex);
            if (!command.Quiet) ShowError(ex.Message, "App_EngineMissingTitle");
        }
        catch (Exception ex)
        {
            exitCode = 1;
            Diagnostics.Error("startup", ex);
            if (!command.Quiet)
            {
                ShowError(Strings.Format("App_ErrorWithLog", ex.Message, Diagnostics.LogPath),
                    "App_ErrorTitle");
            }
        }
        finally
        {
            _archiveWorker.Dispose();
            Shutdown(exitCode);
        }
    }

    /// <summary>
    /// アラビア語のように右から左へ書く言語では、画面全体を反転させる。
    /// ウィンドウごとに指定して回るのではなく、既定値を差し替えて一度で済ませる。
    /// </summary>
    private static void ApplyTextDirection()
    {
        if (!AppLanguage.IsRightToLeft(System.Globalization.CultureInfo.CurrentUICulture)) return;

        FrameworkElement.FlowDirectionProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(FlowDirection.RightToLeft));
    }

    private async Task RunAsync(CommandLine command)
    {
        switch (command.Mode)
        {
            case AppMode.Help:
                MessageBox.Show(CommandLine.HelpText, Strings.Get("Common_AppName"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                break;

            case AppMode.Register:
                // ストア版の関連付けとメニューはパッケージの定義で Windows が管理する。
                // レジストリに書いてもパッケージの中では見えないので、書かない。
                if (!PackageContext.IsPackaged) RegisterShell(command.Quiet);
                break;

            case AppMode.Unregister:
                ShellRegistration.Unregister();
                if (!command.Quiet)
                {
                    MessageBox.Show(Strings.Get("Setup_UnregisteredMessage"), Strings.Get("Common_AppName"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                break;

            case AppMode.Auto:
                if (DropAction.Decide(command.Paths, _settings.AssociatedExtensions) == DropActionKind.Extract)
                {
                    await ExtractAsync(command);
                }
                else
                {
                    await CompressAsync(command);
                }
                break;

            case AppMode.Extract:
            case AppMode.ExtractWithPreview:
                await ExtractAsync(command);
                break;

            case AppMode.Compress:
                await CompressAsync(command);
                break;

            default:
                ShowSettings();
                break;
        }
    }

    // ---------------------------------------------------------------- 解凍

    private async Task ExtractAsync(CommandLine command)
    {
        if (command.Paths.Count == 0)
        {
            ShowSettings();
            return;
        }

        IReadOnlyList<string> archives = await CoalesceAsync("extract", command);
        string? lastDestination = null;

        foreach (string archive in archives)
        {
            ExtractOutcome outcome = ExtractOne(archive, command);

            if (outcome.Cancelled) return;
            if (outcome.Destination is not null) lastDestination = outcome.Destination;
        }

        if (lastDestination is not null && _settings.OpenFolderAfterExtract)
        {
            NativeShell.RevealInExplorer(lastDestination);
        }
    }

    private sealed record ExtractOutcome(string? Destination, bool Cancelled);

    private ExtractOutcome ExtractOne(string archivePath, CommandLine command)
    {
        ArchiveOpenOptions openOptions = _settings.ToOpenOptions();
        ArchiveReader? reader = TryOpen(archivePath, openOptions);
        if (reader is null) return new ExtractOutcome(null, Cancelled: false);

        try
        {
            // 文字コードの確認画面を出すか決める。
            // 自動判定に自信があるなら出さない（ダブルクリックで即解凍の体感を守る）。
            bool wantsPreview = command.Mode == AppMode.ExtractWithPreview
                                || _settings.AlwaysShowEncodingPreview
                                || (reader.DetectedCodePage is not null
                                    && reader.CodePageConfidence < 0.80);

            bool excludeMacMetadata = _settings.ExcludeMacMetadata;

            if (wantsPreview && reader.DetectedCodePage is not null)
            {
                var preview = new EncodingPreviewWindow(
                    reader, _settings.NormalizeMacNames, excludeMacMetadata);

                if (preview.ShowDialog() != true) return new ExtractOutcome(null, Cancelled: true);

                excludeMacMetadata = preview.ExcludeMacMetadata;

                if (preview.SelectedCodePage != reader.DetectedCodePage
                    || preview.NormalizeToNfc != openOptions.NormalizeToNfc)
                {
                    ArchiveReader current = reader;

                    reader = _archiveWorker.Invoke(() =>
                    {
                        ArchiveReader next = current.Reopen(preview.SelectedCodePage, preview.NormalizeToNfc);
                        current.Dispose();
                        return next;
                    });
                }
            }

            // 暗号化されているなら先にパスワードを聞く。
            string? password = null;
            if (reader.Entries.Any(entry => entry.IsEncrypted))
            {
                PasswordWindow dialog = PasswordWindow.ForExtraction(Path.GetFileName(archivePath));
                if (dialog.ShowDialog() != true) return new ExtractOutcome(null, Cancelled: true);
                password = dialog.Password;
            }

            ExtractOptions options = _settings.ToExtractOptions();
            options.ExcludeMacMetadata = excludeMacMetadata;
            options.Password = password;
            if (command.ForceOutputFolder) options.FolderMode = OutputFolderMode.AlwaysCreate;

            return RunExtraction(reader, options, archivePath);
        }
        finally
        {
            ArchiveReader toDispose = reader;
            _archiveWorker.Invoke(toDispose.Dispose);
        }
    }

    private ExtractOutcome RunExtraction(ArchiveReader reader, ExtractOptions options, string archivePath)
    {
        var service = new ExtractService
        {
            ConfirmSuspiciousArchive = ConfirmSuspiciousArchive
        };

        ExtractResult? result = null;

        var window = new ProgressWindow(
            Strings.Format("Progress_ExtractHeading", Path.GetFileName(archivePath)));

        window.Run((progress, cancellationToken) =>
        {
            var relay = new Progress<ExtractProgress>(p =>
                progress.Report((p.CurrentFile, p.Fraction)));

            result = _archiveWorker.Invoke(() =>
                service.Extract(reader, options, relay, cancellationToken));

            return Task.CompletedTask;
        });

        window.ShowDialog();

        Diagnostics.Trace($"extract done cancelled={window.WasCancelled} error={window.Error?.Message} " +
                          $"result={(result is null ? "null" : $"ok={result.Succeeded} files={result.FilesExtracted}")}");

        if (window.Error is not null)
        {
            Diagnostics.Error("extract", window.Error);
            ShowError(window.Error.Message, "App_ExtractFailedTitle");
            return new ExtractOutcome(null, Cancelled: false);
        }

        if (window.WasCancelled || result is null || result.Cancelled)
            return new ExtractOutcome(null, Cancelled: true);

        ReportExtractWarnings(result, archivePath);

        if (options.DeleteArchiveAfterExtract && result.Succeeded)
        {
            // 展開中はアーカイブを開いたままなので、閉じてからでないと消せない。
            _archiveWorker.Invoke(reader.Dispose);
            RecycleBin.TryMoveToRecycleBin(archivePath);
        }

        return new ExtractOutcome(result.DestinationRoot, Cancelled: false);
    }

    private void ReportExtractWarnings(ExtractResult result, string archivePath)
    {
        var lines = new List<string>();

        if (result.WrongPassword) lines.Add(Strings.Get("App_WrongPassword"));

        if (result.RejectedEntries.Count > 0)
        {
            lines.Add(Strings.Format("App_BlockedEntries", result.RejectedEntries.Count));

            foreach (PlannedEntry rejected in result.RejectedEntries.Take(5))
                lines.Add(Strings.Format("App_PathBullet", rejected.Entry.Path));
        }

        foreach (ExtractError error in result.Errors.Take(5))
            lines.Add(Strings.Format("App_ErrorBullet", error.EntryPath, error.Message));

        if (result.Errors.Count > 5)
            lines.Add(Strings.Format("App_MoreErrors", result.Errors.Count - 5));

        if (lines.Count == 0) return;

        MessageBox.Show(
            $"{Path.GetFileName(archivePath)}\n\n{string.Join('\n', lines)}",
            Strings.Get("Common_AppName"), MessageBoxButton.OK,
            result.RejectedEntries.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private bool ConfirmSuspiciousArchive(BombAssessment assessment)
    {
        return Dispatcher.Invoke(() => MessageBox.Show(
            Strings.Format("App_BombWarning", assessment.Reason),
            Strings.Get("Common_AppName"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
    }

    private ArchiveReader? TryOpen(string archivePath, ArchiveOpenOptions options)
    {
        try
        {
            return _archiveWorker.Invoke(() => ArchiveReader.Open(archivePath, options));
        }
        catch (ArchiveOpenException)
        {
            // ヘッダごと暗号化された 7z は、パスワードが無いと開くことすらできない。
            PasswordWindow dialog = PasswordWindow.ForExtraction(Path.GetFileName(archivePath));
            if (dialog.ShowDialog() != true) return null;

            options.Password = dialog.Password;

            try
            {
                return _archiveWorker.Invoke(() => ArchiveReader.Open(archivePath, options));
            }
            catch (ArchiveOpenException ex)
            {
                ShowError(ex.Message, "App_OpenFailedTitle");
                return null;
            }
        }
    }

    // ---------------------------------------------------------------- 圧縮

    private async Task CompressAsync(CommandLine command)
    {
        if (command.Paths.Count == 0)
        {
            ShowSettings();
            return;
        }

        string formatId = command.FormatId ?? _settings.DefaultFormatId;
        OutputFormat? format = OutputFormat.All.FirstOrDefault(f =>
            string.Equals(f.Id, formatId, StringComparison.OrdinalIgnoreCase));

        if (format is null)
        {
            ShowError(Strings.Format("App_UnknownFormat", formatId), "App_CompressNotPossible");
            return;
        }

        string operationKey = $"compress:{format.Id}:{command.AskPassword}";
        IReadOnlyList<string> inputs = await CoalesceAsync(operationKey, command);
        if (inputs.Count == 0) return;

        string? password = null;
        if (command.AskPassword)
        {
            PasswordWindow dialog = PasswordWindow.ForCompression();
            if (dialog.ShowDialog() != true) return;
            password = dialog.Password;
        }

        var options = new CompressOptions
        {
            Format = format,
            Level = (CompressionLevel)_settings.CompressionLevel,
            Password = password,
            EncryptHeaders = password is not null && format.SupportsHeaderEncryption
        };

        if (_settings.ShowCompressDialog && !AskOutputPath(inputs, format, options)) return;

        RunCompression(inputs, options);
    }

    private bool AskOutputPath(IReadOnlyList<string> inputs, OutputFormat format, CompressOptions options)
    {
        string suggested = CompressItemCollector.SuggestArchiveName(inputs, format);
        string directory = Path.GetDirectoryName(Path.GetFullPath(inputs[0])) ?? string.Empty;

        var dialog = new SaveFileDialog
        {
            Title = Strings.Get("App_SaveDialogTitle"),
            FileName = suggested,
            InitialDirectory = directory,
            Filter = $"{format.DisplayName}|*{format.Extension}|{Strings.Get("App_AllFiles")}|*.*",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true) return false;

        options.OutputDirectory = Path.GetDirectoryName(dialog.FileName);
        options.OutputFileName = Path.GetFileName(dialog.FileName);
        options.AvoidOverwrite = false; // ダイアログで上書き確認済み
        return true;
    }

    private void RunCompression(IReadOnlyList<string> inputs, CompressOptions options)
    {
        var service = new CompressService();
        CompressResult? result = null;

        string heading = inputs.Count == 1
            ? Strings.Format("Progress_CompressOne",
                Path.GetFileName(inputs[0].TrimEnd(Path.DirectorySeparatorChar)))
            : Strings.Format("Progress_CompressMany", inputs.Count);

        var window = new ProgressWindow(heading);

        window.Run((progress, cancellationToken) =>
        {
            var relay = new Progress<CompressProgress>(p =>
                progress.Report((p.CurrentFile, p.Fraction)));

            result = _archiveWorker.Invoke(() =>
                service.Compress(inputs, options, relay, cancellationToken));

            return Task.CompletedTask;
        });

        window.ShowDialog();

        Diagnostics.Trace($"compress done cancelled={window.WasCancelled} error={window.Error?.Message}");

        if (window.Error is not null)
        {
            Diagnostics.Error("compress", window.Error);
            ShowError(window.Error.Message, "App_CompressFailedTitle");
            return;
        }

        if (window.WasCancelled || result is null) return;

        if (result.FailedItems.Count > 0)
        {
            MessageBox.Show(
                Strings.Format("App_UnreadableItems", result.FailedItems.Count,
                    string.Join('\n', result.FailedItems.Take(5)
                        .Select(f => Strings.Format("App_PathBullet", f)))),
                Strings.Get("Common_AppName"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        NativeShell.RevealInExplorer(result.ArchivePath);
    }

    // ---------------------------------------------------------------- 共通

    /// <summary>
    /// エクスプローラーの複数選択は「1 ファイルにつき 1 プロセス」で飛んでくる。
    /// 先頭のプロセスだけが処理を続け、残りはここで静かに終了する。
    /// </summary>
    private async Task<IReadOnlyList<string>> CoalesceAsync(string operationKey, CommandLine command)
    {
        List<string> paths = command.Paths;

        // ストア版のメニューは選択を全部まとめて渡してくる（--paths-from）。待つ相手がいない。
        if (command.PathsComplete || paths.Count != 1) return paths;

        CoalescedSelection selection = await SelectionCoalescer.CollectAsync(operationKey, paths[0]);
        Diagnostics.Trace($"coalesce leader={selection.IsLeader} count={selection.Paths.Count}");

        // 先頭でなければ、別のプロセスがまとめて処理してくれる。
        return selection.IsLeader ? selection.Paths : [];
    }

    private void ShowSettings()
    {
        var window = new SettingsWindow(_settings);
        window.ShowDialog();
    }

    /// <summary>
    /// ストア版なら、右クリックメニューの中身（<see cref="ModernMenuFile"/>）を書き出す。
    /// ストア版でなければ何もしない。失敗してもアプリ本来の動作は止めない。
    /// </summary>
    internal static void RefreshModernMenu(AppSettings settings)
    {
        string? directory = PackageContext.LocalStatePath;
        if (directory is null) return;

        try
        {
            IReadOnlyList<OutputFormat> formats;
            try { formats = OutputFormat.GetAvailable(SevenZipLibrary.Instance); }
            catch (SevenZipNotFoundException) { formats = OutputFormat.All; }

            List<CompressMenuItem> items = CompressMenuItem.Resolve(formats, settings.ContextMenuFormats);
            ModernMenuFile.Write(directory, ModernMenuFile.Build(items), settings.AssociatedExtensions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Error("modern-menu", ex);
        }
    }

    private void RegisterShell(bool quiet)
    {
        IReadOnlyList<OutputFormat> formats;
        try { formats = OutputFormat.GetAvailable(SevenZipLibrary.Instance); }
        catch (SevenZipNotFoundException) { formats = OutputFormat.All; }

        List<CompressMenuItem> menuItems =
            CompressMenuItem.Resolve(formats, _settings.ContextMenuFormats);

        ShellRegistration.Register(ExecutablePath, _settings.AssociatedExtensions, menuItems,
            _settings.ExtensionChoices);

        if (quiet) return;

        MessageBox.Show(Strings.Get("Setup_RegisteredMessage"), Strings.Get("Common_AppName"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void ShowError(string message, string titleKey)
        => MessageBox.Show(message, $"smarcivaZIP - {Strings.Get(titleKey)}",
            MessageBoxButton.OK, MessageBoxImage.Error);
}
