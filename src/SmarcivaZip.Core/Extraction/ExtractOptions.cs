namespace SmarcivaZip.Core.Extraction;

public enum OutputFolderMode
{
    /// <summary>ルート直下が 1 つのフォルダならそのまま、複数ならフォルダを作る（既定）。</summary>
    Auto,

    /// <summary>常にアーカイブ名のフォルダを作る。</summary>
    AlwaysCreate,

    /// <summary>フォルダを作らず展開先に直接展開する。</summary>
    Never
}

public enum OverwritePolicy
{
    /// <summary>"名前 (2).txt" のように別名で保存する（既定・最も安全）。</summary>
    Rename,

    Overwrite,

    Skip,

    /// <summary>呼び出し側に問い合わせる。</summary>
    Ask
}

public sealed class ExtractOptions
{
    /// <summary>展開先の親フォルダ。null ならアーカイブと同じ場所。</summary>
    public string? OutputDirectory { get; set; }

    public OutputFolderMode FolderMode { get; set; } = OutputFolderMode.Auto;

    public OverwritePolicy OverwritePolicy { get; set; } = OverwritePolicy.Rename;

    /// <summary>__MACOSX / ._* / .DS_Store を展開しない。</summary>
    public bool ExcludeMacMetadata { get; set; } = true;

    /// <summary>Thumbs.db / desktop.ini を展開しない。</summary>
    public bool ExcludeWindowsMetadata { get; set; } = true;

    public bool PreserveTimestamps { get; set; } = true;

    /// <summary>
    /// シンボリックリンクを展開する。既定では無効。
    /// リンク先を経由して展開先の外に書き込まれる危険があるため。
    /// </summary>
    public bool AllowSymbolicLinks { get; set; }

    /// <summary>
    /// アーカイブ本体に付いている Mark of the Web を展開後のファイルに引き継ぐ。
    /// インターネットから落とした書庫の中身を Windows に「信用できない」と
    /// 認識させ続けるために既定で有効。
    /// </summary>
    public bool PropagateMarkOfTheWeb { get; set; } = true;

    /// <summary>展開後に出力フォルダをエクスプローラーで開く。</summary>
    public bool OpenFolderAfterExtract { get; set; } = true;

    /// <summary>tar.gz などを 2 段階まとめて展開し、中間の .tar を残さない。</summary>
    public bool UnwrapNestedTar { get; set; } = true;

    /// <summary>展開に成功したらアーカイブをごみ箱へ送る。</summary>
    public bool DeleteArchiveAfterExtract { get; set; }

    public string? Password { get; set; }
}
