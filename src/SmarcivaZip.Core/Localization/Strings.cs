using System.Collections;
using System.Reflection;
using System.Globalization;
using System.Resources;

// 既定の言語は英語。訳が無い言語はここに落ちる。
[assembly: NeutralResourcesLanguage("en")]

namespace SmarcivaZip.Core.Localization;

/// <summary>
/// 画面に出す文字列。
///
/// 強く型付けされた自動生成クラス（*.Designer.cs）は使わない。
/// あれは Visual Studio のデザイン時ツールが作るもので、dotnet build だけの
/// CI では生成されず、生成物をコミットすると resx とずれる余地が残る。
/// ここでは ResourceManager を直接引き、キーの存在はテストで担保する。
/// </summary>
public static class Strings
{
    private const string BaseName = "SmarcivaZip.Core.Resources.Strings";

    private static readonly ResourceManager Manager = new(BaseName, typeof(Strings).Assembly);

    /// <summary>
    /// キーに対応する文字列を返す。
    /// 見つからないときはキーそのものを返す。翻訳漏れがあっても
    /// 画面が空欄になるより、キー名が出ている方が原因を追いやすい。
    /// </summary>
    public static string Get(string key)
        => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object?[] arguments)
        => string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);

    /// <summary>指定した言語にその文字列が用意されているか（テスト用）。</summary>
    public static bool Has(string key, CultureInfo culture)
        => Manager.GetString(key, culture) is not null;

    /// <summary>その言語のリソースに入っているキーをすべて返す（テスト用）。</summary>
    public static IReadOnlyList<string> Keys(CultureInfo culture)
    {
        // 親カルチャーへのフォールバックを切って、その言語のファイルだけを見る。
        ResourceSet? set = Manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        if (set is null) return [];

        var keys = new List<string>();
        foreach (DictionaryEntry entry in set)
        {
            if (entry.Key is string key) keys.Add(key);
        }

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }
}
