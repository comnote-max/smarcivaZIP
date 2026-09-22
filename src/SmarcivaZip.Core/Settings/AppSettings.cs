using System.Text.Json;
using System.Text.Json.Serialization;
using SmarcivaZip.Core.Encodings;
using SmarcivaZip.Core.Extraction;

namespace SmarcivaZip.Core.Settings;

/// <summary>
/// アプリ全体の設定。%APPDATA%\smarcivaZIP\settings.json に保存する。
/// レジストリではなく JSON にしているのは、ポータブル運用（USB メモリに入れて
/// 持ち歩く）でもそのままフォルダごとコピーできるようにするため。
/// </summary>
public sealed class AppSettings
{
    // ---- 解凍 ----

    public OutputFolderMode FolderMode { get; set; } = OutputFolderMode.Auto;

    public OverwritePolicy OverwritePolicy { get; set; } = OverwritePolicy.Rename;

    /// <summary>空なら「アーカイブと同じ場所」。</summary>
    public string? FixedOutputDirectory { get; set; }

    public bool ExcludeMacMetadata { get; set; } = true;

    public bool ExcludeWindowsMetadata { get; set; } = true;

    public bool PreserveTimestamps { get; set; } = true;

    public bool PropagateMarkOfTheWeb { get; set; } = true;

    public bool OpenFolderAfterExtract { get; set; } = true;

    public bool DeleteArchiveAfterExtract { get; set; }

    public bool UnwrapNestedTar { get; set; } = true;

    // ---- 文字コード ----

    /// <summary>判定が割れたときに優先する言語圏。</summary>
    public int PreferredCodePage { get; set; } = CodePageInfo.ShiftJis;

    /// <summary>
    /// 文字コードの確認画面を常に出す。
    /// 既定は false で、判定に自信が無いときだけ出す（Lhaplus と同じ体感を保つため）。
    /// </summary>
    public bool AlwaysShowEncodingPreview { get; set; }

    public bool NormalizeMacNames { get; set; } = true;

    // ---- 圧縮 ----

    public string DefaultFormatId { get; set; } = "zip";

    public int CompressionLevel { get; set; } = 5;

    /// <summary>圧縮前に出力先と形式を確認するダイアログを出す。</summary>
    public bool ShowCompressDialog { get; set; }

    /// <summary>
    /// 右クリックメニューに並べる圧縮の項目（CompressMenuItem.Id の並び）。
    /// 順番がそのままメニューの並び順になる。
    /// 全形式を出すとメニューが長くなりすぎるので、よく使うものだけを既定にしてある。
    /// </summary>
    public List<string> ContextMenuFormats { get; set; } = [.. DefaultContextMenuFormats];

    public static readonly string[] DefaultContextMenuFormats =
    [
        "zip", "zip-password", "7z", "7z-password", "tar.gz"
    ];

    // ---- 関連付け ----

    /// <summary>ダブルクリックで smarcivaZIP が開く拡張子（＝チェックが入っているもの）。</summary>
    public List<string> AssociatedExtensions { get; set; } = [.. DefaultAssociatedExtensions];

    /// <summary>
    /// 設定画面の一覧に並べる拡張子。チェックの有無に関わらず表示される。
    /// 利用者が「追加」した拡張子もここに入るので、チェックを外しても一覧からは消えない。
    /// </summary>
    public List<string> ExtensionChoices { get; set; } = [.. DefaultExtensionChoices];

    /// <summary>既定でチェックを入れておく拡張子。まず間違いなく書庫である拡張子だけ。</summary>
    public static readonly string[] DefaultAssociatedExtensions =
    [
        "zip", "7z", "rar", "tar", "gz", "tgz", "bz2", "tbz", "xz", "txz",
        "zst", "tzst", "lzh", "lha", "cab", "arj", "z", "lzma"
    ];

    /// <summary>
    /// 一覧に並べる既定の拡張子。
    /// iso や msi のように「書庫として開けるが、ふつうは別のアプリで開きたい」ものは
    /// 表示はするがチェックを外してある。
    /// </summary>
    public static readonly string[] DefaultExtensionChoices =
    [
        "zip", "7z", "rar", "tar", "gz", "tgz", "bz2", "tbz", "xz", "txz",
        "zst", "tzst", "lzh", "lha", "cab", "arj", "z", "lzma",
        "iso", "dmg", "msi", "wim", "cpio", "rpm", "deb", "jar", "xar", "chm", "zipx"
    ];

    // ---- 永続化 ----

    [JsonIgnore]
    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "smarcivaZIP");

    [JsonIgnore]
    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();

            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 設定が壊れていても起動はできるべきなので、既定値で続行する。
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);

            // 書き込み中に電源が落ちても設定が飛ばないよう、一時ファイル経由で差し替える。
            string temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 保存できなくても動作は続けられる。
        }
    }

    public ExtractOptions ToExtractOptions() => new()
    {
        OutputDirectory = string.IsNullOrWhiteSpace(FixedOutputDirectory) ? null : FixedOutputDirectory,
        FolderMode = FolderMode,
        OverwritePolicy = OverwritePolicy,
        ExcludeMacMetadata = ExcludeMacMetadata,
        ExcludeWindowsMetadata = ExcludeWindowsMetadata,
        PreserveTimestamps = PreserveTimestamps,
        PropagateMarkOfTheWeb = PropagateMarkOfTheWeb,
        OpenFolderAfterExtract = OpenFolderAfterExtract,
        DeleteArchiveAfterExtract = DeleteArchiveAfterExtract,
        UnwrapNestedTar = UnwrapNestedTar
    };

    public ArchiveOpenOptions ToOpenOptions() => new()
    {
        NormalizeToNfc = NormalizeMacNames
    };

    /// <summary>判定器へユーザーの言語設定を反映する。</summary>
    public void ApplyToDetector() => EncodingDetector.PreferredCodePage = PreferredCodePage;
}
