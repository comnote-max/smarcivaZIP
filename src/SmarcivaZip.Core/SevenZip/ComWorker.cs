using System.Collections.Concurrent;

namespace SmarcivaZip.Core.SevenZip;

/// <summary>
/// 7z.dll の COM オブジェクトを扱うための専用スレッド。
///
/// 7z.dll が返すオブジェクトは COM に登録されていない素の C++ オブジェクトで、
/// プロキシ／スタブを持たない。そのため STA スレッド（WPF の UI スレッド）で
/// 生成したものを別スレッドから使おうとすると、CLR がアパートメント間の
/// マーシャリングを試みて QueryInterface が E_NOINTERFACE で落ちる。
///
/// アーカイブを開くのと展開するのを別スレッドでやると、まさにこれを踏む。
/// そこで COM に触る操作はすべてこの 1 本の MTA スレッドに集約する。
/// 生成も利用も破棄も同じスレッドで行われるので、マーシャリングが発生しない。
/// </summary>
public sealed class ComWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public ComWorker()
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "smarcivaZIP archive worker"
        };

        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private void Loop()
    {
        foreach (Action work in _queue.GetConsumingEnumerable())
        {
            work();
        }
    }

    public T Invoke<T>(Func<T> work)
    {
        if (Thread.CurrentThread == _thread) return work();

        T result = default!;
        Exception? failure = null;

        using var done = new ManualResetEventSlim(false);

        _queue.Add(() =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = ex; }
            finally { done.Set(); }
        });

        done.Wait();

        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
        return result;
    }

    public void Invoke(Action work) => Invoke<object?>(() => { work(); return null; });

    public Task<T> InvokeAsync<T>(Func<T> work) => Task.Run(() => Invoke(work));

    public void Dispose()
    {
        _queue.CompleteAdding();

        // キューに残った破棄処理（COM の解放など）を最後まで走らせてから抜ける。
        _thread.Join(TimeSpan.FromSeconds(5));
        _queue.Dispose();
    }
}
