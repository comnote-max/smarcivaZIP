using SmarcivaZip.Core.Localization;

namespace SmarcivaZip.Core.Safety;

public sealed record BombAssessment(
    bool IsSuspicious,
    string? Reason,
    ulong TotalUncompressedSize,
    ulong ArchiveSize,
    double CompressionRatio,
    int EntryCount);

/// <summary>
/// いわゆる Zip Bomb（数 KB のアーカイブが展開すると数 TB になる）の検出。
///
/// 目的は「ブロックすること」ではなく「黙って走り出してディスクを埋めないこと」。
/// 疑わしいときはユーザーに確認を出し、了承されれば展開する。
/// </summary>
public static class ArchiveBombDetector
{
    /// <summary>これを超える展開後サイズは確認を出す（既定 20 GB）。</summary>
    public static ulong MaxTotalUncompressedSize { get; set; } = 20UL * 1024 * 1024 * 1024;

    /// <summary>圧縮率がこれを超えたら確認を出す。通常のデータで 200 倍を超えることはまず無い。</summary>
    public static double MaxCompressionRatio { get; set; } = 200.0;

    /// <summary>エントリ数の上限。大量の小ファイルでファイルシステムを詰まらせる攻撃への対策。</summary>
    public static int MaxEntryCount { get; set; } = 500_000;

    /// <summary>比率の判定を始める最小アーカイブサイズ。小さすぎると誤検知が増える。</summary>
    private const ulong RatioCheckMinimumArchiveSize = 4 * 1024;

    public static BombAssessment Assess(ulong archiveSize, ulong totalUncompressedSize, int entryCount)
    {
        double ratio = archiveSize == 0 ? 0 : (double)totalUncompressedSize / archiveSize;

        if (entryCount > MaxEntryCount)
        {
            return new BombAssessment(true,
                Strings.Format("Bomb_TooManyEntries", entryCount, MaxEntryCount),
                totalUncompressedSize, archiveSize, ratio, entryCount);
        }

        if (totalUncompressedSize > MaxTotalUncompressedSize)
        {
            return new BombAssessment(true,
                Strings.Format("Bomb_TooLarge", FormatSize(totalUncompressedSize)),
                totalUncompressedSize, archiveSize, ratio, entryCount);
        }

        if (archiveSize >= RatioCheckMinimumArchiveSize && ratio > MaxCompressionRatio)
        {
            return new BombAssessment(true,
                Strings.Format("Bomb_RatioTooHigh",
                    ratio.ToString("N0"), FormatSize(archiveSize), FormatSize(totalUncompressedSize)),
                totalUncompressedSize, archiveSize, ratio, entryCount);
        }

        return new BombAssessment(false, null, totalUncompressedSize, archiveSize, ratio, entryCount);
    }

    public static string FormatSize(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes:N0} {units[unit]}" : $"{value:N1} {units[unit]}";
    }
}
