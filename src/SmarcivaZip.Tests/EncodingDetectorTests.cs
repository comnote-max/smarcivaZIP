using System.Text;
using SmarcivaZip.Core.Encodings;
using Xunit;

namespace SmarcivaZip.Tests;

public class EncodingDetectorTests
{
    public EncodingDetectorTests() => CodePageInfo.EnsureEncodingProviderRegistered();

    private static byte[] Encode(int codePage, string text)
        => Encoding.GetEncoding(codePage).GetBytes(text);

    [Fact]
    public void 日本語のファイル名をCP932と判定する()
    {
        string[] names =
        [
            "写真フォルダ/夏の思い出.jpg",
            "資料/第1章 はじめに.txt",
            "ドキュメント/読みかた.pdf"
        ];

        var result = EncodingDetector.Detect(names.Select(n => Encode(CodePageInfo.ShiftJis, n)));

        Assert.Equal(CodePageInfo.ShiftJis, result.CodePage);
        Assert.False(result.AllAscii);
    }

    [Fact]
    public void UTF8のファイル名をUTF8と判定する()
    {
        string[] names = ["画像/猫の写真.png", "音楽/夜明けのうた.mp3"];

        var result = EncodingDetector.Detect(names.Select(n => Encode(CodePageInfo.Utf8, n)));

        Assert.Equal(CodePageInfo.Utf8, result.CodePage);
        Assert.True(result.Confidence > 0.9);
    }

    [Fact]
    public void ASCIIのみならコードページ判定を求めない()
    {
        string[] names = ["docs/readme.txt", "src/main.c"];

        var result = EncodingDetector.Detect(names.Select(n => Encode(CodePageInfo.Utf8, n)));

        Assert.True(result.AllAscii);
        Assert.False(result.NeedsUserConfirmation);
    }

    [Fact]
    public void 韓国語のファイル名をEUCKRと判定する()
    {
        string[] names = ["사진/여름휴가.jpg", "문서/보고서.txt"];

        var result = EncodingDetector.Detect(names.Select(n => Encode(CodePageInfo.EucKr, n)));

        Assert.Equal(CodePageInfo.EucKr, result.CodePage);
    }

    [Fact]
    public void 簡体字中国語のファイル名をGBKと判定する()
    {
        string[] names = ["照片/夏天的回忆.jpg", "文档/说明文件.txt", "音乐/我的歌单.mp3"];

        var result = EncodingDetector.Detect(names.Select(n => Encode(CodePageInfo.Gbk, n)));

        Assert.Equal(CodePageInfo.Gbk, result.CodePage);
    }

    [Fact]
    public void 西欧のファイル名をWindows1252と判定する()
    {
        string[] names = ["café/résumé.txt", "Grüße.doc"];

        var result = EncodingDetector.Detect(names.Select(n => Encode(CodePageInfo.Latin1252, n)));

        Assert.Equal(CodePageInfo.Latin1252, result.CodePage);
    }

    [Fact]
    public void キリル文字のファイル名をCP866と判定する()
    {
        string[] names = ["документы/отчёт.txt", "фото/лето.jpg"];

        var result = EncodingDetector.Detect(names.Select(n => Encode(CodePageInfo.Cyrillic866, n)));

        Assert.Equal(CodePageInfo.Cyrillic866, result.CodePage);
    }

    [Fact]
    public void 候補が僅差のときは確認を求める()
    {
        // 韓国語と簡体字中国語はバイト構造が近く、短い名前では差が付きにくい。
        // こういう場合は黙って展開せず、プレビューで確認させるのが正しい。
        string[] names = ["사진/여름휴가.jpg", "문서/보고서.txt"];

        var result = EncodingDetector.Detect(names.Select(n => Encode(CodePageInfo.EucKr, n)));

        Assert.Equal(CodePageInfo.EucKr, result.CodePage);
        Assert.True(result.NeedsUserConfirmation);
    }

    [Fact]
    public void 候補一覧には複数のコードページが並ぶ()
    {
        var result = EncodingDetector.Detect([Encode(CodePageInfo.ShiftJis, "設定ファイル.ini")]);

        Assert.True(result.Ranked.Count > 1);
        Assert.Contains(result.Ranked, g => g.CodePage.CodePage == CodePageInfo.ShiftJis);
    }
}

public class NameNormalizerTests
{
    [Fact]
    public void macOSのNFD名を検出する()
    {
        // 「ガ」を カ + 濁点 に分解したもの（macOS が実際に記録する形）
        string decomposed = "ガ" + "ラス.png";

        Assert.True(NameNormalizer.IsDecomposed(decomposed));
    }

    [Fact]
    public void NFD名をNFCに正規化する()
    {
        string decomposed = "ガ" + "ラス.png";
        string normalized = NameNormalizer.ToNfc(decomposed);

        Assert.Equal("ガラス.png", normalized);
        Assert.False(NameNormalizer.IsDecomposed(normalized));
    }

    [Fact]
    public void 通常の日本語名はNFDと誤判定しない()
    {
        Assert.False(NameNormalizer.IsDecomposed("ガラスの写真.png"));
    }

    [Theory]
    [InlineData("__MACOSX/foo/._bar.txt")]
    [InlineData("フォルダ/._画像.jpg")]
    [InlineData("project/.DS_Store")]
    [InlineData(".Spotlight-V100/store.db")]
    public void macOSのメタデータを除外対象と判定する(string path)
    {
        Assert.True(NameNormalizer.IsMacMetadata(path));
    }

    [Theory]
    [InlineData("docs/readme.txt")]
    [InlineData("画像/写真.jpg")]
    [InlineData("_private/notes.md")]
    public void 通常のファイルは除外対象にしない(string path)
    {
        Assert.False(NameNormalizer.IsMacMetadata(path));
    }
}
