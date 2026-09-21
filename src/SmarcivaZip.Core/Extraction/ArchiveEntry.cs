namespace SmarcivaZip.Core.Extraction;

/// <summary>アーカイブ内の 1 エントリ。7z.dll のインデックスと 1 対 1 に対応する。</summary>
public sealed class ArchiveEntry
{
    public required uint Index { get; init; }

    /// <summary>アーカイブに記録されていた名前（文字コード補正・NFC 正規化を適用済み）。</summary>
    public required string Path { get; set; }

    /// <summary>7z.dll がデコードした生の名前。文字コード補正の前後を比較表示するのに使う。</summary>
    public required string RawPath { get; init; }

    public required bool IsDirectory { get; init; }
    public required ulong Size { get; init; }
    public required ulong PackedSize { get; init; }
    public required bool IsEncrypted { get; init; }
    public DateTime? LastWriteTime { get; init; }
    public DateTime? CreationTime { get; init; }
    public uint? Attributes { get; init; }
    public uint? Crc { get; init; }

    /// <summary>シンボリックリンクのリンク先。リンクでなければ null。</summary>
    public string? SymbolicLinkTarget { get; init; }

    public bool IsSymbolicLink => SymbolicLinkTarget is not null;

    /// <summary>文字コード補正によって名前が変化したか（UI で「修復しました」と示すため）。</summary>
    public bool WasNameRepaired => !string.Equals(Path, RawPath, StringComparison.Ordinal);

    public override string ToString() => Path;
}
