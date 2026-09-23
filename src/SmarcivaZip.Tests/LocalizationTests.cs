using System.Globalization;
using System.Text.RegularExpressions;
using SmarcivaZip.Core.Encodings;
using SmarcivaZip.Core.Localization;
using Xunit;

namespace SmarcivaZip.Tests;

/// <summary>
/// 翻訳の抜けは動かしてみるまで気付きにくく、しかも気付いたときには
/// 利用者の画面にキー名が出ている。ここで機械的に潰す。
/// </summary>
public class LocalizationTests
{
    // 英語は中立リソース（衛星アセンブリを持たない）なので InvariantCulture 側に入る。
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;
    private static readonly CultureInfo Japanese = CultureInfo.GetCultureInfo("ja");

    /// <summary>ソースを走査するためにリポジトリのルートを探す。</summary>
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "smarcivaZIP.sln")))
                    return directory.FullName;

                directory = directory.Parent;
            }

            throw new InvalidOperationException("リポジトリのルートが見つかりませんでした。");
        }
    }

    private static IEnumerable<string> SourceFiles()
        => Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        // テスト自身はキー名や日本語をリテラルで書くので対象外
                        && !path.Contains("SmarcivaZip.Tests"));

    /// <summary>
    /// 一覧に出している言語すべて。ここに載せた以上は訳が揃っている必要がある。
    /// </summary>
    public static TheoryData<string> OfferedLanguages()
    {
        var data = new TheoryData<string>();

        foreach (AppLanguage language in AppLanguage.Available)
        {
            if (language.Code.Length > 0) data.Add(language.Code);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(OfferedLanguages))]
    public void 一覧に出す言語は訳が揃っている(string code)
    {
        var culture = CultureInfo.GetCultureInfo(code);

        IReadOnlyList<string> english = Strings.Keys(English);
        IReadOnlyList<string> translated = Strings.Keys(culture);

        Assert.NotEmpty(english);

        var missing = english.Except(translated).ToList();
        var extra = translated.Except(english).ToList();

        Assert.True(missing.Count == 0,
            $"{code} の訳がありません ({missing.Count} 件): " + string.Join(", ", missing.Take(10)));
        Assert.True(extra.Count == 0,
            $"{code} に余分なキーがあります: " + string.Join(", ", extra.Take(10)));
    }

    [Theory]
    [MemberData(nameof(OfferedLanguages))]
    public void 書式指定子が言語間でずれていない(string code)
    {
        // 片方だけ {0} を持っていると、その言語でだけ値が抜け落ちる。
        var culture = CultureInfo.GetCultureInfo(code);
        var mismatched = new List<string>();

        foreach (string key in Strings.Keys(English))
        {
            if (!PlaceholdersOf(key, English).SetEquals(PlaceholdersOf(key, culture)))
                mismatched.Add(key);
        }

        Assert.True(mismatched.Count == 0,
            $"{code} で書式指定子が一致しません: " + string.Join(", ", mismatched));
    }

    [Theory]
    [MemberData(nameof(OfferedLanguages))]
    public void 訳が英語のままになっていない(string code)
    {
        // 英語は中立リソースそのものなので比べる意味がない。
        if (code == "en") return;

        // 訳し忘れを拾う。英語そのままで正しい語（OK など）もあるので、
        // 一定の割合を超えたときだけ失敗させる。
        var culture = CultureInfo.GetCultureInfo(code);

        IReadOnlyList<string> keys = Strings.Keys(English);
        int untouched = keys.Count(key => ValueIn(key, English) == ValueIn(key, culture));

        Assert.True(untouched < keys.Count / 4,
            $"{code} は {untouched}/{keys.Count} 件が英語のままです");
    }

    private static string ValueIn(string key, CultureInfo culture)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = culture;
            return Strings.Get(key);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static HashSet<string> PlaceholdersOf(string key, CultureInfo culture)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = culture;
            return Regex.Matches(Strings.Get(key), @"\{(\d+)[^}]*\}")
                        .Select(m => m.Groups[1].Value)
                        .ToHashSet(StringComparer.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void ソースが参照しているキーがすべて存在する()
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        // Strings.Get("X") / Strings.Format("X", ...) / {loc:L X}
        var patterns = new[]
        {
            new Regex(@"Strings\.(?:Get|Format)\(""([A-Za-z0-9_]+)""", RegexOptions.Compiled),
            new Regex(@"\{loc:L\s+([A-Za-z0-9_]+)\s*\}", RegexOptions.Compiled)
        };

        foreach (string file in SourceFiles())
        {
            string text = File.ReadAllText(file);
            foreach (Regex pattern in patterns)
            {
                foreach (Match match in pattern.Matches(text)) referenced.Add(match.Groups[1].Value);
            }
        }

        Assert.NotEmpty(referenced);

        var missing = referenced.Where(key => !Strings.Has(key, English)).ToList();

        Assert.True(missing.Count == 0, "リソースに無いキーを参照しています: " + string.Join(", ", missing));
    }

    [Fact]
    public void 画面に出るコードから日本語の直書きが消えている()
    {
        // 翻訳の仕組みを入れた後に直書きが混ざると、その行だけ日本語のままになる。
        // コメントと XML ドキュメントは対象外（開発者向けなので日本語のままでよい）。
        var literal = new Regex("\"(?:[^\"\\\\\\n]|\\\\.)*[\u3040-\u30ff\u4e00-\u9fff][^\"\\n]*\"",
            RegexOptions.Compiled);

        var offenders = new List<string>();

        foreach (string file in SourceFiles())
        {
            if (file.EndsWith("AppLanguage.cs", StringComparison.Ordinal)) continue;

            foreach (string line in File.ReadAllLines(file))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("<!--"))
                    continue;

                if (literal.IsMatch(line))
                    offenders.Add($"{Path.GetFileName(file)}: {trimmed}");
            }
        }

        Assert.True(offenders.Count == 0,
            "日本語が直書きされています:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Theory]
    [InlineData("ja", CodePageInfo.ShiftJis)]
    [InlineData("ko", CodePageInfo.EucKr)]
    [InlineData("zh-Hans", CodePageInfo.Gbk)]
    [InlineData("zh-CN", CodePageInfo.Gbk)]
    [InlineData("zh-Hant", CodePageInfo.Big5)]
    [InlineData("zh-TW", CodePageInfo.Big5)]
    [InlineData("ru", CodePageInfo.Cyrillic866)]
    [InlineData("en", CodePageInfo.Latin1252)]
    [InlineData("de", CodePageInfo.Latin1252)]
    public void 表示言語から優先コードページが決まる(string culture, int expected)
    {
        Assert.Equal(expected, AppLanguage.PreferredCodePageFor(CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void 未対応の言語では英語にフォールバックする()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            AppLanguage.Apply("de");
            Assert.Equal("Register", Strings.Get("Setup_Register"));

            AppLanguage.Apply("ja");
            Assert.Equal("登録する", Strings.Get("Setup_Register"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void 知らないキーはキー名がそのまま返る()
    {
        Assert.Equal("No_Such_Key", Strings.Get("No_Such_Key"));
    }
}
