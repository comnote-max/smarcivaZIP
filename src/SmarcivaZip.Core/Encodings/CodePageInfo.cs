using System.Text;
using SmarcivaZip.Core.Localization;

namespace SmarcivaZip.Core.Encodings;

/// <summary>
/// ファイル名の文字コード候補。UI のドロップダウンにもそのまま使う。
/// </summary>
public sealed record CodePageInfo(int CodePage, string DisplayName, string LanguageKey)
{
    /// <summary>「日本語」「簡体字中国語」など、表示言語に合わせた言語名。</summary>
    public string Language => Strings.Get(LanguageKey);

    public const int Utf8 = 65001;
    public const int ShiftJis = 932;
    public const int Gbk = 936;
    public const int EucKr = 949;
    public const int Big5 = 950;
    public const int Cyrillic866 = 866;
    public const int Latin1252 = 1252;
    public const int Oem437 = 437;

    /// <summary>判定の対象にするコードページ。並び順は同点時の優先順位を兼ねる。</summary>
    public static readonly IReadOnlyList<CodePageInfo> Candidates =
    [
        new(Utf8, "UTF-8", "CodePage_Unicode"),
        new(ShiftJis, "Shift_JIS (CP932)", "CodePage_Japanese"),
        new(Gbk, "GBK (CP936)", "CodePage_SimplifiedChinese"),
        new(Big5, "Big5 (CP950)", "CodePage_TraditionalChinese"),
        new(EucKr, "EUC-KR (CP949)", "CodePage_Korean"),
        new(Cyrillic866, "CP866", "CodePage_Cyrillic"),
        new(Latin1252, "Windows-1252", "CodePage_WesternEuropean"),
        new(Oem437, "CP437", "CodePage_Oem")
    ];

    private static bool _providerRegistered;
    private static readonly Lock RegistrationLock = new();

    /// <summary>
    /// .NET Core 以降、CP932 などのレガシーコードページは既定では使えない。
    /// 使う前に一度だけプロバイダを登録する。
    /// </summary>
    public static void EnsureEncodingProviderRegistered()
    {
        if (_providerRegistered) return;
        lock (RegistrationLock)
        {
            if (_providerRegistered) return;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _providerRegistered = true;
        }
    }

    /// <summary>不正なバイト列を検出できるよう、例外フォールバック付きでエンコーディングを取得する。</summary>
    public static Encoding? GetStrictEncoding(int codePage)
    {
        EnsureEncodingProviderRegistered();
        try
        {
            return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>実際に表示するとき用。判定不能なバイトは U+FFFD になる。</summary>
    public static Encoding? GetLenientEncoding(int codePage)
    {
        EnsureEncodingProviderRegistered();
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public static CodePageInfo? Find(int codePage)
        => Candidates.FirstOrDefault(c => c.CodePage == codePage);

    public override string ToString() => $"{DisplayName} — {Language}";
}
