using System.Text.Json;
using System.Text.Json.Serialization;
using SmarcivaZip.Core.Extraction;
using SmarcivaZip.Core.Settings;
using Xunit;

namespace SmarcivaZip.Tests;

public class AppSettingsTests
{
    // AppSettings.Save は %APPDATA% に書くので、テストでは呼ばずに
    // シリアライズだけを同じ設定で確認する。
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static AppSettings RoundTrip(AppSettings settings)
        => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, Options), Options)!;

    [Fact]
    public void 拡張子のチェック状態が保存して読み直しても残る()
    {
        var settings = new AppSettings
        {
            ExtensionChoices = ["zip", "7z", "iso", "alz"],
            AssociatedExtensions = ["zip", "7z", "alz"]
        };

        AppSettings restored = RoundTrip(settings);

        Assert.Equal(["zip", "7z", "iso", "alz"], restored.ExtensionChoices);
        Assert.Equal(["zip", "7z", "alz"], restored.AssociatedExtensions);

        // 一覧には出るがチェックは入っていない、という状態が保てていること。
        Assert.Contains("iso", restored.ExtensionChoices);
        Assert.DoesNotContain("iso", restored.AssociatedExtensions);
    }

    [Fact]
    public void 列挙型の設定が名前で保存される()
    {
        var settings = new AppSettings
        {
            FolderMode = OutputFolderMode.AlwaysCreate,
            OverwritePolicy = OverwritePolicy.Skip
        };

        string json = JsonSerializer.Serialize(settings, Options);

        // 数値で保存すると、列挙子の並びを変えた瞬間に既存の設定が壊れる。
        Assert.Contains("AlwaysCreate", json);
        Assert.Contains("Skip", json);

        AppSettings restored = RoundTrip(settings);
        Assert.Equal(OutputFolderMode.AlwaysCreate, restored.FolderMode);
        Assert.Equal(OverwritePolicy.Skip, restored.OverwritePolicy);
    }

    [Fact]
    public void 既定でチェックが入る拡張子はすべて一覧にも載っている()
    {
        // 一覧に無いのにチェックだけ入っている、という不整合を防ぐ。
        Assert.All(AppSettings.DefaultAssociatedExtensions,
            extension => Assert.Contains(extension, AppSettings.DefaultExtensionChoices));
    }

    [Fact]
    public void 既定ではisoやmsiにチェックを入れない()
    {
        // 書庫として開けはするが、ふつうは別のアプリで開きたいもの。
        foreach (string extension in new[] { "iso", "msi", "jar", "chm" })
        {
            Assert.Contains(extension, AppSettings.DefaultExtensionChoices);
            Assert.DoesNotContain(extension, AppSettings.DefaultAssociatedExtensions);
        }
    }

    [Fact]
    public void 設定から展開オプションを作れる()
    {
        var settings = new AppSettings
        {
            FixedOutputDirectory = "  ",
            DeleteArchiveAfterExtract = true
        };

        ExtractOptions options = settings.ToExtractOptions();

        // 空白だけの指定は「アーカイブと同じ場所」として扱う。
        Assert.Null(options.OutputDirectory);
        Assert.True(options.DeleteArchiveAfterExtract);
    }
}
