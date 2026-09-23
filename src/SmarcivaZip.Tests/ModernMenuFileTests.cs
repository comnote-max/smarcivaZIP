using SmarcivaZip.Core.Compression;
using SmarcivaZip.Core.Shell;
using Xunit;

namespace SmarcivaZip.Tests;

/// <summary>
/// ストア版の右クリックメニューの中身を書いたファイル。
///
/// 読むのは C++ の部品（src/SmarcivaZip.ShellExt/ShellExt.cpp の LoadMenu）なので、
/// ここの書式を変えると、メニューが空になったり項目がずれたりする。書式を固定しておく。
/// </summary>
public class ModernMenuFileTests
{
    private static List<CompressMenuItem> ZipAnd7z()
    {
        OutputFormat zip = OutputFormat.All.First(f => f.Id == "zip");
        OutputFormat sevenZip = OutputFormat.All.First(f => f.Id == "7z");
        return
        [
            new CompressMenuItem(zip, WithPassword: false),
            new CompressMenuItem(sevenZip, WithPassword: true)
        ];
    }

    [Fact]
    public void 先頭の2行は見出しと拡張子()
    {
        string text = ModernMenuFile.Serialize(ModernMenuFile.Build(ZipAnd7z()), ["zip", ".7Z", "rar"]);
        string[] lines = text.Split('\n');

        Assert.Equal(ModernMenuFile.Header, lines[0]);
        // 部品側は小文字・ドット無しで比べるので、書き出す側でそろえる。
        Assert.Equal("extensions\tzip;7z;rar", lines[1]);
    }

    [Fact]
    public void 圧縮_区切り_解凍3つ_区切り_設定の順に並ぶ()
    {
        IReadOnlyList<ModernMenuEntry> entries = ModernMenuFile.Build(ZipAnd7z());

        Assert.Equal(
            ["compress", "compress", "separator", "extract", "extract", "extract", "separator", "settings"],
            entries.Select(e => e.Kind));
    }

    [Fact]
    public void 各行は種類_表示名_引数のタブ区切り()
    {
        string text = ModernMenuFile.Serialize(ModernMenuFile.Build(ZipAnd7z()), ["zip"]);
        string[] lines = text.TrimEnd('\n').Split('\n');

        Assert.Equal(3, lines[2].Split('\t').Length);
        Assert.StartsWith("compress\t", lines[2]);
        Assert.EndsWith("\t--compress zip", lines[2]);
        Assert.EndsWith("\t--compress 7z --password", lines[3]);
        Assert.Equal("separator", lines[4]);
        Assert.EndsWith("\t--settings", lines[^1]);
    }

    [Fact]
    public void 表示名に混ざったタブや改行は区切りを壊さない()
    {
        var entries = new List<ModernMenuEntry>
        {
            new(ModernMenuFile.KindCompress, "A\tB\nC", "--compress zip")
        };

        string line = ModernMenuFile.Serialize(entries, []).Split('\n')[2];

        Assert.Equal("compress\tA B C\t--compress zip", line);
    }

    [Fact]
    public void 圧縮の項目が無ければ先頭に区切りを置かない()
    {
        IReadOnlyList<ModernMenuEntry> entries = ModernMenuFile.Build([]);

        Assert.Equal("extract", entries[0].Kind);
    }

    [Fact]
    public void 書き出しは既存のファイルを置き換える()
    {
        string dir = Path.Combine(Path.GetTempPath(), "smarcivazip-menu-" + Guid.NewGuid().ToString("N"));
        try
        {
            ModernMenuFile.Write(dir, ModernMenuFile.Build(ZipAnd7z()), ["zip"]);
            ModernMenuFile.Write(dir, ModernMenuFile.Build([]), ["7z"]);

            string text = File.ReadAllText(Path.Combine(dir, ModernMenuFile.FileName));
            Assert.Contains("extensions\t7z", text);
            Assert.DoesNotContain("compress\t", text);
            Assert.False(File.Exists(Path.Combine(dir, ModernMenuFile.FileName + ".tmp")));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void テスト実行中はパッケージの外として扱われる()
    {
        Assert.False(PackageContext.IsPackaged);
        Assert.Null(PackageContext.LocalStatePath);
    }
}
