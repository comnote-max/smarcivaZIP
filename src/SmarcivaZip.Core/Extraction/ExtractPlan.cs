using SmarcivaZip.Core.Encodings;
using SmarcivaZip.Core.Safety;

namespace SmarcivaZip.Core.Extraction;

public enum SkipReason
{
    None,
    MacMetadata,
    WindowsMetadata,
    SymbolicLink,
    UnsafePath
}

public sealed record PlannedEntry(ArchiveEntry Entry, string? TargetPath, SkipReason Skip)
{
    public bool WillExtract => Skip == SkipReason.None && TargetPath is not null;
}

public sealed class ExtractPlan
{
    public required string DestinationRoot { get; init; }
    public required IReadOnlyList<PlannedEntry> Entries { get; init; }
    public required bool CreatedWrapperFolder { get; init; }

    public IEnumerable<PlannedEntry> Extractable => Entries.Where(e => e.WillExtract);

    public IReadOnlyList<PlannedEntry> Rejected =>
        Entries.Where(e => e.Skip == SkipReason.UnsafePath).ToList();

    public ulong TotalBytes => Extractable.Where(e => !e.Entry.IsDirectory)
                                          .Aggregate(0UL, (sum, e) => sum + e.Entry.Size);

    /// <summary>
    /// 展開計画を立てる。ここで「どこに何を書くか」を全部決めてしまうので、
    /// 実際の展開処理はこの計画に従うだけになり、経路の安全性を一箇所で保証できる。
    /// </summary>
    public static ExtractPlan Create(ArchiveReader reader, ExtractOptions options)
    {
        string parentDirectory = options.OutputDirectory
                                 ?? Path.GetDirectoryName(Path.GetFullPath(reader.ArchivePath))
                                 ?? Directory.GetCurrentDirectory();

        var included = new List<ArchiveEntry>();
        var skipped = new List<PlannedEntry>();

        foreach (ArchiveEntry entry in reader.Entries)
        {
            SkipReason reason = ClassifySkip(entry, options);
            if (reason == SkipReason.None) included.Add(entry);
            else skipped.Add(new PlannedEntry(entry, null, reason));
        }

        bool needsWrapper = ShouldCreateWrapperFolder(included, options.FolderMode);

        string destinationRoot = needsWrapper
            ? PathSanitizer.MakeUniquePath(Path.Combine(parentDirectory, GetArchiveBaseName(reader.ArchivePath)))
            : parentDirectory;

        var planned = new List<PlannedEntry>(reader.Entries.Count);

        foreach (ArchiveEntry entry in included)
        {
            SanitizedPath resolved = PathSanitizer.Resolve(destinationRoot, entry.Path);

            planned.Add(resolved.IsSafe
                ? new PlannedEntry(entry, resolved.FullPath, SkipReason.None)
                : new PlannedEntry(entry, null, SkipReason.UnsafePath));
        }

        planned.AddRange(skipped);

        return new ExtractPlan
        {
            DestinationRoot = destinationRoot,
            Entries = planned.OrderBy(p => p.Entry.Index).ToList(),
            CreatedWrapperFolder = needsWrapper
        };
    }

    private static SkipReason ClassifySkip(ArchiveEntry entry, ExtractOptions options)
    {
        if (options.ExcludeMacMetadata && NameNormalizer.IsMacMetadata(entry.Path))
            return SkipReason.MacMetadata;

        if (options.ExcludeWindowsMetadata && NameNormalizer.IsWindowsMetadata(entry.Path))
            return SkipReason.WindowsMetadata;

        if (entry.IsSymbolicLink && !options.AllowSymbolicLinks)
            return SkipReason.SymbolicLink;

        return SkipReason.None;
    }

    /// <summary>
    /// いわゆる tar bomb（展開するとカレントに数百個のファイルが散らばる）を防ぐ。
    /// ルート直下が 1 つのフォルダにまとまっているアーカイブは、
    /// そのまま展開した方が二重フォルダにならず自然。
    /// </summary>
    private static bool ShouldCreateWrapperFolder(IReadOnlyList<ArchiveEntry> entries, OutputFolderMode mode)
    {
        switch (mode)
        {
            case OutputFolderMode.AlwaysCreate: return true;
            case OutputFolderMode.Never: return false;
        }

        if (entries.Count == 0) return false;

        var rootSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasRootLevelFile = false;

        foreach (ArchiveEntry entry in entries)
        {
            string normalized = entry.Path.Replace('\\', '/').Trim('/');
            if (normalized.Length == 0) continue;

            int slash = normalized.IndexOf('/');
            if (slash < 0)
            {
                rootSegments.Add(normalized);
                if (!entry.IsDirectory) hasRootLevelFile = true;
            }
            else
            {
                rootSegments.Add(normalized[..slash]);
            }
        }

        // ルート直下がちょうど 1 つのフォルダだけ → そのまま展開して二重を避ける。
        return rootSegments.Count != 1 || hasRootLevelFile;
    }

    private static readonly string[] CompoundExtensions =
    [
        ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst", ".tar.lz4", ".tar.lzma", ".tar.z"
    ];

    /// <summary>
    /// アーカイブ名から展開フォルダ名を作る。
    /// foo.tar.gz は "foo.tar" ではなく "foo" にする。
    /// </summary>
    public static string GetArchiveBaseName(string archivePath)
    {
        string fileName = Path.GetFileName(archivePath);

        foreach (string compound in CompoundExtensions)
        {
            if (fileName.EndsWith(compound, StringComparison.OrdinalIgnoreCase))
                return PathSanitizer.SanitizeSegment(fileName[..^compound.Length]);
        }

        string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return PathSanitizer.SanitizeSegment(
            withoutExtension.Length > 0 ? withoutExtension : fileName);
    }
}
