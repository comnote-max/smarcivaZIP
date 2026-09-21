using System.Text;

namespace SmarcivaZip.Core.Safety;

public enum PathRejectionReason
{
    None,
    PathTraversal,
    AbsolutePath,
    EmptyName,
    TooLong
}

public sealed record SanitizedPath(string? FullPath, string RelativePath, PathRejectionReason Reason)
{
    public bool IsSafe => Reason == PathRejectionReason.None && FullPath is not null;
}

/// <summary>
/// アーカイブ内のエントリ名を、展開先の外に絶対に出られない形に正規化する。
///
/// アーカイブのエントリ名は攻撃者が自由に決められるので、そのまま Path.Combine に
/// 渡してはいけない。"../../../Windows/System32/..." や "C:\..." を入れて
/// 展開先の外にファイルを書かせる攻撃が Zip Slip として知られている。
/// </summary>
public static class PathSanitizer
{
    private static readonly char[] InvalidNameChars = ['<', '>', ':', '"', '|', '?', '*'];

    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    /// <summary>
    /// エントリ名を展開先ルート配下の実パスに解決する。
    /// ルート外に出る場合は FullPath が null になる。
    /// </summary>
    public static SanitizedPath Resolve(string destinationRoot, string entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath))
            return new SanitizedPath(null, string.Empty, PathRejectionReason.EmptyName);

        string normalized = entryPath.Replace('\\', '/');

        // "C:/..." や "//server/share" のような絶対パスは無条件で拒否する。
        if (IsAbsolute(normalized))
            return new SanitizedPath(null, normalized, PathRejectionReason.AbsolutePath);

        var segments = new List<string>();

        foreach (string rawSegment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawSegment == ".") continue;

            if (rawSegment == "..")
            {
                // ルートより上に出ようとした時点で攻撃とみなす。
                // 深さが足りている場合だけ 1 段戻ることも考えられるが、
                // 正規のアーカイブに ".." は出てこないので一律で拒否する方が安全。
                return new SanitizedPath(null, normalized, PathRejectionReason.PathTraversal);
            }

            string safeSegment = SanitizeSegment(rawSegment);
            if (safeSegment.Length > 0) segments.Add(safeSegment);
        }

        if (segments.Count == 0)
            return new SanitizedPath(null, normalized, PathRejectionReason.EmptyName);

        string relative = string.Join(Path.DirectorySeparatorChar, segments);
        string combined = Path.Combine(destinationRoot, relative);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(combined);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new SanitizedPath(null, relative, PathRejectionReason.TooLong);
        }

        // 正規化後にもう一度、本当にルート配下かを確認する。
        // シンボリックリンクや 8.3 名を経由した迂回もここで弾ける。
        if (!IsUnderRoot(destinationRoot, fullPath))
            return new SanitizedPath(null, relative, PathRejectionReason.PathTraversal);

        return new SanitizedPath(fullPath, relative, PathRejectionReason.None);
    }

    private static bool IsAbsolute(string normalized)
    {
        if (normalized.StartsWith('/')) return true;
        if (normalized.Length >= 2 && normalized[1] == ':' && char.IsLetter(normalized[0])) return true;
        return false;
    }

    public static bool IsUnderRoot(string root, string candidate)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;
        string normalizedCandidate = Path.GetFullPath(candidate);

        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 1 階層分の名前を Windows のファイル名として成立する形に直す。
    /// </summary>
    public static string SanitizeSegment(string segment)
    {
        var builder = new StringBuilder(segment.Length);

        foreach (char c in segment)
        {
            // 代替データストリーム注入（"name:stream"）と、ファイル名に使えない文字を潰す。
            if (char.IsControl(c) || InvalidNameChars.Contains(c))
            {
                builder.Append('_');
                continue;
            }
            builder.Append(c);
        }

        // Windows は末尾のドットと空白を黙って削る。
        // 削られた結果ほかのファイルと衝突するのを避けるため、こちらで明示的に処理する。
        string trimmed = builder.ToString().TrimEnd(' ', '.');
        if (trimmed.Length == 0) return segment.Length > 0 ? "_" : string.Empty;

        if (IsReservedDeviceName(trimmed)) trimmed += "_";

        return trimmed;
    }

    /// <summary>CON.txt のように拡張子が付いていても予約名として扱われる点に注意。</summary>
    public static bool IsReservedDeviceName(string name)
    {
        int dot = name.IndexOf('.');
        string baseName = dot < 0 ? name : name[..dot];
        return ReservedDeviceNames.Contains(baseName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// MAX_PATH (260) を超えるパスを扱えるよう \\?\ プレフィックスを付ける。
    /// アプリマニフェストで longPathAware を有効にしていても、
    /// 明示的に付けた方が確実に動く。
    /// </summary>
    public static string ToExtendedLengthPath(string fullPath)
    {
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal)) return fullPath;
        if (fullPath.Length < 248) return fullPath;

        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + fullPath[2..];

        return @"\\?\" + fullPath;
    }

    /// <summary>
    /// 既存ファイルと衝突したとき "名前 (2).txt" のような代替名を返す。
    /// </summary>
    public static string MakeUniquePath(string fullPath)
    {
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath)) return fullPath;

        string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        string baseName = Path.GetFileNameWithoutExtension(fullPath);
        string extension = Path.GetExtension(fullPath);

        for (int i = 2; i < 10000; i++)
        {
            string candidate = Path.Combine(directory, $"{baseName} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }

        return Path.Combine(directory, $"{baseName} ({Guid.NewGuid():N}){extension}");
    }
}
