using System.Globalization;
using SmarcivaZip.Core.Encodings;

namespace SmarcivaZip.Core.Localization;

/// <summary>画面の表示言語。</summary>
public sealed record AppLanguage(string Code, string DisplayName)
{
    /// <summary>Windows の表示言語に従う。</summary>
    public static readonly AppLanguage Auto = new(string.Empty, "システムに合わせる / Use system language");

    /// <summary>
    /// 選べる言語。
    ///
    /// 訳が用意できている言語だけをここに並べる。
    /// 一覧に出しておいて中身が未翻訳、というのが利用者にとって一番困る状態なので、
    /// 翻訳が揃ってから追加する。
    /// </summary>
    public static readonly IReadOnlyList<AppLanguage> Available =
    [
        Auto,
        new("ja", "日本語"),
        new("en", "English")
    ];

    public override string ToString() => DisplayName;

    /// <summary>設定された言語を、このプロセスの表示言語として適用する。</summary>
    public static void Apply(string? code)
    {
        CultureInfo culture = Resolve(code);

        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private static CultureInfo Resolve(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return CultureInfo.InstalledUICulture;

        try { return CultureInfo.GetCultureInfo(code); }
        catch (CultureNotFoundException) { return CultureInfo.InstalledUICulture; }
    }

    /// <summary>
    /// 表示言語から、文字コード判定で優先すべきコードページを決める。
    ///
    /// ZIP のファイル名は判定が割れることがあり、そのときは利用者の言語圏を
    /// 優先するのが最も当たる。以前は日本語固定だったため、
    /// 韓国語の利用者が韓国語の書庫を開いても日本語が優先されていた。
    /// </summary>
    public static int PreferredCodePageFor(CultureInfo culture)
    {
        string language = culture.TwoLetterISOLanguageName.ToLowerInvariant();

        return language switch
        {
            "ja" => CodePageInfo.ShiftJis,
            "ko" => CodePageInfo.EucKr,
            "zh" => IsTraditionalChinese(culture) ? CodePageInfo.Big5 : CodePageInfo.Gbk,
            "ru" or "uk" or "be" or "bg" or "sr" or "mk" => CodePageInfo.Cyrillic866,
            _ => CodePageInfo.Latin1252
        };
    }

    /// <summary>
    /// 繁体字圏かどうか。台湾・香港・マカオが繁体字で、それ以外の中国語は簡体字。
    /// zh-Hant のようにスクリプトが明示されている場合はそれを優先する。
    /// </summary>
    private static bool IsTraditionalChinese(CultureInfo culture)
    {
        string name = culture.Name.ToLowerInvariant();

        if (name.Contains("hant")) return true;
        if (name.Contains("hans")) return false;

        return name.Contains("-tw") || name.Contains("-hk") || name.Contains("-mo");
    }
}
