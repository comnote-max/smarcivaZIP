using System.Diagnostics;
using SmarcivaZip.Core.Shell;
using Xunit;

namespace SmarcivaZip.Tests;

/// <summary>
/// エクスプローラーの複数選択をまとめる仕組み。
///
/// レジストリだけで作った右クリックメニューは、ファイルごとに別プロセスとして
/// 起動される。束ねに失敗すると、10 個選んで圧縮したときに書庫が 10 個できる。
///
/// 実測では 60 ファイルの選択でプロセスの到着が 2.4 秒に散らばったが、
/// 締め切りは 1 件届くたびに延びるので全部ひとつにまとまった。
/// 「間隔が締め切り未満なら何個でも拾う」というこの性質をここで固定する。
/// </summary>
public class SelectionCoalescerTests
{
    /// <summary>テストごとに別の鍵を使う。同時に走っても互いに巻き込まない。</summary>
    private static string NewKey() => "test-" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// 呼び出しひとつにつきスレッドひとつを用意する。
    ///
    /// 本番ではファイルごとに別プロセスが起動するので、待ち手はそれぞれ自分のスレッドを持つ。
    /// このテストはそれを 1 プロセス内のタスクで代用しているが、
    /// NamedPipeClientStream.ConnectAsync は接続できるまでスレッドプールのスレッドを
    /// 1 本占有し続ける。コアの少ない CI ではプールのスレッドが待ち手に食い尽くされ、
    /// まとめ役の続きが回ってこないまま待ち手が時間切れになり、全員が「自分が先頭」になる
    /// （DOTNET_PROCESSOR_COUNT=1 で 20 回中 20 回再現した）。
    /// 本番には無い取り合いなので、テストの側で別プロセスと同じ条件にそろえる。
    /// </summary>
    private static IDisposable OneThreadPerCaller(int callers)
    {
        ThreadPool.GetMinThreads(out int worker, out int io);
        ThreadPool.SetMinThreads(Math.Max(worker, callers + 4), Math.Max(io, callers + 4));
        return new RestoreMinThreads(worker, io);
    }

    private sealed class RestoreMinThreads(int worker, int io) : IDisposable
    {
        public void Dispose() => ThreadPool.SetMinThreads(worker, io);
    }

    private static string PathIn(string directory, string name) => Path.Combine(directory, name);

    [Fact]
    public async Task 単独の呼び出しはそのまま先頭になる()
    {
        CoalescedSelection result = await SelectionCoalescer.CollectAsync(
            NewKey(), PathIn(@"C:\work", "a.txt"));

        Assert.True(result.IsLeader);
        Assert.Equal([PathIn(@"C:\work", "a.txt")], result.Paths);
    }

    [Fact]
    public async Task 同時に来た呼び出しがひとつにまとまる()
    {
        using IDisposable _ = OneThreadPerCaller(8);
        string key = NewKey();
        string directory = @"C:\work";

        var tasks = Enumerable.Range(1, 8)
            .Select(i => SelectionCoalescer.CollectAsync(key, PathIn(directory, $"file{i}.txt")))
            .ToArray();

        CoalescedSelection[] results = await Task.WhenAll(tasks);

        CoalescedSelection[] leaders = results.Where(r => r.IsLeader).ToArray();

        Assert.Single(leaders);
        Assert.Equal(8, leaders[0].Paths.Count);

        // 先頭以外は何もせずに終わる。ここが空でないと書庫が余分にできる。
        Assert.All(results.Where(r => !r.IsLeader), r => Assert.Empty(r.Paths.Where(_ => false)));
    }

    [Fact]
    public async Task 到着が間延びしても締め切りが延びる()
    {
        // 1 件ごとに 300 ms 空けても、締め切り (600 ms) 未満なので拾われ続ける。
        // 合計の経過時間は最初の締め切りを大きく超える。
        using IDisposable _ = OneThreadPerCaller(5);
        string key = NewKey();
        string directory = @"C:\work";

        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task<CoalescedSelection>>();

        for (int i = 1; i <= 5; i++)
        {
            tasks.Add(SelectionCoalescer.CollectAsync(key, PathIn(directory, $"late{i}.txt")));
            await Task.Delay(300);
        }

        CoalescedSelection[] results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        CoalescedSelection leader = Assert.Single(results.Where(r => r.IsLeader));

        Assert.Equal(5, leader.Paths.Count);
        Assert.True(stopwatch.ElapsedMilliseconds > 600,
            $"締め切りが延びていない（{stopwatch.ElapsedMilliseconds} ms）");
    }

    [Fact]
    public async Task 操作が違えば別々にまとまる()
    {
        // ZIP 圧縮と 7z 圧縮を同時に選んだとき、混ざってはいけない。
        using IDisposable _ = OneThreadPerCaller(3);
        string directory = @"C:\work";

        Task<CoalescedSelection> zip1 = SelectionCoalescer.CollectAsync("op-zip", PathIn(directory, "a.txt"));
        Task<CoalescedSelection> zip2 = SelectionCoalescer.CollectAsync("op-zip", PathIn(directory, "b.txt"));
        Task<CoalescedSelection> sevenZip = SelectionCoalescer.CollectAsync("op-7z", PathIn(directory, "c.txt"));

        CoalescedSelection[] zipResults = await Task.WhenAll(zip1, zip2);
        CoalescedSelection sevenZipResult = await sevenZip;

        CoalescedSelection zipLeader = Assert.Single(zipResults.Where(r => r.IsLeader));

        Assert.Equal(2, zipLeader.Paths.Count);
        Assert.True(sevenZipResult.IsLeader);
        Assert.Single(sevenZipResult.Paths);
    }

    [Fact]
    public async Task フォルダが違えば別々にまとまる()
    {
        // エクスプローラーの選択はひとつのフォルダ内で完結する。
        // 別フォルダからの呼び出しを巻き込むと、意図しない書庫ができる。
        string key = NewKey();

        Task<CoalescedSelection> first = SelectionCoalescer.CollectAsync(key, @"C:\one\a.txt");
        Task<CoalescedSelection> second = SelectionCoalescer.CollectAsync(key, @"C:\two\b.txt");

        CoalescedSelection[] results = await Task.WhenAll(first, second);

        Assert.All(results, r => Assert.True(r.IsLeader));
        Assert.All(results, r => Assert.Single(r.Paths));
    }

    [Fact]
    public async Task まとめた結果は名前順に並ぶ()
    {
        // エクスプローラーが渡す順序は選択順で安定しないため、書庫の中身が
        // 実行のたびに入れ替わらないよう並べ直している。
        using IDisposable _ = OneThreadPerCaller(3);
        string key = NewKey();
        string directory = @"C:\work";

        var tasks = new[] { "c.txt", "a.txt", "b.txt" }
            .Select(name => SelectionCoalescer.CollectAsync(key, PathIn(directory, name)))
            .ToArray();

        CoalescedSelection[] results = await Task.WhenAll(tasks);
        CoalescedSelection leader = Assert.Single(results.Where(r => r.IsLeader));

        Assert.Equal(
            [PathIn(directory, "a.txt"), PathIn(directory, "b.txt"), PathIn(directory, "c.txt")],
            leader.Paths);
    }
}
