using System.Text;
using SmarcivaZip.Core.Encodings;
using SmarcivaZip.Core.Extraction;
using Xunit;

namespace SmarcivaZip.Tests;

/// <summary>
/// LZH の展開。Lhaplus の利用者が今も持っている形式なので、後継を名乗る以上は外せない。
///
/// 書庫は tools/make-lzh-fixture.py が仕様どおりに組み立てたもの。
/// LZH を作れるツールが手元に無いため自前で生成しているが、
/// ヘッダのチェックサムとデータの CRC-16 を 7-Zip が検証するので、
/// 読めて中身が一致している時点で構造は正しい。
///
/// LZH にはファイル名の文字コードを記録する場所が無く、日本語の書庫は
/// CP932 のバイト列がそのまま入っている。この資材もそうしてある。
/// </summary>
public sealed class LzhTests : IDisposable
{
    private readonly string _workDir;

    public LzhTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "smarcivazip-lzh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); }
        catch (IOException) { }
    }

    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "lzh-cp932.lzh");

    [Fact]
    public void LZHをLzhハンドラとして開く()
    {
        using ArchiveReader reader = ArchiveReader.Open(Fixture);

        Assert.Equal("Lzh", reader.Handler.Name);
        Assert.Equal(3, reader.Entries.Count);
    }

    [Fact]
    public void CP932のファイル名が読める()
    {
        using ArchiveReader reader = ArchiveReader.Open(Fixture);

        var paths = reader.Entries.Select(e => e.Path.Replace('\\', '/')).ToList();

        Assert.Contains("日本語の名前.txt", paths);
        Assert.Contains("readme.txt", paths);
    }

    [Fact]
    public void フォルダ構造が保たれる()
    {
        // LZH のディレクトリは拡張ヘッダに入っており、ファイル名欄とは別物。
        // ここが読めていないとフォルダが潰れて名前が繋がってしまう。
        using ArchiveReader reader = ArchiveReader.Open(Fixture);

        var paths = reader.Entries.Select(e => e.Path.Replace('\\', '/')).ToList();

        Assert.Contains("サブフォルダ/読みかた.txt", paths);
    }

    [Fact]
    public void 名前は7zdllではなく自前で読んだバイト列から作る()
    {
        // 7z.dll は LZH の名前を Windows のシステムのコードページで読む。
        // 日本語版 Windows ではそれが偶然 CP932 なので、上のテストはこの開発機では
        // 何もしなくても通ってしまい、英語版の CI でだけ落ちていた。
        // 指定したコードページがそのまま結果に出ることを確かめれば、
        // どの環境でも「システムの設定に左右されない」ことの確認になる。
        using ArchiveReader reader = ArchiveReader.Open(Fixture, new ArchiveOpenOptions { ForcedCodePage = 1252 });

        CodePageInfo.EnsureEncodingProviderRegistered();
        string expected = Encoding.GetEncoding(1252).GetString(Encoding.GetEncoding(932).GetBytes("日本語の名前.txt"));

        var paths = reader.Entries.Select(e => e.Path.Replace('\\', '/')).ToList();
        Assert.Contains(expected, paths);
        Assert.DoesNotContain("日本語の名前.txt", paths);
    }

    [Fact]
    public void 指定が無ければCP932で読んだと報告する()
    {
        using ArchiveReader reader = ArchiveReader.Open(Fixture);

        Assert.Equal(CodePageInfo.ShiftJis, reader.DetectedCodePage);
    }

    [Fact]
    public void ヘッダからフォルダと名前を分けて取り出せる()
    {
        IReadOnlyList<LzhEntryNameInfo>? entries = LzhHeaders.TryRead(Fixture);

        Assert.NotNull(entries);
        Assert.Equal(3, entries.Count);

        CodePageInfo.EnsureEncodingProviderRegistered();
        Encoding cp932 = Encoding.GetEncoding(932);
        Assert.Contains(entries, e => e.Decode(cp932) == @"サブフォルダ\読みかた.txt");
    }

    [Fact]
    public void 二バイト目が0x5Cの文字で名前が割れない()
    {
        // 「表」は CP932 で 0x95 0x5C。バイトのまま '\' で切ると文字が割れ、
        // 存在しないフォルダができてしまう。
        CodePageInfo.EnsureEncodingProviderRegistered();
        Encoding cp932 = Encoding.GetEncoding(932);

        var info = new LzhEntryNameInfo(
            DirectoryRaw: [.. cp932.GetBytes("資料"), 0xFF],
            NameRaw: cp932.GetBytes("表.txt"),
            OriginalSize: 0,
            IsDirectory: false);

        Assert.Equal(@"資料\表.txt", info.Decode(cp932));
    }

    [Fact]
    public void 展開すると中身が取り出せる()
    {
        using ArchiveReader reader = ArchiveReader.Open(Fixture);

        ExtractResult result = new ExtractService().Extract(reader, new ExtractOptions
        {
            OutputDirectory = _workDir,
            OpenFolderAfterExtract = false
        });

        // 展開が成功したということは、データの CRC-16 も一致している。
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.Equal(3, result.FilesExtracted);

        string root = result.DestinationRoot;
        Assert.Contains("LZH", File.ReadAllText(Path.Combine(root, "日本語の名前.txt")));
        Assert.Equal("よみかた", File.ReadAllText(Path.Combine(root, "サブフォルダ", "読みかた.txt")));
    }
}
