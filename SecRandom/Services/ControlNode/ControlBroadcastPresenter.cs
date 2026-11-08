using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Views.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     集控广播的窗口管理：同一时刻最多一个广播窗，后来的广播替换前一条。
/// </summary>
/// <remarks>
///     替换而不是叠加：两条广播同时全屏显示会互相遮挡，而且管理员只想让教室看到"最新那条"。
///     窗口的创建/关闭一律在 UI 线程；调用方（命令执行线程池）不需要自己切线程。
/// </remarks>
public sealed class ControlBroadcastPresenter(ILogger<ControlBroadcastPresenter> logger)
{
    private ControlBroadcastWindow? _window;

    /// <summary>显示一条广播；返回时窗口已在 UI 线程上创建并开始倒计时。</summary>
    public async Task ShowAsync(string text, int seconds, CancellationToken cancellationToken = default)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            if (_window is null)
            {
                _window = new ControlBroadcastWindow();
                _window.Closed += (_, _) => _window = null;
                _window.Show();
            }

            _window.Configure(text, seconds);
            if (!_window.IsVisible)
                _window.Show();
        }).GetTask().ConfigureAwait(false);

        logger.LogInformation("集控广播已显示（{Length} 字，{Seconds} 秒）。", text.Length, seconds);
    }

    /// <summary>关闭当前广播（本机收回远控权时调用）。</summary>
    public void Dismiss()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _window?.Close();
            _window = null;
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _window?.Close();
            _window = null;
        });
    }
}