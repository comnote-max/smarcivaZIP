using SmarcivaZip.Core.Compression;
using SmarcivaZip.Core.Settings;
using Xunit;

namespace SmarcivaZip.Tests;

public class CompressMenuItemTests
{
    private static readonly OutputFormat[] SampleFormats =
        [OutputFormat.Zip, OutputFormat.SevenZip, OutputFormat.TarGz];

    [Fact]
    public void パスワード対応の形式だけ暗号化の項目を作る()
    {
        List<CompressMenuItem> items = CompressMenuItem.BuildAll(SampleFormats);

        Assert.Contains(items, i => i.Id == "zip");
        Assert.Contains(items, i => i.Id == "zip-password");
        Assert.Contains(items, i => i.Id == "7z-password");

        // TAR.GZ は暗号化に対応していないので、パスワード版を出してはいけない。
        Assert.DoesNotContain(items, i => i.Id == "tar.gz-password");
    }

    [Fact]
    public void メニューの並び順は設定の順番どおりになる()
    {
        List<CompressMenuItem> resolved = CompressMenuItem.Resolve(
            SampleFormats, ["7z", "tar.gz", "zip"]);

        Assert.Equal(["7z", "tar.gz", "zip"], resolved.Select(i => i.Id));
    }

    [Fact]
    public void 使えない形式の指定は黙って落とす()
    {
        // 7z.dll を差し替えて zstd が消えた、といった場合に落ちないこと。
        List<CompressMenuItem> resolved = CompressMenuItem.Resolve(
            SampleFormats, ["zip", "tar.zst", "存在しない形式"]);

        Assert.Equal(["zip"], resolved.Select(i => i.Id));
    }

    [Fact]
    public void 同じ項目を二重に並べない()
    {
        List<CompressMenuItem> resolved = CompressMenuItem.Resolve(SampleFormats, ["zip", "zip"]);

        Assert.Single(resolved);
    }

    [Fact]
    public void コマンドライン引数が圧縮モードとして解釈できる形になっている()
    {
        List<CompressMenuItem> items = CompressMenuItem.BuildAll([OutputFormat.SevenZip]);

        CompressMenuItem plain = items.Single(i => !i.WithPassword);
        CompressMenuItem encrypted = items.Single(i => i.WithPassword);

        Assert.Equal("--compress 7z", plain.Arguments);
        Assert.Equal("--compress 7z --password", encrypted.Arguments);
    }

    [Fact]
    public void 既定のメニュー項目はすべて実在する形式を指している()
    {
        List<CompressMenuItem> resolved = CompressMenuItem.Resolve(
            OutputFormat.All, AppSettings.DefaultContextMenuFormats);

        Assert.Equal(AppSettings.DefaultContextMenuFormats.Length, resolved.Count);
    }
}
