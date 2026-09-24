using SmarcivaZip.Core.Localization;

namespace SmarcivaZip.Core.Shell;

/// <summary>
/// エクスプローラー上でひとまとまりに扱う書庫の種類。
///
/// アイコンは ProgID 単位でしか設定できないため、形式ごとに違うアイコンを
/// 出すには ProgID も分ける必要がある。ここがその分け方。
///
/// 形式ひとつに 1 種類ずつ用意するのではなく、まとめられるものはまとめている。
/// .tgz と .tar.gz に別のアイコンを与えても利用者には区別が付かないし、
/// 一覧に並んだときに色が増えすぎて、かえって読み取りにくくなる。
/// </summary>
public sealed record ArchiveFileType(
    string Id, string IconFileName, string DisplayNameKey, IReadOnlyList<string> Extensions)
{
    /// <summary>レジストリに書く ProgID。</summary>
    public string ProgId => "smarcivaZIP." + Id;

    /// <summary>「ZIP 書庫」など、エクスプローラーの種類欄に出る名前。</summary>
    public string DisplayName => Strings.Get(DisplayNameKey);

    /// <summary>種類欄とプロパティに出る、アプリ名まで含んだ名前。</summary>
    public string FriendlyName => Strings.Format("FileType_Friendly", DisplayName);

    /// <summary>
    /// どの分類にも当てはまらない拡張子の受け皿。
    /// iso や cab のように、書庫として開けはするが独自の見た目を用意するほどでもないもの。
    /// よく使うものは明示しておき、それ以外もここへ落ちる。
    /// </summary>
    public static readonly ArchiveFileType Fallback = new("Archive", "archive.ico", "FileType_Archive",
        ["cab", "arj", "iso", "dmg", "msi", "wim", "cpio", "rpm", "deb", "xar", "chm"]);

    public static readonly IReadOnlyList<ArchiveFileType> All =
    [
        new("Zip", "zip.ico", "FileType_Zip", ["zip", "zipx", "jar"]),
        new("SevenZip", "7z.ico", "FileType_SevenZip", ["7z"]),
        new("Rar", "rar.ico", "FileType_Rar", ["rar", "r00"]),
        new("Tar", "tar.ico", "FileType_Tar", ["tar", "tgz", "tbz", "tbz2", "txz", "tzst", "taz"]),
        new("Compressed", "gz.ico", "FileType_Compressed", ["gz", "bz2", "xz", "zst", "lzma", "lz4", "z"]),
        new("Lzh", "lzh.ico", "FileType_Lzh", ["lzh", "lha"]),
        Fallback
    ];

    /// <summary>拡張子（ドット無し）から分類を引く。該当が無ければ受け皿を返す。</summary>
    public static ArchiveFileType ForExtension(string extension)
    {
        string normalized = extension.Trim().TrimStart('.').ToLowerInvariant();

        return All.FirstOrDefault(
            type => type.Extensions.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            ?? Fallback;
    }

    /// <summary>いずれかの分類（受け皿を含む）に明示されている拡張子か。</summary>
    public static bool IsKnownExtension(string extension)
    {
        string normalized = extension.Trim().TrimStart('.');
        return All.Any(type => type.Extensions.Contains(normalized, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 指定した拡張子を扱うために、実際に登録が要る分類だけを返す。
    /// 使わない ProgID まで作ると、解除し忘れたときにレジストリに残り続ける。
    /// </summary>
    public static IReadOnlyList<ArchiveFileType> UsedBy(IEnumerable<string> extensions)
    {
        var used = new List<ArchiveFileType>();

        foreach (string extension in extensions)
        {
            ArchiveFileType type = ForExtension(extension);
            if (!used.Contains(type)) used.Add(type);
        }

        return used;
    }

    public override string ToString() => ProgId;
}
