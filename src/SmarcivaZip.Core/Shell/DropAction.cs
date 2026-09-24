namespace SmarcivaZip.Core.Shell;

public enum DropActionKind
{
    Extract,
    Compress
}

/// <summary>
/// 動作を指定せずにパスだけ渡されたとき（アイコンへのドロップ、関連付けからの起動）に、
/// 解凍するか圧縮するかを決める。
///
/// 渡されたものが全部書庫なら解凍、ひとつでも書庫でないもの（普通のファイルやフォルダ）が
/// 混ざっていれば全体を圧縮する。Lhaplus のアイコンと同じ考え方。
/// 書庫と普通のファイルを一緒に落としたときに、書庫だけ解凍して残りを圧縮する、とはしない。
/// 1 回の操作で結果が 2 種類に分かれると、何が起きたのか分かりにくいため。
/// </summary>
public static class DropAction
{
    /// <param name="paths">渡されたパス。</param>
    /// <param name="archiveExtensions">
    /// 利用者が書庫として扱うと決めた拡張子（設定の関連付け対象）。右クリックメニューで
    /// 解凍の項目を出す判定と同じものを使い、両者の振る舞いをそろえる。
    /// </param>
    public static DropActionKind Decide(IEnumerable<string> paths, IEnumerable<string> archiveExtensions)
    {
        var extensions = new HashSet<string>(
            archiveExtensions.Select(e => e.Trim().TrimStart('.')), StringComparer.OrdinalIgnoreCase);

        bool any = false;
        foreach (string path in paths)
        {
            any = true;
            if (!IsArchive(path, extensions)) return DropActionKind.Compress;
        }

        return any ? DropActionKind.Extract : DropActionKind.Compress;
    }

    private static bool IsArchive(string path, HashSet<string> extensions)
    {
        if (Directory.Exists(path)) return false;

        string extension = Path.GetExtension(path).TrimStart('.');
        if (extension.Length == 0) return false;

        // 設定の一覧から外した拡張子でも、「プログラムから開く」で smarcivaZIP が選ばれていれば
        // ダブルクリックでここに来る。そのとき .iso を圧縮して .iso.zip を作るのは明らかに違うので、
        // smarcivaZIP が書庫として知っている拡張子も書庫とみなす。
        return extensions.Contains(extension) || ArchiveFileType.IsKnownExtension(extension);
    }
}
