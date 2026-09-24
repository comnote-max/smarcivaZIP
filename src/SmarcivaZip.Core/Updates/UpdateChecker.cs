using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SmarcivaZip.Core.Updates;

/// <summary>新しいバージョンの知らせ。</summary>
public sealed record UpdateInfo(Version Version, string PageUrl);

/// <summary>
/// GitHub の最新リリースを見て、新しいバージョンが出ているか調べる。
///
/// 知らせるだけで、ダウンロードも入れ替えもしない。勝手に実行ファイルを落として動かす仕組みは、
/// 配布元が乗っ取られたときの被害が大きく、セキュリティソフトにも怪しまれる。
/// 送るのは HTTP の要求だけで、利用者やファイルに関する情報は何も送らない。
///
/// ストア版はストアが更新するので、これを使わない（アプリ自身の更新はストアの規約でも禁止）。
/// </summary>
public static class UpdateChecker
{
    public const string ReleasesPage = "https://github.com/comnote-max/smarcivaZIP/releases/latest";

    private const string LatestReleaseApi = "https://api.github.com/repos/comnote-max/smarcivaZIP/releases/latest";

    /// <summary>リリースのページとして開いてよい URL の先頭。これ以外が返ってきたら固定のページを開く。</summary>
    private const string TrustedPagePrefix = "https://github.com/comnote-max/smarcivaZIP/releases/";

    /// <summary>自動確認の間隔。</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(7);

    /// <summary>
    /// 新しいバージョンがあればそれを、無ければ null を返す。
    /// 通信やデータの異常は例外（<see cref="UpdateCheckException"/>）で知らせる。
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(Version current, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = timeout };
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);

        // GitHub の API は User-Agent が無い要求を断る。
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("smarcivaZIP", current.ToString(3)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseLatestRelease(json, current);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new UpdateCheckException(ex.Message, ex);
        }
    }

    /// <summary>
    /// releases/latest の応答を読む。下書きとプレリリースはそもそも latest に出てこないが、
    /// 念のため除く。
    /// </summary>
    public static UpdateInfo? ParseLatestRelease(string json, Version current)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True) return null;
            if (root.TryGetProperty("prerelease", out JsonElement pre) && pre.ValueKind == JsonValueKind.True) return null;

            string? tag = root.TryGetProperty("tag_name", out JsonElement tagElement) ? tagElement.GetString() : null;
            Version? latest = ParseVersion(tag);
            if (latest is null) throw new UpdateCheckException($"Unexpected release tag: {tag}");

            if (Normalize(latest) <= Normalize(current)) return null;

            string? page = root.TryGetProperty("html_url", out JsonElement url) ? url.GetString() : null;
            if (page is null || !page.StartsWith(TrustedPagePrefix, StringComparison.Ordinal)) page = ReleasesPage;

            return new UpdateInfo(Normalize(latest), page);
        }
        catch (JsonException ex)
        {
            throw new UpdateCheckException(ex.Message, ex);
        }
    }

    /// <summary>"v1.2.0" や "1.2" を読む。読めなければ null。</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        string text = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out Version? version) ? version : null;
    }

    /// <summary>自動確認の時期が来ているか。時計が戻っていたら確認する。</summary>
    public static bool IsDue(DateTime? lastCheckUtc, DateTime nowUtc)
        => lastCheckUtc is null || nowUtc - lastCheckUtc.Value >= Interval || lastCheckUtc.Value > nowUtc;

    /// <summary>1.2 と 1.2.0 を同じものとして比べられるよう、3 つの数字にそろえる。</summary>
    private static Version Normalize(Version version)
        => new(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0));
}

public sealed class UpdateCheckException(string message, Exception? inner = null) : Exception(message, inner);
