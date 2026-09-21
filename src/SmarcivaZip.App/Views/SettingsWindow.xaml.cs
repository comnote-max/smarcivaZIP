using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;
using SmarcivaZip.Core.Compression;
using SmarcivaZip.Core.Encodings;
using SmarcivaZip.Core.Extraction;
using SmarcivaZip.Core.SevenZip;
using SmarcivaZip.Core.Settings;
using SmarcivaZip.Core.Shell;

namespace SmarcivaZip.App.Views;

public partial class SettingsWindow : Window
{
    private sealed record Choice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        PopulateChoices();
        LoadFromSettings();
        ShowEngineInformation();
        UpdateRegistrationState();
    }

    private void PopulateChoices()
    {
        foreach (CodePageInfo info in CodePageInfo.Candidates)
        {
            PreferredCodePageCombo.Items.Add(new Choice<int>(info.CodePage, info.ToString()));
        }

        foreach (OutputFormat format in AvailableFormats())
        {
            DefaultFormatCombo.Items.Add(new Choice<string>(format.Id, format.DisplayName));
        }

        (int Level, string Label)[] levels =
        [
            (0, "無圧縮（速い・まとめるだけ）"),
            (1, "最速"),
            (5, "標準（おすすめ）"),
            (7, "高圧縮"),
            (9, "最高圧縮（遅い）")
        ];

        foreach ((int level, string label) in levels)
        {
            CompressionLevelCombo.Items.Add(new Choice<int>(level, label));
        }
    }

    private static IReadOnlyList<OutputFormat> AvailableFormats()
    {
        try { return OutputFormat.GetAvailable(SevenZipLibrary.Instance); }
        catch (SevenZipNotFoundException) { return OutputFormat.All; }
    }

    private void LoadFromSettings()
    {
        bool hasFixedFolder = !string.IsNullOrWhiteSpace(_settings.FixedOutputDirectory);
        OutputSameFolder.IsChecked = !hasFixedFolder;
        OutputFixedFolder.IsChecked = hasFixedFolder;
        FixedFolderBox.Text = _settings.FixedOutputDirectory ?? string.Empty;

        FolderAuto.IsChecked = _settings.FolderMode == OutputFolderMode.Auto;
        FolderAlways.IsChecked = _settings.FolderMode == OutputFolderMode.AlwaysCreate;
        FolderNever.IsChecked = _settings.FolderMode == OutputFolderMode.Never;

        OverwriteRename.IsChecked = _settings.OverwritePolicy == OverwritePolicy.Rename;
        OverwriteReplace.IsChecked = _settings.OverwritePolicy == OverwritePolicy.Overwrite;
        OverwriteSkip.IsChecked = _settings.OverwritePolicy == OverwritePolicy.Skip;

        ExcludeMacCheck.IsChecked = _settings.ExcludeMacMetadata;
        ExcludeWindowsCheck.IsChecked = _settings.ExcludeWindowsMetadata;
        TimestampsCheck.IsChecked = _settings.PreserveTimestamps;
        MotwCheck.IsChecked = _settings.PropagateMarkOfTheWeb;
        OpenFolderCheck.IsChecked = _settings.OpenFolderAfterExtract;
        UnwrapTarCheck.IsChecked = _settings.UnwrapNestedTar;
        DeleteArchiveCheck.IsChecked = _settings.DeleteArchiveAfterExtract;

        SelectByValue(PreferredCodePageCombo, _settings.PreferredCodePage);
        AlwaysPreviewCheck.IsChecked = _settings.AlwaysShowEncodingPreview;
        NormalizeMacCheck.IsChecked = _settings.NormalizeMacNames;

        SelectByValue(DefaultFormatCombo, _settings.DefaultFormatId);
        SelectByValue(CompressionLevelCombo, _settings.CompressionLevel);
        ShowCompressDialogCheck.IsChecked = _settings.ShowCompressDialog;

        ExtensionsBox.Text = string.Join(' ', _settings.AssociatedExtensions);
    }

    private static void SelectByValue<T>(System.Windows.Controls.ComboBox combo, T value)
    {
        foreach (object item in combo.Items)
        {
            if (item is Choice<T> choice && EqualityComparer<T>.Default.Equals(choice.Value, value))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static T? SelectedValue<T>(System.Windows.Controls.ComboBox combo, T? fallback)
        => combo.SelectedItem is Choice<T> choice ? choice.Value : fallback;

    private void ShowEngineInformation()
    {
        VersionText.Text = "バージョン " +
            (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

        try
        {
            SevenZipLibrary library = SevenZipLibrary.Instance;
            EngineText.Text = $"7-Zip の 7z.dll を使用しています。\n{library.LibraryPath}";

            var readable = library.Handlers
                .SelectMany(h => h.Extensions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var writable = library.Handlers
                .Where(h => h.CanUpdate)
                .Select(h => h.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            FormatsText.Text =
                $"解凍 ({readable.Count} 種類): {string.Join(", ", readable)}\n\n" +
                $"圧縮: {string.Join(", ", writable)}";
        }
        catch (SevenZipNotFoundException ex)
        {
            EngineText.Text = ex.Message;
            EngineText.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
            FormatsText.Text = "7z.dll が見つからないため、対応形式を表示できません。";
        }
    }

    private void UpdateRegistrationState()
    {
        bool registered = ShellRegistration.IsRegistered();

        RegistrationStateText.Text = registered
            ? "登録済みです。エクスプローラーの右クリックメニューから使えます。"
            : "まだ登録されていません。登録すると、右クリックメニューに smarcivaZIP が追加されます。";

        RegisterButton.Content = registered ? "登録し直す" : "登録する";
        UnregisterButton.IsEnabled = registered;
    }

    private void OnBrowseOutputFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "解凍先のフォルダを選択",
            InitialDirectory = Directory.Exists(FixedFolderBox.Text) ? FixedFolderBox.Text : null
        };

        if (dialog.ShowDialog(this) == true)
        {
            FixedFolderBox.Text = dialog.FolderName;
            OutputFixedFolder.IsChecked = true;
        }
    }

    private void OnRegisterClicked(object sender, RoutedEventArgs e)
    {
        ApplyToSettings();
        _settings.Save();

        try
        {
            ShellRegistration.Register(
                App.ExecutablePath, _settings.AssociatedExtensions, AvailableFormats());

            UpdateRegistrationState();
            MessageBox.Show(this,
                "登録しました。\n\nWindows 11 では、右クリックメニューの「その他のオプションを確認」の中に smarcivaZIP が入ります。",
                "smarcivaZIP", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(this, $"登録できませんでした。\n\n{ex.Message}",
                "smarcivaZIP", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnUnregisterClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            ShellRegistration.Unregister(_settings.AssociatedExtensions);
            UpdateRegistrationState();
            MessageBox.Show(this, "登録を解除しました。", "smarcivaZIP",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(this, $"解除できませんでした。\n\n{ex.Message}",
                "smarcivaZIP", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnOpenDefaultAppsClicked(object sender, RoutedEventArgs e)
        => NativeShell.OpenDefaultAppsSettings();

    private void ApplyToSettings()
    {
        _settings.FixedOutputDirectory = OutputFixedFolder.IsChecked == true
            ? FixedFolderBox.Text.Trim()
            : null;

        _settings.FolderMode =
            FolderAlways.IsChecked == true ? OutputFolderMode.AlwaysCreate :
            FolderNever.IsChecked == true ? OutputFolderMode.Never :
            OutputFolderMode.Auto;

        _settings.OverwritePolicy =
            OverwriteReplace.IsChecked == true ? OverwritePolicy.Overwrite :
            OverwriteSkip.IsChecked == true ? OverwritePolicy.Skip :
            OverwritePolicy.Rename;

        _settings.ExcludeMacMetadata = ExcludeMacCheck.IsChecked == true;
        _settings.ExcludeWindowsMetadata = ExcludeWindowsCheck.IsChecked == true;
        _settings.PreserveTimestamps = TimestampsCheck.IsChecked == true;
        _settings.PropagateMarkOfTheWeb = MotwCheck.IsChecked == true;
        _settings.OpenFolderAfterExtract = OpenFolderCheck.IsChecked == true;
        _settings.UnwrapNestedTar = UnwrapTarCheck.IsChecked == true;
        _settings.DeleteArchiveAfterExtract = DeleteArchiveCheck.IsChecked == true;

        _settings.PreferredCodePage = SelectedValue(PreferredCodePageCombo, CodePageInfo.ShiftJis);
        _settings.AlwaysShowEncodingPreview = AlwaysPreviewCheck.IsChecked == true;
        _settings.NormalizeMacNames = NormalizeMacCheck.IsChecked == true;

        _settings.DefaultFormatId = SelectedValue(DefaultFormatCombo, "zip") ?? "zip";
        _settings.CompressionLevel = SelectedValue(CompressionLevelCombo, 5);
        _settings.ShowCompressDialog = ShowCompressDialogCheck.IsChecked == true;

        _settings.AssociatedExtensions = ExtensionsBox.Text
            .Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().TrimStart('.').ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Distinct()
            .ToList();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        ApplyToSettings();
        _settings.Save();
        _settings.ApplyToDetector();
        DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;
}
