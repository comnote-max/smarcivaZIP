using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace SmarcivaZip.Core.Shell;

public sealed record CoalescedSelection(bool IsLeader, IReadOnlyList<string> Paths);

/// <summary>
/// エクスプローラーの複数選択をまとめる。
///
/// レジストリだけで作った右クリックメニューは、複数のファイルを選んで実行すると
/// ファイルごとに別プロセスとして起動される。そのまま処理すると
/// 「10 個選んで ZIP 圧縮」したときに ZIP が 10 個できてしまい、明らかに期待と違う。
///
/// COM のシェル拡張を書けば一度の呼び出しで全部受け取れるが、
/// それはエクスプローラーに自前の DLL を読み込ませることを意味する。
/// 代わりに、最初に起動したプロセスが名前付きパイプで待ち受け、
/// 後続のプロセスは自分のパスを送って静かに終了する、という形にした。
/// </summary>
public static class SelectionCoalescer
{
    /// <summary>後続プロセスの到着を待つ時間。エクスプローラーは連続して起動してくる。</summary>
    private static readonly TimeSpan CollectionWindow = TimeSpan.FromMilliseconds(600);

    /// <summary>後続プロセスが先頭プロセスのパイプを待つ時間。</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// 同じ操作としてまとめてよい呼び出しを集める。
    ///
    /// 戻り値の IsLeader が false のとき、この呼び出しは別プロセスに引き継がれたので
    /// 何もせずに終了してよい。
    /// </summary>
    public static async Task<CoalescedSelection> CollectAsync(
        string operationKey, string firstPath, CancellationToken cancellationToken = default)
    {
        string id = BuildIdentifier(operationKey, firstPath);
        string pipeName = $"smarcivazip-{id}";
        string mutexName = $"Local\\smarcivazip-leader-{id}";

        using var leaderMutex = new Mutex(initiallyOwned: false, mutexName, out _);

        bool isLeader;
        try
        {
            isLeader = leaderMutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // 前回の先頭プロセスが異常終了していた。自分が引き継ぐ。
            isLeader = true;
        }

        if (!isLeader)
        {
            bool handedOff = await TrySendAsync(pipeName, firstPath, cancellationToken);

            // 送れなかった場合、先頭プロセスは既に締め切った後。単独で処理する。
            return new CoalescedSelection(!handedOff, [firstPath]);
        }

        try
        {
            IReadOnlyList<string> paths = await CollectAsLeaderAsync(pipeName, firstPath, cancellationToken);
            return new CoalescedSelection(true, paths);
        }
        finally
        {
            leaderMutex.ReleaseMutex();
        }
    }

    private static async Task<IReadOnlyList<string>> CollectAsLeaderAsync(
        string pipeName, string firstPath, CancellationToken cancellationToken)
    {
        var paths = new List<string> { firstPath };
        DateTime deadline = DateTime.UtcNow + CollectionWindow;

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(
                pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(deadline - DateTime.UtcNow);

            try
            {
                await server.WaitForConnectionAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }

            using var reader = new StreamReader(server, Encoding.UTF8);
            string? received = await reader.ReadToEndAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(received))
            {
                foreach (string line in received.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    string path = line.Trim();
                    if (path.Length > 0 && !paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                        paths.Add(path);
                }

                // 1 つ届いたということは続きが来る可能性が高いので、少しだけ待ち直す。
                deadline = DateTime.UtcNow + CollectionWindow;
            }
        }

        // エクスプローラーが渡してくる順序は選択順で安定しないため、名前順に整える。
        paths.Sort(StringComparer.OrdinalIgnoreCase);
        return paths;
    }

    private static async Task<bool> TrySendAsync(
        string pipeName, string path, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await client.ConnectAsync((int)ConnectTimeout.TotalMilliseconds, cancellationToken);

            await using var writer = new StreamWriter(client, Encoding.UTF8);
            await writer.WriteAsync(path);
            await writer.FlushAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException
                                      or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 同じ操作・同じフォルダの呼び出しだけをまとめるための識別子。
    /// パイプ名に使えるよう、ハッシュにして記号を落とす。
    /// </summary>
    private static string BuildIdentifier(string operationKey, string firstPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(firstPath)) ?? string.Empty;
        string material = $"{operationKey}|{directory.ToLowerInvariant()}";

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
