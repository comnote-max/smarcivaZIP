using SmarcivaZip.Core.Safety;
using Xunit;

namespace SmarcivaZip.Tests;

public class PathSanitizerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "smarcivazip-root");

    [Theory]
    [InlineData("../../../Windows/System32/evil.dll")]
    [InlineData("..\\..\\evil.exe")]
    [InlineData("docs/../../escape.txt")]
    public void 親ディレクトリへの脱出を拒否する(string entryPath)
    {
        SanitizedPath result = PathSanitizer.Resolve(Root, entryPath);

        Assert.False(result.IsSafe);
        Assert.Equal(PathRejectionReason.PathTraversal, result.Reason);
    }

    [Theory]
    [InlineData("C:/Windows/System32/evil.dll")]
    [InlineData("/etc/passwd")]
    [InlineData("//server/share/evil.dll")]
    public void 絶対パスを拒否する(string entryPath)
    {
        SanitizedPath result = PathSanitizer.Resolve(Root, entryPath);

        Assert.False(result.IsSafe);
        Assert.Equal(PathRejectionReason.AbsolutePath, result.Reason);
    }

    [Fact]
    public void 通常のパスは展開先の下に解決される()
    {
        SanitizedPath result = PathSanitizer.Resolve(Root, "docs/画像/写真.jpg");

        Assert.True(result.IsSafe);
        Assert.True(PathSanitizer.IsUnderRoot(Root, result.FullPath!));
        Assert.EndsWith(Path.Combine("docs", "画像", "写真.jpg"), result.FullPath);
    }

    [Fact]
    public void 代替データストリームの注入を防ぐ()
    {
        // "report.txt:hidden.exe" のようなコロンは ADS の指定になるため潰す。
        SanitizedPath result = PathSanitizer.Resolve(Root, "report.txt:hidden.exe");

        Assert.True(result.IsSafe);
        Assert.DoesNotContain("txt:hidden", result.FullPath);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("NUL.txt")]
    [InlineData("com1")]
    [InlineData("LPT9.dat")]
    public void Windowsの予約デバイス名をリネームする(string name)
    {
        string sanitized = PathSanitizer.SanitizeSegment(name);

        Assert.EndsWith("_", sanitized);
    }

    [Fact]
    public void 末尾のドットと空白を取り除く()
    {
        Assert.Equal("report", PathSanitizer.SanitizeSegment("report. "));
    }

    [Fact]
    public void 長いパスには拡張プレフィックスを付ける()
    {
        string longPath = @"C:\" + new string('a', 300);

        Assert.StartsWith(@"\\?\", PathSanitizer.ToExtendedLengthPath(longPath));
    }

    [Fact]
    public void 短いパスはそのまま返す()
    {
        Assert.Equal(@"C:\temp\a.txt", PathSanitizer.ToExtendedLengthPath(@"C:\temp\a.txt"));
    }
}

public class ArchiveBombDetectorTests
{
    [Fact]
    public void 極端な圧縮率を疑わしいと判定する()
    {
        // 1 MB が 10 GB になる典型的な Zip Bomb
        BombAssessment result = ArchiveBombDetector.Assess(
            archiveSize: 1024 * 1024,
            totalUncompressedSize: 10UL * 1024 * 1024 * 1024,
            entryCount: 5);

        Assert.True(result.IsSuspicious);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void 通常のアーカイブは疑わしくない()
    {
        BombAssessment result = ArchiveBombDetector.Assess(
            archiveSize: 5 * 1024 * 1024,
            totalUncompressedSize: 12UL * 1024 * 1024,
            entryCount: 120);

        Assert.False(result.IsSuspicious);
    }

    [Fact]
    public void 小さなアーカイブでは圧縮率を理由に警告しない()
    {
        // 数百バイトのテキストは簡単に 1000 倍近くまで縮むので、
        // ここで警告すると誤検知だらけになる。
        BombAssessment result = ArchiveBombDetector.Assess(
            archiveSize: 200,
            totalUncompressedSize: 500_000,
            entryCount: 1);

        Assert.False(result.IsSuspicious);
    }
}
