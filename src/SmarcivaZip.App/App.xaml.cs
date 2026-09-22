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
        _settings.ApplyToDetector();

        CommandLine command = CommandLine.Parse(e.Args);
        Diagnostics.Trace($"start mode={command.Mode} paths={string.Join(" | ", command.Paths)}");

        try
        {
            await RunAsync(command);
        }
        catch (SevenZipNotFoundException ex)
        {
            Diagnostics.Error("startup", ex);
            ShowError(ex.Message, "圧縮エンジンが見つかりません");
        }
        catch (Exception ex)
        {
            Diagnostics.Error("startup", ex);
            ShowError($"{ex.Message}\n\n詳細は次のファイルに記録しました。\n{Diagnostics.LogPath}", "エラー");
        }
        finally
        {
            _archiveWorker.Dispose();
            Shutdown();
        }
    }

    private async Task RunAsync(CommandLine command)
    {
        switch (command.Mode)
        {
            case AppMode.Help:
                MessageBox.Show(CommandLine.HelpText, "smarcivaZIP",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                break;

            case AppMode.Register:
                RegisterShell();
                break;

            case AppMode.Unregister:
                ShellRegistration.Unregister(_settings.AssociatedExtensions);
                MessageBox.Show("登録を解除しました。", "smarcivaZIP",
                    MessageBoxButton.OK, MessageBoxImage.Information);
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

        IReadOnlyList<string> archives = await CoalesceAsync("extract", command.Paths);
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

        var window = new ProgressWindow($"{Path.GetFileName(archivePath)} を解凍しています");

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
            ShowError(window.Error.Message, "解凍に失敗しました");
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

        if (result.WrongPassword) lines.Add("パスワードが違うため、一部を取り出せませんでした。");

        if (result.RejectedEntries.Count > 0)
        {
            lines.Add(
                $"展開先の外に書き込もうとするエントリを {result.RejectedEntries.Count} 件ブロックしました。" +
                "このアーカイブは細工されている可能性があります。");

            foreach (PlannedEntry rejected in result.RejectedEntries.Take(5))
                lines.Add($"　・{rejected.Entry.Path}");
        }

        foreach (ExtractError error in result.Errors.Take(5))
            lines.Add($"　・{error.EntryPath}: {error.Message}");

        if (result.Errors.Count > 5)
            lines.Add($"　ほか {result.Errors.Count - 5} 件のエラー");

        if (lines.Count == 0) return;

        MessageBox.Show(
            $"{Path.GetFileName(archivePath)}\n\n{string.Join('\n', lines)}",
            "smarcivaZIP", MessageBoxButton.OK,
            result.RejectedEntries.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private bool ConfirmSuspiciousArchive(BombAssessment assessment)
    {
        return Dispatcher.Invoke(() => MessageBox.Show(
            $"このアーカイブは展開すると異常に大きくなります。\n\n{assessment.Reason}\n\n続けますか？",
            "smarcivaZIP", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
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
                ShowError(ex.Message, "開けませんでした");
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
            ShowError($"知らない形式です: {formatId}", "圧縮できません");
            return;
        }

        string operationKey = $"compress:{format.Id}:{command.AskPassword}";
        IReadOnlyList<string> inputs = await CoalesceAsync(operationKey, command.Paths);
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
            Title = "保存先",
            FileName = suggested,
            InitialDirectory = directory,
            Filter = $"{format.DisplayName}|*{format.Extension}|すべてのファイル|*.*",
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
            ? $"{Path.GetFileName(inputs[0].TrimEnd(Path.DirectorySeparatorChar))} を圧縮しています"
            : $"{inputs.Count} 個の項目を圧縮しています";

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
            ShowError(window.Error.Message, "圧縮に失敗しました");
            return;
        }

        if (window.WasCancelled || result is null) return;

        if (result.FailedItems.Count > 0)
        {
            MessageBox.Show(
                $"{result.FailedItems.Count} 個のファイルを読み取れなかったため、アーカイブに含めていません。\n\n" +
                string.Join('\n', result.FailedItems.Take(5).Select(f => "　・" + f)),
                "smarcivaZIP", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        NativeShell.RevealInExplorer(result.ArchivePath);
    }

    // ---------------------------------------------------------------- 共通

    /// <summary>
    /// エクスプローラーの複数選択は「1 ファイルにつき 1 プロセス」で飛んでくる。
    /// 先頭のプロセスだけが処理を続け、残りはここで静かに終了する。
    /// </summary>
    private async Task<IReadOnlyList<string>> CoalesceAsync(string operationKey, List<string> paths)
    {
        if (paths.Count != 1) return paths;

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

    private void RegisterShell()
    {
        IReadOnlyList<OutputFormat> formats;
        try { formats = OutputFormat.GetAvailable(SevenZipLibrary.Instance); }
        catch (SevenZipNotFoundException) { formats = OutputFormat.All; }

        List<CompressMenuItem> menuItems =
            CompressMenuItem.Resolve(formats, _settings.ContextMenuFormats);

        ShellRegistration.Register(ExecutablePath, _settings.AssociatedExtensions, menuItems,
            _settings.ExtensionChoices);

        MessageBox.Show("関連付けと右クリックメニューを登録しました。", "smarcivaZIP",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void ShowError(string message, string title)
        => MessageBox.Show(message, $"smarcivaZIP - {title}",
            MessageBoxButton.OK, MessageBoxImage.Error);
}
