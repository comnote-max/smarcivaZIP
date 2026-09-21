using SmarcivaZip.Core.SevenZip;

namespace SmarcivaZip.Core.Compression;

public enum CompressionLevel
{
    Store = 0,
    Fastest = 1,
    Fast = 3,
    Normal = 5,
    Maximum = 7,
    Ultra = 9
}

/// <summary>
/// 右クリックメニューに並べる圧縮形式。
/// 「まず tar にまとめてから単一ストリーム圧縮」という 2 段構成の形式は
/// TarStageHandler に内側のハンドラ名を持たせて表現する。
/// </summary>
public sealed record OutputFormat(
    string Id,
    string DisplayName,
    string HandlerName,
    string Extension)
{
    /// <summary>暗号化に対応するか。</summary>
    public bool SupportsPassword { get; init; }

    /// <summary>ファイル名まで隠す（ヘッダ暗号化）に対応するか。7z のみ。</summary>
    public bool SupportsHeaderEncryption { get; init; }

    /// <summary>tar にまとめてから圧縮する形式の場合、外側のハンドラ名。</summary>
    public string? TarStageHandler { get; init; }

    public bool IsTwoStage => TarStageHandler is not null;

    /// <summary>この形式に固有の 7-Zip プロパティ。</summary>
    public IReadOnlyList<(string Name, string Value)> ExtraProperties { get; init; } = [];

    public static readonly OutputFormat Zip =
        new("zip", "ZIP", "zip", ".zip") { SupportsPassword = true };

    public static readonly OutputFormat SevenZip =
        new("7z", "7z", "7z", ".7z") { SupportsPassword = true, SupportsHeaderEncryption = true };

    public static readonly OutputFormat Tar =
        new("tar", "TAR", "tar", ".tar");

    public static readonly OutputFormat TarGz =
        new("tar.gz", "TAR.GZ", "tar", ".tar.gz") { TarStageHandler = "gzip" };

    public static readonly OutputFormat TarXz =
        new("tar.xz", "TAR.XZ", "tar", ".tar.xz") { TarStageHandler = "xz" };

    public static readonly OutputFormat TarZstd =
        new("tar.zst", "TAR.ZST", "tar", ".tar.zst") { TarStageHandler = "zstd" };

    public static readonly OutputFormat Gzip =
        new("gz", "GZIP", "gzip", ".gz");

    public static readonly OutputFormat Xz =
        new("xz", "XZ", "xz", ".xz");

    /// <summary>定義済みの全形式。実際に使えるかは 7z.dll の対応状況による。</summary>
    public static readonly IReadOnlyList<OutputFormat> All =
    [
        Zip, SevenZip, TarGz, TarXz, TarZstd, Tar, Gzip, Xz
    ];

    /// <summary>
    /// 読み込んだ 7z.dll が実際に「書き込み可能」として公開している形式だけを返す。
    /// zstd 非対応の 7z.dll を同梱したときに TAR.ZST をメニューに出さないためのもの。
    /// </summary>
    public static IReadOnlyList<OutputFormat> GetAvailable(SevenZipLibrary library)
    {
        var writable = library.Handlers
            .Where(h => h.CanUpdate)
            .Select(h => h.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return All.Where(f => writable.Contains(f.HandlerName)
                              && (f.TarStageHandler is null || writable.Contains(f.TarStageHandler)))
                  .ToList();
    }

    public override string ToString() => DisplayName;
}
