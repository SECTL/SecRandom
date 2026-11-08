using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Views.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     集控锁屏的生命周期：建窗、解除与"本机收回远控权/控制台解除锁定"的自动释放。
/// </summary>
/// <remarks>
///     <para>
///         <b>锁屏必须能被设备自己解除</b>：<c>draw_locked</c> 变成 <see langword="false" />、
///         或者老师关掉本机集控开关，都应该让屏幕立刻恢复——否则一条命令就能把一台教室机永久锁死。
///     </para>
///     <para>
///         这里只订阅状态变化，不订阅连接状态：断网时控制台解不了锁，但老师仍然可以用长按出口。
///     </para>
/// </remarks>
public sealed class ControlLockScreenService : IDisposable
{
    private readonly IControlNodeStateStore _stateStore;
    private readonly ILogger<ControlLockScreenService> _logger;
    private ControlLockScreenWindow? _window;

    public ControlLockScreenService(
        IControlNodeStateStore stateStore,
        ILogger<ControlLockScreenService> logger)
    {
        _stateStore = stateStore;
        _logger = logger;
        _stateStore.Changed += OnStateChanged;
    }

    public bool IsLocked => _window is { IsVisible: true };

    /// <summary>锁住屏幕。重复的锁屏命令只替换文案与倒计时，不会叠出第二个窗口。</summary>
    public async Task LockAsync(ControlLockScreenRequest request, CancellationToken cancellationToken = default)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            if (_window is null)
            {
                _window = new ControlLockScreenWindow();
                _window.Closed += (_, _) => _window = null;
                _window.Show();
            }

            _window.Configure(request.Message, request.UnlockAfterSeconds);
        }).GetTask().ConfigureAwait(false);

        _logger.LogInformation(
            "集控锁屏已启用（自动解除：{UnlockAfter}）。",
            request.UnlockAfterSeconds is { } seconds ? $"{seconds} 秒" : "否");
    }

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

    private void OnStateChanged(object? sender, ControlNodeState state)
    {
        if (!state.RemoteControlEnabled || !state.DrawLocked)
        {
            if (IsLocked)
                _logger.LogInformation("本机已解除锁屏（远控开关关闭或控制台解除了抽取锁定）。");
            Dismiss();
        }
    }

    public void Dispose()
    {
        _stateStore.Changed -= OnStateChanged;
        Dismiss();
    }
}