namespace SmarcivaZip.Core.Safety;

/// <summary>
/// Mark of the Web (MOTW) の引き継ぎ。
///
/// インターネットから落としたファイルには Zone.Identifier という代替データ
/// ストリームが付き、Windows / Office / SmartScreen はこれを見て
/// 「信用できないファイル」として扱う。書庫を展開したときに中身へ引き継がないと、
/// 「zip に入れるだけで警告を回避できる」という穴になる。
/// 実際これは ISO / ZIP 経由のマルウェア配布で広く悪用された。
/// </summary>
public static class MarkOfTheWeb
{
    private const string StreamSuffix = ":Zone.Identifier";

    /// <summary>アーカイブに付いている Zone.Identifier の中身。無ければ null。</summary>
    public static string? Read(string filePath)
    {
        try
        {
            string streamPath = filePath + StreamSuffix;
            return File.Exists(streamPath) ? File.ReadAllText(streamPath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>信用できないゾーン（インターネット / 制限付きサイト）かどうか。</summary>
    public static bool IsUntrusted(string? zoneIdentifierContent)
    {
        if (string.IsNullOrEmpty(zoneIdentifierContent)) return false;

        foreach (string line in zoneIdentifierContent.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase)) continue;

            // 3 = インターネット, 4 = 制限付きサイト
            return trimmed[7..].Trim() is "3" or "4";
        }

        return false;
    }

    /// <summary>展開したファイルに同じ Zone.Identifier を書き込む。</summary>
    public static void Apply(string filePath, string zoneIdentifierContent)
    {
        try
        {
            File.WriteAllText(PathSanitizer.ToExtendedLengthPath(filePath) + StreamSuffix,
                zoneIdentifierContent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // FAT32 / exFAT など ADS を持てないファイルシステムでは黙って諦める。
        }
    }

    public static void ApplyToAll(IEnumerable<string> filePaths, string zoneIdentifierContent)
    {
        foreach (string path in filePaths) Apply(path, zoneIdentifierContent);
    }
}
