using SmarcivaZip.Core.Extraction;
using SmarcivaZip.Core.SevenZip;
using Xunit;

namespace SmarcivaZip.Tests;

/// <summary>
/// RAR5 の展開。Lhaplus が対応していない形式で、本プロジェクトの目玉のひとつ。
///
/// 書庫は WinRAR で作ったものをコミットしてある。RAR 形式での圧縮は
/// unRAR のライセンス上行わないので、テスト側で生成できないためである。
/// 中身は日本語と韓国語のファイル名を含み、暗号化版はヘッダごと暗号化してある。
/// </summary>
public sealed class RarTests : IDisposable
{
    private const string Password = "pass123";

    private readonly string _workDir;

    public RarTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "smarcivazip-rar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); }
        catch (IOException) { }
    }

    private static string Fixture(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void RAR5をRar5ハンドラとして開く()
    {
        using ArchiveReader reader = ArchiveReader.Open(Fixture("rar5-plain.rar"));

        Assert.Equal("Rar5", reader.Handler.Name);

        // RAR での圧縮は unRAR ライセンスにより行わない。
        Assert.False(reader.Handler.CanUpdate);
    }

    [Fact]
    public void RAR5の日本語と韓国語のファイル名が壊れない()
    {
        using ArchiveReader reader = ArchiveReader.Open(Fixture("rar5-plain.rar"));

        var paths = reader.Entries.Select(e => e.Path.Replace('\\', '/')).ToList();

        Assert.Contains("src/日本語の名前.txt", paths);
        Assert.Contains("src/한국어.txt", paths);
        Assert.Contains("src/サブフォルダ/data.bin", paths);
    }

    [Fact]
    public void RAR5を展開すると中身が取り出せる()
    {
        using ArchiveReader reader = ArchiveReader.Open(Fixture("rar5-plain.rar"));

        ExtractResult result = new ExtractService().Extract(reader, new ExtractOptions
        {
            OutputDirectory = _workDir,
            OpenFolderAfterExtract = false
        });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Message)));

        string extracted = Path.Combine(result.DestinationRoot, "src", "日本語の名前.txt");
        Assert.True(File.Exists(extracted));
        Assert.Contains("RAR5", File.ReadAllText(extracted));
    }

    [Fact]
    public void ヘッダ暗号化RAR5はパスワード無しでは開けない()
    {
        // ヘッダごと暗号化されていると、ファイル名の一覧すら読めない。
        Assert.Throws<ArchiveOpenException>(
            () => ArchiveReader.Open(Fixture("rar5-encrypted.rar")));
    }

    [Fact]
    public void ヘッダ暗号化RAR5をパスワードで展開できる()
    {
        using ArchiveReader reader = ArchiveReader.Open(Fixture("rar5-encrypted.rar"),
            new ArchiveOpenOptions { Password = Password });

        Assert.Contains(reader.Entries, e => e.Path.EndsWith("日本語の名前.txt", StringComparison.Ordinal));
        Assert.Contains(reader.Entries, e => !e.IsDirectory && e.IsEncrypted);

        ExtractResult result = new ExtractService().Extract(reader, new ExtractOptions
        {
            OutputDirectory = _workDir,
            Password = Password,
            OpenFolderAfterExtract = false
        });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.Equal(3, result.FilesExtracted);
    }

    [Fact]
    public void 間違ったパスワードでは展開できない()
    {
        using ArchiveReader reader = ArchiveReader.Open(Fixture("rar5-encrypted.rar"),
            new ArchiveOpenOptions { Password = Password });

        ExtractResult result = new ExtractService().Extract(reader, new ExtractOptions
        {
            OutputDirectory = _workDir,
            Password = "wrong-password",
            OpenFolderAfterExtract = false
        });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void RARのハンドラが読み取り専用で公開されている()
    {
        // RAR で圧縮できてしまうと unRAR のライセンスに反する。
        var rarHandlers = SevenZipLibrary.Instance.Handlers
            .Where(h => h.Name.StartsWith("Rar", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(rarHandlers);
        Assert.All(rarHandlers, handler => Assert.False(handler.CanUpdate));
    }
}
