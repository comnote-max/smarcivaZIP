using System.Text;
using SmarcivaZip.Core.Compression;
using SmarcivaZip.Core.Localization;

namespace SmarcivaZip.Core.Shell;

/// <summary>ストア版の右クリックメニューの 1 項目。</summary>
/// <param name="Kind">compress / extract / settings / separator のいずれか。</param>
/// <param name="Title">表示する文字列（すでに表示言語に訳してあるもの）。</param>
/// <param name="Arguments">選んだときに SmarcivaZip.exe へ渡す引数。パスは含まない。</param>
public sealed record ModernMenuEntry(string Kind, string Title, string Arguments);

/// <summary>
/// ストア版（MSIX）の右クリックメニューの中身を、ファイルに書き出す。
///
/// Windows 11 の新しいメニューに項目を出すのは、パッケージに同梱した C++ の部品
/// （SmarcivaZip.ShellExt.dll）で、エクスプローラーとは別のプロセスで動く。
/// どの圧縮形式を何語で並べるかという知識をその部品に持たせると、
/// 翻訳や設定を 2 か所で管理することになるので、アプリがここで決めた結果を
/// ファイルに書き、部品はそれを読んで並べるだけにしている。
///
/// 書式は UTF-8 のタブ区切り。部品側の読み取り（ShellExt.cpp の LoadMenu）と対になっている。
/// <code>
/// # smarcivaZIP context menu v1
/// extensions	zip;7z;rar;...
/// compress	ZIP に圧縮	--compress zip
/// separator
/// extract	ここに解凍	--extract
/// settings	設定...	--settings
/// </code>
/// </summary>
public static class ModernMenuFile
{
    public const string FileName = "menu.tsv";
    public const string Header = "# smarcivaZIP context menu v1";

    public const string KindCompress = "compress";
    public const string KindExtract = "extract";
    public const string KindSettings = "settings";
    public const string KindSeparator = "separator";

    /// <summary>設定画面で選んだ圧縮形式の並びから、メニューの項目を組み立てる。</summary>
    public static IReadOnlyList<ModernMenuEntry> Build(IReadOnlyList<CompressMenuItem> compressItems)
    {
        var entries = new List<ModernMenuEntry>();

        foreach (CompressMenuItem item in compressItems)
            entries.Add(new ModernMenuEntry(KindCompress, item.Label, item.Arguments));

        if (entries.Count > 0) entries.Add(new ModernMenuEntry(KindSeparator, "", ""));

        foreach ((_, string labelKey, string argument) in ShellRegistration.ExtractVerbs)
            entries.Add(new ModernMenuEntry(KindExtract, Strings.Get(labelKey), argument));

        entries.Add(new ModernMenuEntry(KindSeparator, "", ""));
        entries.Add(new ModernMenuEntry(KindSettings, Strings.Get("Menu_Settings"), "--settings"));

        return entries;
    }

    /// <summary>ファイルの中身を作る。解凍の項目は <paramref name="archiveExtensions"/> のファイルにだけ出る。</summary>
    public static string Serialize(IReadOnlyList<ModernMenuEntry> entries, IEnumerable<string> archiveExtensions)
    {
        var text = new StringBuilder();
        text.Append(Header).Append('\n');

        string extensions = string.Join(';', archiveExtensions
            .Select(e => e.Trim().TrimStart('.').ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Distinct());
        text.Append("extensions\t").Append(extensions).Append('\n');

        foreach (ModernMenuEntry entry in entries)
        {
            if (entry.Kind == KindSeparator)
            {
                text.Append(KindSeparator).Append('\n');
                continue;
            }

            text.Append(entry.Kind).Append('\t')
                .Append(Clean(entry.Title)).Append('\t')
                .Append(Clean(entry.Arguments)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// 書き出す。途中で読まれても壊れた中身を見せないよう、別名で書いてから置き換える。
    /// </summary>
    public static void Write(string directory, IReadOnlyList<ModernMenuEntry> entries, IEnumerable<string> archiveExtensions)
    {
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, FileName);
        string temp = path + ".tmp";

        File.WriteAllText(temp, Serialize(entries, archiveExtensions), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>区切りに使うタブと改行は、表示文字列から取り除く。</summary>
    private static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
