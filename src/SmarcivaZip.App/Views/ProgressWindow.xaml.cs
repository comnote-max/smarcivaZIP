using System.ComponentModel;
using System.Windows;

namespace SmarcivaZip.App.Views;

/// <summary>
/// 進捗ダイアログ。Lhaplus と同じく、通常の操作ではこの 1 枚しか出さない。
///
/// 実処理はバックグラウンドスレッドで走らせ、進捗だけを UI に送る。
/// 進捗は 7z.dll から高頻度で飛んでくるので、そのまま Dispatcher に流すと
/// UI が詰まる。ここで間引いてから描画する。
/// </summary>
public partial class ProgressWindow : Window
{
    private readonly CancellationTokenSource _cancellation = new();
    private DateTime _lastRender = DateTime.MinValue;
    private bool _finished;

    /// <summary>描画の最小間隔。これ以上細かく更新しても人間には見えない。</summary>
    private static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(60);

    public ProgressWindow(string heading)
    {
        InitializeComponent();
        HeadingText.Text = heading;
    }

    public Exception? Error { get; private set; }
    public bool WasCancelled => _cancellation.IsCancellationRequested;

    /// <summary>
    /// 処理を開始する。完了したらウィンドウを閉じる。
    /// 呼び出し側は続けて ShowDialog() すればよい。
    /// </summary>
    public void Run(Func<IProgress<(string Text, double Fraction)>, CancellationToken, Task> work)
    {
        var progress = new Progress<(string Text, double Fraction)>(OnProgress);

        _ = Task.Run(async () =>
        {
            try
            {
                await work(progress, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // キャンセルは異常ではない。
            }
            catch (Exception ex)
            {
                Error = ex;
            }
            finally
            {
                Dispatcher.Invoke(Finish);
            }
        });
    }

    private void OnProgress((string Text, double Fraction) value)
    {
        // 完了間際の更新は必ず描く。それ以外は間引く。
        DateTime now = DateTime.UtcNow;
        if (now - _lastRender < RenderInterval && value.Fraction < 1.0) return;
        _lastRender = now;

        DetailText.Text = string.IsNullOrEmpty(value.Text) ? "処理中..." : value.Text;
        Bar.Value = value.Fraction;
        PercentText.Text = $"{value.Fraction * 100:0}%";
    }

    private void Finish()
    {
        _finished = true;
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        DetailText.Text = "中止しています...";
        _cancellation.Cancel();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 処理中に閉じられたらキャンセル扱いにし、後始末が終わるまで閉じない。
        if (!_finished)
        {
            e.Cancel = true;
            _cancellation.Cancel();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _cancellation.Dispose();
        base.OnClosed(e);
    }
}
