using SmarcivaZip.Core.Shell;
using Xunit;

namespace SmarcivaZip.Tests;

/// <summary>
/// アイコンへのドロップ（動作の指定が無い起動）で、解凍か圧縮かを決める規則。
/// </summary>
public sealed class DropActionTests : IDisposable
{
    private static readonly string[] Associated = ["zip", "7z", "lzh"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "smarcivazip-drop-" + Guid.NewGuid().ToString("N"));

    public DropActionTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string File(string name)
    {
        string path = Path.Combine(_root, name);
        System.IO.File.WriteAllText(path, "x");
        return path;
    }

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    [Fact]
    public void 書庫だけなら解凍()
    {
        Assert.Equal(DropActionKind.Extract, DropAction.Decide([File("a.zip"), File("b.LZH")], Associated));
    }

    [Fact]
    public void 普通のファイルなら圧縮()
    {
        Assert.Equal(DropActionKind.Compress, DropAction.Decide([File("report.docx")], Associated));
    }

    [Fact]
    public void フォルダなら圧縮()
    {
        Assert.Equal(DropActionKind.Compress, DropAction.Decide([Folder("photos")], Associated));
    }

    [Fact]
    public void 拡張子が書庫でもフォルダなら圧縮()
    {
        Assert.Equal(DropActionKind.Compress, DropAction.Decide([Folder("backup.zip")], Associated));
    }

    [Fact]
    public void 書庫と普通のファイルが混ざれば全体を圧縮()
    {
        Assert.Equal(DropActionKind.Compress, DropAction.Decide([File("a.zip"), File("memo.txt")], Associated));
    }

    [Fact]
    public void 設定の一覧に無くても知っている書庫なら解凍()
    {
        // 「プログラムから開く」で smarcivaZIP を選んだ .iso をダブルクリックしたとき、
        // .iso.zip を作ってはいけない。
        Assert.Equal(DropActionKind.Extract, DropAction.Decide([File("disc.iso")], Associated));
    }

    [Fact]
    public void 拡張子の無いファイルは圧縮()
    {
        Assert.Equal(DropActionKind.Compress, DropAction.Decide([File("README")], Associated));
    }
}
