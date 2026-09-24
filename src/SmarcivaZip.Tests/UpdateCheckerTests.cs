using SmarcivaZip.Core.Updates;
using Xunit;

namespace SmarcivaZip.Tests;

/// <summary>
/// 更新のお知らせ。通信はせず、GitHub の応答を読むところと、確認の間隔だけを見る。
/// </summary>
public class UpdateCheckerTests
{
    private static readonly Version Current = new(1, 2, 0);

    private static string Release(string tag, bool prerelease = false, string url = "https://github.com/comnote-max/smarcivaZIP/releases/tag/v9.9.9")
        => $$"""{"tag_name":"{{tag}}","html_url":"{{url}}","draft":false,"prerelease":{{(prerelease ? "true" : "false")}}}""";

    [Fact]
    public void 新しい版があれば知らせる()
    {
        UpdateInfo? update = UpdateChecker.ParseLatestRelease(Release("v1.3.0"), Current);

        Assert.NotNull(update);
        Assert.Equal(new Version(1, 3, 0), update.Version);
    }

    [Theory]
    [InlineData("v1.2.0")]
    [InlineData("1.2")]
    [InlineData("v1.1.9")]
    public void 同じか古い版なら知らせない(string tag)
    {
        Assert.Null(UpdateChecker.ParseLatestRelease(Release(tag), Current));
    }

    [Fact]
    public void プレリリースは知らせない()
    {
        Assert.Null(UpdateChecker.ParseLatestRelease(Release("v2.0.0", prerelease: true), Current));
    }

    [Fact]
    public void 知らない場所のページは開かず固定のページにする()
    {
        UpdateInfo? update = UpdateChecker.ParseLatestRelease(Release("v1.3.0", url: "https://example.com/evil"), Current);

        Assert.Equal(UpdateChecker.ReleasesPage, update?.PageUrl);
    }

    [Theory]
    [InlineData("""{"tag_name":"latest"}""")]
    [InlineData("not json")]
    public void 読めない応答は失敗として扱う(string json)
    {
        Assert.Throws<UpdateCheckException>(() => UpdateChecker.ParseLatestRelease(json, Current));
    }

    [Fact]
    public void 確認は週に1回()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(UpdateChecker.IsDue(null, now));
        Assert.False(UpdateChecker.IsDue(now.AddDays(-6), now));
        Assert.True(UpdateChecker.IsDue(now.AddDays(-7), now));
        Assert.True(UpdateChecker.IsDue(now.AddDays(1), now)); // 時計が戻っていたら確認する
    }
}
