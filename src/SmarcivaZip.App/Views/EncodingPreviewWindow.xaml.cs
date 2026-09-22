using System.IO;
using System.Windows;
using System.Windows.Controls;
using SmarcivaZip.Core.Encodings;
using SmarcivaZip.Core.Extraction;
using SmarcivaZip.Core.Localization;

namespace SmarcivaZip.App.Views;

/// <summary>
/// 文字コードの確認画面。
///
/// Lhaplus は化けたら化けたまま展開するしかなかった。ここでは展開する前に
/// 実際のファイル名を見せ、違っていればその場で切り替えられるようにする。
/// 自動判定に自信があるときはこの画面自体を出さないので、
/// 普段の使い勝手は Lhaplus と同じまま（ダブルクリックで即解凍）に保てる。
/// </summary>
public partial class EncodingPreviewWindow : Window
{
    private sealed record CodePageChoice(CodePageInfo Info, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ArchiveReader _reader;
    private bool _initialized;

    public int SelectedCodePage { get; private set; }
    public bool NormalizeToNfc { get; private set; }
    public bool ExcludeMacMetadata { get; private set; }
    public bool Confirmed { get; private set; }

    public EncodingPreviewWindow(ArchiveReader reader, bool normalizeToNfc, bool excludeMacMetadata)
    {
        InitializeComponent();

        _reader = reader;
        SelectedCodePage = reader.DetectedCodePage ?? CodePageInfo.Utf8;
        NormalizeToNfc = normalizeToNfc;
        ExcludeMacMetadata = excludeMacMetadata;

        ArchiveNameText.Text = Path.GetFileName(reader.ArchivePath);
        NormalizeCheck.IsChecked = normalizeToNfc;
        ExcludeMacCheck.IsChecked = excludeMacMetadata;

        BuildVerdict();
        BuildCodePageList();

        _initialized = true;
        RefreshPreview();
    }

    private void BuildVerdict()
    {
        string formatName = _reader.Handler.Name.ToUpperInvariant();
        double confidence = _reader.CodePageConfidence;

        string assessment = Strings.Get(confidence switch
        {
            >= 0.9 => "Preview_VerdictHigh",
            >= 0.7 => "Preview_VerdictMedium",
            _ => "Preview_VerdictLow"
        });

        string macNote = _reader.IsMacArchive ? Strings.Get("Preview_MacNote") : string.Empty;

        VerdictText.Text = Strings.Format("Preview_Summary",
            formatName, _reader.Entries.Count.ToString("N0"), assessment, macNote);
    }

    private void BuildCodePageList()
    {
        // 判定スコアの高い順に並べる。上から試せば当たりやすい、という並びにする。
        var ordered = new List<CodePageInfo>();

        foreach (EncodingGuess guess in _reader.CodePageCandidates)
        {
            if (guess.IsDecodable) ordered.Add(guess.CodePage);
        }

        foreach (CodePageInfo candidate in CodePageInfo.Candidates)
        {
            if (!ordered.Contains(candidate)) ordered.Add(candidate);
        }

        foreach (CodePageInfo info in ordered)
        {
            bool isDetected = info.CodePage == SelectedCodePage;
            string label = isDetected ? Strings.Format("Preview_Detected", info) : info.ToString();

            var choice = new CodePageChoice(info, label);
            CodePageCombo.Items.Add(choice);

            if (isDetected) CodePageCombo.SelectedItem = choice;
        }

        if (CodePageCombo.SelectedItem is null && CodePageCombo.Items.Count > 0)
            CodePageCombo.SelectedIndex = 0;
    }

    private void RefreshPreview()
    {
        if (CodePageCombo.SelectedItem is not CodePageChoice choice) return;

        SelectedCodePage = choice.Info.CodePage;
        NormalizeToNfc = NormalizeCheck.IsChecked == true;
        ExcludeMacMetadata = ExcludeMacCheck.IsChecked == true;

        NameList.Items.Clear();

        IReadOnlyList<string> names = _reader.PreviewNames(SelectedCodePage, NormalizeToNfc);

        foreach (string name in names)
        {
            if (ExcludeMacMetadata && NameNormalizer.IsMacMetadata(name)) continue;
            NameList.Items.Add(name);
        }

        if (_reader.Entries.Count > names.Count)
        {
            NameList.Items.Add(Strings.Format("Preview_More",
                (_reader.Entries.Count - names.Count).ToString("N0")));
        }
    }

    private void OnCodePageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initialized) RefreshPreview();
    }

    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_initialized) RefreshPreview();
    }

    private void OnExtractClicked(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
