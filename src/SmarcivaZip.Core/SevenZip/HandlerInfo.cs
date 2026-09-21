namespace SmarcivaZip.Core.SevenZip;

/// <summary>
/// 7z.dll が公開している 1 つのアーカイブハンドラ（＝対応フォーマット）の情報。
/// GUID をハードコードせず、DLL から実行時に列挙して構築する。
/// こうしておくと 7-Zip 本体が新形式（zstd など）に対応した時点で
/// smarcivaZIP 側を変更せずに追従できる。
/// </summary>
public sealed class HandlerInfo
{
    public required uint Index { get; init; }
    public required string Name { get; init; }
    public required Guid ClassId { get; init; }

    /// <summary>"zip jar xpi" のように空白区切りで返される拡張子（ドット無し・小文字）。</summary>
    public required IReadOnlyList<string> Extensions { get; init; }

    /// <summary>tar.gz の "tar" のように、展開後に付け替える拡張子。要素が "*" の場合は無し。</summary>
    public required IReadOnlyList<string> AddExtensions { get; init; }

    /// <summary>この形式で新規アーカイブを作れるか（IOutArchive を持つか）。</summary>
    public required bool CanUpdate { get; init; }

    public required IReadOnlyList<byte[]> Signatures { get; init; }
    public required uint SignatureOffset { get; init; }
    public required ArchiveFlags Flags { get; init; }

    public bool MatchesExtension(string extensionWithoutDot)
        => Extensions.Contains(extensionWithoutDot, StringComparer.OrdinalIgnoreCase);

    /// <summary>ファイル先頭のバイト列がこのハンドラのシグネチャと一致するか。</summary>
    public bool MatchesSignature(ReadOnlySpan<byte> header)
    {
        if (Signatures.Count == 0) return false;
        foreach (var signature in Signatures)
        {
            if (signature.Length == 0) continue;
            int offset = (int)SignatureOffset;
            if (header.Length < offset + signature.Length) continue;
            if (header.Slice(offset, signature.Length).SequenceEqual(signature)) return true;
        }
        return false;
    }

    public override string ToString() => $"{Name} [{string.Join(", ", Extensions)}]";
}
