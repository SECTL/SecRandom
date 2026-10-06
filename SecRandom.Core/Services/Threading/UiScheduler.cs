using Avalonia;
using Avalonia.Threading;
using SecRandom.Core.Abstraction.Services.Threading;

namespace SecRandom.Core.Services.Threading;

/// <summary>
///     <see cref="IUiScheduler" /> 的默认实现：把插件里的 UI 操作统一收到宿主 Dispatcher 上。
///     <para>
///         没有桌面壳（手机、无头测试）时 <see cref="IsOnUiThread" /> 恒为 true，
///         所有调用内联执行，不会抛"没有 Dispatcher"。
///     </para>
/// </summary>
public sealed class UiScheduler : IUiScheduler
{
    /// <inheritdoc />
    public bool IsOnUiThread => Application.Current is null || Dispatcher.UIThread.CheckAccess();

    /// <inheritdoc />
    public void Post(Action action, UiPriority priority = UiPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Application.Current is null)
        {
            // 无壳环境没有 Dispatcher：直接执行，避免插件等在永远不会到来的回调上。
            action();
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action, Map(priority));
    }

    /// <inheritdoc />
    public Task InvokeAsync(Action action, UiPriority priority = UiPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsOnUiThread)
            return RunInline(action);

        return Dispatcher.UIThread.InvokeAsync(action, Map(priority)).GetTask();
    }

    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<T> func, UiPriority priority = UiPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(func);

        if (IsOnUiThread)
            return RunInline(func);

        return Dispatcher.UIThread.InvokeAsync(func, Map(priority)).GetTask();
    }

    /// <inheritdoc />
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken);

    /// <inheritdoc />
    public async Task RunOnUiThreadAsync(Func<Task> work, UiPriority priority = UiPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (IsOnUiThread)
        {
            await work().ConfigureAwait(true);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(work, Map(priority)).ConfigureAwait(false);
    }

    private static DispatcherPriority Map(UiPriority priority) => priority switch
    {
        UiPriority.Background => DispatcherPriority.Background,
        UiPriority.Render => DispatcherPriority.Render,
        UiPriority.Immediate => DispatcherPriority.Send,
        _ => DispatcherPriority.Normal
    };

    private static Task RunInline(Action action)
    {
        try
        {
            action();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    private static Task<T> RunInline<T>(Func<T> func)
    {
        try
        {
            return Task.FromResult(func());
        }
        catch (Exception ex)
        {
            return Task.FromException<T>(ex);
        }
    }
}
