using System.Globalization;
using System.Text;

namespace SmarcivaZip.Core.Encodings;

/// <summary>
/// macOS 由来アーカイブのファイル名を Windows にとって自然な形に直す。
///
/// macOS (APFS/HFS+) はファイル名を UTF-8 の NFD で保存する。
/// たとえば「ガ」は U+30AC 1 文字ではなく U+30AB + U+3099（カ + 濁点）に分解される。
/// この状態の名前を Windows に展開すると、見た目が崩れる・検索でヒットしない・
/// 別アプリで開けないといった問題が起きる。Windows 側の慣習である NFC に直す。
/// </summary>
public static class NameNormalizer
{
    /// <summary>結合文字による分解が含まれているか（＝ NFD の疑いがあるか）。</summary>
    public static bool IsDecomposed(ReadOnlySpan<byte> utf8Bytes)
    {
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(utf8Bytes);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return IsDecomposed(text);
    }

    public static bool IsDecomposed(string text)
    {
        foreach (char c in text)
        {
            // 日本語の濁点・半濁点（macOS の ZIP で最も多い分解パターン）
            if (c is (char)0x3099 or (char)0x309A) return true;

            // 一般的な結合文字（ラテン系のアクセント、ハングルの字母分解など）
            if (CharUnicodeInfo.GetUnicodeCategory(c) is
                UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
            {
                return true;
            }

            // ハングルの字母（NFD だと音節が字母に分解される）
            if (c is >= (char)0x1100 and <= (char)0x11FF) return true;
        }

        return false;
    }

    /// <summary>NFC に正規化する。すでに NFC なら文字列をそのまま返す。</summary>
    public static string ToNfc(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        try
        {
            return text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            // 不正なサロゲートを含む場合。正規化できないのでそのまま返す。
            return text;
        }
    }

    private static readonly string[] MacJunkFileNames =
    [
        ".DS_Store",
        ".localized",
        ".apdisk",
        ".VolumeIcon.icns"
    ];

    private static readonly string[] MacJunkDirectoryNames =
    [
        "__MACOSX",
        ".fseventsd",
        ".Spotlight-V100",
        ".Trashes",
        ".TemporaryItems",
        ".DocumentRevisions-V100"
    ];

    /// <summary>
    /// macOS が ZIP に混ぜ込むメタデータかどうか。
    /// 展開すると中身が二重に見えてユーザーが混乱するため、既定で除外する。
    /// </summary>
    public static bool IsMacMetadata(string entryPath)
    {
        if (string.IsNullOrEmpty(entryPath)) return false;

        string[] segments = entryPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return false;

        foreach (string segment in segments)
        {
            if (MacJunkDirectoryNames.Contains(segment, StringComparer.Ordinal)) return true;
        }

        string fileName = segments[^1];

        if (MacJunkFileNames.Contains(fileName, StringComparer.Ordinal)) return true;

        // AppleDouble（リソースフォーク）。"._" で始まり、同名の本体ファイルと対になる。
        if (fileName.StartsWith("._", StringComparison.Ordinal)) return true;

        return false;
    }

    /// <summary>Windows 由来の Thumbs.db / desktop.ini も同様に無意味なので合わせて扱う。</summary>
    public static bool IsWindowsMetadata(string entryPath)
    {
        string fileName = Path.GetFileName(entryPath.Replace('\\', '/'));
        return fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);
    }
}
