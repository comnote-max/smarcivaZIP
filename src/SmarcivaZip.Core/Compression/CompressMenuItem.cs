using SmarcivaZip.Core.Localization;

namespace SmarcivaZip.Core.Compression;

/// <summary>
/// 右クリックメニューに並べる圧縮の項目ひとつ分。
///
/// 形式そのもの（<see cref="OutputFormat"/>）とは分けている。
/// 「ZIP」と「ZIP（パスワード）」はメニュー上は別項目として出し入れしたいが、
/// 圧縮処理としては同じ形式だからである。
/// </summary>
public sealed record CompressMenuItem(OutputFormat Format, bool WithPassword)
{
    /// <summary>設定ファイルに保存する識別子。</summary>
    public string Id => WithPassword ? Format.Id + "-password" : Format.Id;

    public string Label => Strings.Format(
        WithPassword ? "Menu_CompressToPassword" : "Menu_CompressTo", Format.DisplayName);

    /// <summary>右クリックメニューのコマンドに渡す引数。</summary>
    public string Arguments => WithPassword
        ? $"--compress {Format.Id} --password"
        : $"--compress {Format.Id}";

    /// <summary>
    /// 指定した形式から、メニューに出せる項目をすべて組み立てる。
    /// パスワードに対応していない形式では暗号化の項目を作らない。
    /// </summary>
    public static List<CompressMenuItem> BuildAll(IEnumerable<OutputFormat> formats)
    {
        var items = new List<CompressMenuItem>();

        foreach (OutputFormat format in formats)
        {
            items.Add(new CompressMenuItem(format, WithPassword: false));
            if (format.SupportsPassword) items.Add(new CompressMenuItem(format, WithPassword: true));
        }

        return items;
    }

    /// <summary>設定に保存された識別子の並びから、実際のメニュー項目を復元する。</summary>
    public static List<CompressMenuItem> Resolve(
        IEnumerable<OutputFormat> availableFormats, IEnumerable<string> selectedIds)
    {
        Dictionary<string, CompressMenuItem> byId = BuildAll(availableFormats)
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        var resolved = new List<CompressMenuItem>();

        // 設定に書かれている順番をそのままメニューの並び順にする。
        foreach (string id in selectedIds)
        {
            if (byId.TryGetValue(id, out CompressMenuItem? item) && !resolved.Contains(item))
                resolved.Add(item);
        }

        return resolved;
    }

    public override string ToString() => Label;
}
