using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
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

    /// <summary>
    /// 拡張子ひとつ分の行。チェックの有無をそのまま双方向バインドする。
    /// </summary>
    private sealed class ExtensionChoice(string extension, bool isSelected, string? description)
        : INotifyPropertyChanged
    {
        private bool _isSelected = isSelected;

        public string Extension { get; } = extension;

        public string? Description { get; } = description;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// 右クリックメニューに出す圧縮項目ひとつ分の行。
    /// </summary>
    private sealed class MenuFormatChoice(CompressMenuItem item, bool isSelected) : INotifyPropertyChanged
    {
        private bool _isSelected = isSelected;

        public CompressMenuItem Item { get; } = item;

        public string Label => Item.Label;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly AppSettings _settings;
    private readonly ObservableCollection<ExtensionChoice> _extensions = [];
    private readonly ObservableCollection<MenuFormatChoice> _menuFormats = [];

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        ExtensionList.ItemsSource = _extensions;
        ExtensionList.SelectionChanged += (_, _) => UpdateExtensionButtons();

        MenuFormatList.ItemsSource = _menuFormats;
        MenuFormatList.SelectionChanged += (_, _) => UpdateMenuFormatButtons();

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

        LoadMenuFormats();
        LoadExtensions();
    }

    // ---------------------------------------------------------------- 右クリックメニューの形式

    private void LoadMenuFormats()
    {
        List<CompressMenuItem> available = CompressMenuItem.BuildAll(AvailableFormats());

        var selected = _settings.ContextMenuFormats
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        _menuFormats.Clear();

        // 選ばれているものを設定どおりの順番で先に並べる。
        // その順番がそのままメニューの並び順になるので、上下の入れ替えが意味を持つ。
        foreach (string id in selected)
        {
            CompressMenuItem? item = available.FirstOrDefault(
                i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

            if (item is not null) _menuFormats.Add(new MenuFormatChoice(item, true));
        }

        foreach (CompressMenuItem item in available)
        {
            if (_menuFormats.Any(c => c.Item.Id == item.Id)) continue;
            _menuFormats.Add(new MenuFormatChoice(item, false));
        }

        UpdateMenuFormatButtons();
    }

    private void UpdateMenuFormatButtons()
    {
        int index = MenuFormatList.SelectedIndex;
        MenuFormatUpButton.IsEnabled = index > 0;
        MenuFormatDownButton.IsEnabled = index >= 0 && index < _menuFormats.Count - 1;
    }

    private void OnMenuFormatItemActivated(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBoxItem item) item.IsSelected = true;
    }

    private void MoveSelectedMenuFormat(int offset)
    {
        int index = MenuFormatList.SelectedIndex;
        int target = index + offset;
        if (index < 0 || target < 0 || target >= _menuFormats.Count) return;

        _menuFormats.Move(index, target);
        MenuFormatList.SelectedIndex = target;
        UpdateMenuFormatButtons();
    }

    private void OnMenuFormatMoveUpClicked(object sender, RoutedEventArgs e) => MoveSelectedMenuFormat(-1);

    private void OnMenuFormatMoveDownClicked(object sender, RoutedEventArgs e) => MoveSelectedMenuFormat(1);

    private void OnResetMenuFormatsClicked(object sender, RoutedEventArgs e)
    {
        _settings.ContextMenuFormats = [.. AppSettings.DefaultContextMenuFormats];
        LoadMenuFormats();
    }

    /// <summary>選択されている項目を、一覧に並んでいる順で返す。</summary>
    private List<CompressMenuItem> SelectedMenuItems()
        => _menuFormats.Where(c => c.IsSelected).Select(c => c.Item).ToList();

    private void OnHyperlinkRequestNavigate(object sender,
        System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 既定のブラウザーが無い環境。開けないだけで、設定画面は使い続けられる。
        }

        e.Handled = true;
    }

    // ---------------------------------------------------------------- 拡張子の一覧

    /// <summary>拡張子 -> それを扱えるハンドラ名。ツールチップに出す。</summary>
    private static Dictionary<string, string> BuildExtensionDescriptions()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (HandlerInfo handler in SevenZipLibrary.Instance.Handlers)
            {
                foreach (string extension in handler.Extensions)
                {
                    if (!map.ContainsKey(extension))
                        map[extension] = handler.Name.ToUpperInvariant() + " 形式";
                }
            }
        }
        catch (SevenZipNotFoundException)
        {
            // 7z.dll が無い環境では説明を出せないだけ。一覧自体は使える。
        }

        return map;
    }

    private void LoadExtensions()
    {
        Dictionary<string, string> descriptions = BuildExtensionDescriptions();

        var selected = _settings.AssociatedExtensions
            .Select(NormalizeExtension)
            .Where(e => e.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 一覧に無いのにチェックだけ入っている拡張子があっても取りこぼさない。
        IEnumerable<string> all = _settings.ExtensionChoices
            .Concat(_settings.AssociatedExtensions)
            .Select(NormalizeExtension)
            .Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        _extensions.Clear();

        foreach (string extension in all)
        {
            _extensions.Add(new ExtensionChoice(
                extension,
                selected.Contains(extension),
                descriptions.GetValueOrDefault(extension, "この 7z.dll では未対応")));
        }

        UpdateExtensionButtons();
    }

    private static string NormalizeExtension(string extension)
        => extension.Trim().TrimStart('.').ToLowerInvariant();

    private void UpdateExtensionButtons()
        => RemoveExtensionButton.IsEnabled = ExtensionList.SelectedItem is ExtensionChoice;

    /// <summary>
    /// 行の中のチェックボックスに触れたとき、その行自体も選択する。
    /// 「削除」がどの拡張子に効くのかを利用者に見せるため。
    /// </summary>
    private void OnExtensionItemActivated(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBoxItem item) item.IsSelected = true;
    }

    private void OnAddExtensionClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new TextInputWindow(
            "拡張子を追加",
            "ドットは付けても付けなくても構いません（例: alz）。")
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true) return;

        string extension = NormalizeExtension(dialog.Value);
        if (extension.Length == 0) return;

        ExtensionChoice? existing = _extensions.FirstOrDefault(
            c => string.Equals(c.Extension, extension, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.IsSelected = true;
            ExtensionList.SelectedItem = existing;
            return;
        }

        var added = new ExtensionChoice(extension, true,
            BuildExtensionDescriptions().GetValueOrDefault(extension, "この 7z.dll では未対応"));

        _extensions.Add(added);
        ExtensionList.SelectedItem = added;
        ExtensionList.ScrollIntoView(added);
    }

    private void OnRemoveExtensionClicked(object sender, RoutedEventArgs e)
    {
        if (ExtensionList.SelectedItem is not ExtensionChoice choice) return;

        _extensions.Remove(choice);
        UpdateExtensionButtons();
    }

    private void OnCheckAllClicked(object sender, RoutedEventArgs e)
    {
        foreach (ExtensionChoice choice in _extensions) choice.IsSelected = true;
    }

    private void OnUncheckAllClicked(object sender, RoutedEventArgs e)
    {
        foreach (ExtensionChoice choice in _extensions) choice.IsSelected = false;
    }

    /// <summary>
    /// 読み込んでいる 7z.dll が扱える拡張子をすべて一覧に足す。
    /// チェックは入れない（iso や vhd まで勝手に関連付けられたら迷惑なので）。
    /// </summary>
    private void OnAddAllSupportedClicked(object sender, RoutedEventArgs e)
    {
        Dictionary<string, string> descriptions = BuildExtensionDescriptions();

        var known = _extensions
            .Select(c => c.Extension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int added = 0;

        foreach ((string extension, string description) in
                 descriptions.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            if (!known.Add(extension)) continue;
            _extensions.Add(new ExtensionChoice(extension, false, description));
            added++;
        }

        if (added == 0)
        {
            MessageBox.Show(this, "追加できる拡張子はありませんでした。", "smarcivaZIP",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnResetExtensionsClicked(object sender, RoutedEventArgs e)
    {
        _settings.ExtensionChoices = [.. AppSettings.DefaultExtensionChoices];
        _settings.AssociatedExtensions = [.. AppSettings.DefaultAssociatedExtensions];
        LoadExtensions();
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
                App.ExecutablePath, _settings.AssociatedExtensions, SelectedMenuItems(),
                _settings.ExtensionChoices);

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
            ShellRegistration.Unregister(
                _settings.ExtensionChoices.Concat(_settings.AssociatedExtensions).Distinct().ToList());
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

        _settings.ContextMenuFormats = SelectedMenuItems().Select(i => i.Id).ToList();

        _settings.ExtensionChoices = _extensions.Select(c => c.Extension).ToList();
        _settings.AssociatedExtensions = _extensions
            .Where(c => c.IsSelected)
            .Select(c => c.Extension)
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
