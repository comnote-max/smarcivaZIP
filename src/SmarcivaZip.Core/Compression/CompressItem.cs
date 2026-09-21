namespace SmarcivaZip.Core.Compression;

/// <summary>アーカイブに入れる 1 項目。</summary>
public sealed class CompressItem
{
    /// <summary>アーカイブ内での相対パス。区切りは '/'。</summary>
    public required string ArchivePath { get; init; }

    /// <summary>元ファイルの絶対パス。ディレクトリエントリでは中身を読まない。</summary>
    public required string SourcePath { get; init; }

    public required bool IsDirectory { get; init; }
    public required ulong Size { get; init; }
    public required DateTime LastWriteTime { get; init; }
    public required DateTime CreationTime { get; init; }
    public required FileAttributes Attributes { get; init; }

    public override string ToString() => ArchivePath;
}

public static class CompressItemCollector
{
    /// <summary>
    /// 選択されたファイル・フォルダから、アーカイブに入れる項目一覧を作る。
    ///
    /// アーカイブ内のパスは「選択された項目の親フォルダ」からの相対パスにする。
    /// こうすると C:\work\data を圧縮したときに data\... という自然な構造になり、
    /// 展開側の「フォルダにまとめる」判定とも噛み合う。
    /// </summary>
    public static List<CompressItem> Collect(IEnumerable<string> inputPaths, CancellationToken cancellationToken = default)
    {
        var items = new List<CompressItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string rawPath in inputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fullPath = Path.GetFullPath(rawPath.TrimEnd(Path.DirectorySeparatorChar));
            string parent = Path.GetDirectoryName(fullPath) ?? fullPath;

            if (Directory.Exists(fullPath))
            {
                AddDirectory(items, seen, fullPath, parent, cancellationToken);
            }
            else if (File.Exists(fullPath))
            {
                AddFile(items, seen, fullPath, parent);
            }
        }

        return items;
    }

    private static void AddDirectory(
        List<CompressItem> items, HashSet<string> seen,
        string directory, string basePath, CancellationToken cancellationToken)
    {
        AddEntry(items, seen, directory, basePath, isDirectory: true);

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateFileSystemEntries(directory);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return;
        }

        foreach (string child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 再解析ポイント（ジャンクション・シンボリックリンク）は辿らない。
            // 辿ると無限ループやディスク全体の巻き込みが起きる。
            var info = new FileInfo(child);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

            if (Directory.Exists(child)) AddDirectory(items, seen, child, basePath, cancellationToken);
            else AddFile(items, seen, child, basePath);
        }
    }

    private static void AddFile(List<CompressItem> items, HashSet<string> seen, string file, string basePath)
        => AddEntry(items, seen, file, basePath, isDirectory: false);

    private static void AddEntry(
        List<CompressItem> items, HashSet<string> seen,
        string path, string basePath, bool isDirectory)
    {
        string relative = Path.GetRelativePath(basePath, path).Replace('\\', '/');
        if (relative is "." or "..") return;
        if (!seen.Add(relative)) return;

        try
        {
            var info = new FileInfo(path);
            items.Add(new CompressItem
            {
                ArchivePath = relative,
                SourcePath = path,
                IsDirectory = isDirectory,
                Size = isDirectory ? 0 : (ulong)info.Length,
                LastWriteTime = info.LastWriteTime,
                CreationTime = info.CreationTime,
                Attributes = info.Attributes
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 読めないファイルは黙って飛ばす。
        }
    }

    /// <summary>
    /// 出力アーカイブの既定の名前を決める。
    /// 1 項目だけならその名前、複数なら親フォルダ名を使う。
    /// </summary>
    public static string SuggestArchiveName(IReadOnlyList<string> inputPaths, OutputFormat format)
    {
        if (inputPaths.Count == 0) return "archive" + format.Extension;

        if (inputPaths.Count == 1)
        {
            string path = inputPaths[0].TrimEnd(Path.DirectorySeparatorChar);
            string name = Directory.Exists(path)
                ? Path.GetFileName(path)
                : Path.GetFileNameWithoutExtension(path);

            if (!string.IsNullOrEmpty(name)) return name + format.Extension;
        }

        string? parent = Path.GetDirectoryName(Path.GetFullPath(inputPaths[0]));
        string parentName = string.IsNullOrEmpty(parent) ? "archive" : Path.GetFileName(parent);

        return (string.IsNullOrEmpty(parentName) ? "archive" : parentName) + format.Extension;
    }
}
