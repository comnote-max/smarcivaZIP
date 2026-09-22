using SmarcivaZip.Core.Settings;
using SmarcivaZip.Core.Shell;
using Xunit;

namespace SmarcivaZip.Tests;

/// <summary>
/// 書庫の種類分け。アイコンは ProgID 単位でしか設定できないので、
/// ここの分類がそのままエクスプローラー上の見た目になる。
/// </summary>
public class ArchiveFileTypeTests
{
    [Theory]
    [InlineData("zip", "Zip")]
    [InlineData("zipx", "Zip")]
    [InlineData("7z", "SevenZip")]
    [InlineData("rar", "Rar")]
    [InlineData("tar", "Tar")]
    [InlineData("tgz", "Tar")]
    [InlineData("tzst", "Tar")]
    [InlineData("gz", "Compressed")]
    [InlineData("xz", "Compressed")]
    [InlineData("zst", "Compressed")]
    [InlineData("lzh", "Lzh")]
    [InlineData("lha", "Lzh")]
    public void 拡張子が意図した分類に入る(string extension, string expectedId)
    {
        Assert.Equal(expectedId, ArchiveFileType.ForExtension(extension).Id);
    }

    [Theory]
    [InlineData(".ZIP")]
    [InlineData("ZIP")]
    [InlineData(" zip ")]
    public void 大文字やドット付きでも同じ分類になる(string extension)
    {
        Assert.Equal("Zip", ArchiveFileType.ForExtension(extension).Id);
    }

    [Theory]
    [InlineData("iso")]
    [InlineData("cab")]
    [InlineData("msi")]
    [InlineData("知らない拡張子")]
    public void 分類の無い拡張子は受け皿に入る(string extension)
    {
        Assert.Equal(ArchiveFileType.Fallback, ArchiveFileType.ForExtension(extension));
    }

    [Fact]
    public void ProgIDが分類ごとに別になっている()
    {
        var progIds = ArchiveFileType.All.Select(t => t.ProgId).ToList();

        Assert.Equal(progIds.Count, progIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(progIds, id => Assert.StartsWith(ShellRegistration.ProgIdPrefix, id));
    }

    [Fact]
    public void 分類ごとにアイコンが用意されている()
    {
        string icons = Path.Combine(AppContext.BaseDirectory, "Icons");

        var missing = ArchiveFileType.All
            .Where(type => !File.Exists(Path.Combine(icons, type.IconFileName)))
            .Select(type => type.IconFileName)
            .ToList();

        Assert.True(missing.Count == 0, "アイコンがありません: " + string.Join(", ", missing));
    }

    [Fact]
    public void 同じ拡張子が複数の分類に属していない()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicated = new List<string>();

        foreach (ArchiveFileType type in ArchiveFileType.All)
        {
            foreach (string extension in type.Extensions)
            {
                if (!seen.Add(extension)) duplicated.Add(extension);
            }
        }

        Assert.True(duplicated.Count == 0, "重複: " + string.Join(", ", duplicated));
    }

    [Fact]
    public void 使う分類だけが返る()
    {
        IReadOnlyList<ArchiveFileType> used = ArchiveFileType.UsedBy(["zip", "zipx", "7z"]);

        // zip と zipx は同じ分類なので、まとめてひとつになる。
        Assert.Equal(2, used.Count);
        Assert.Contains(used, t => t.Id == "Zip");
        Assert.Contains(used, t => t.Id == "SevenZip");
    }

    [Fact]
    public void 既定の拡張子はすべてどこかに宣言されている()
    {
        // 受け皿があるので実行時に困ることはないが、既定で関連付ける拡張子が
        // どこにも書かれていないのは分類の取りこぼしなので、気付けるようにする。
        // 受け皿へ入れるのが正解なら、受け皿の一覧に明示的に書くこと。
        var undeclared = AppSettings.DefaultAssociatedExtensions
            .Where(extension => !ArchiveFileType.All.Any(
                type => type.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(undeclared.Count == 0,
            "どの分類にも書かれていない拡張子: " + string.Join(", ", undeclared));
    }

    [Fact]
    public void 種類名が表示言語に追従する()
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            SmarcivaZip.Core.Localization.AppLanguage.Apply("ja");
            Assert.Equal("ZIP 書庫", ArchiveFileType.ForExtension("zip").DisplayName);

            SmarcivaZip.Core.Localization.AppLanguage.Apply("en");
            Assert.Equal("ZIP archive", ArchiveFileType.ForExtension("zip").DisplayName);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }
}
