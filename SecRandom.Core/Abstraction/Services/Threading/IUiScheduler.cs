namespace SecRandom.Core.Abstraction.Services.Threading;

/// <summary>派发到 UI 线程时使用的优先级。</summary>
public enum UiPriority
{
    /// <summary>等界面空闲时执行，适合后台刷新。</summary>
    Background = 0,

    /// <summary>普通优先级（默认）。</summary>
    Normal = 1,

    /// <summary>赶在下一次渲染之前执行，适合动画前更新。</summary>
    Render = 2,

    /// <summary>立刻执行，适合必须马上生效的状态回写。</summary>
    Immediate = 3
}

/// <summary>
///     UI 线程调度扩展点：插件不需要自己判 <c>Dispatcher.UIThread.CheckAccess()</c>，
///     也不用到处写 <c>Post</c>——没有桌面壳（手机、无头测试）时统一内联执行。
/// </summary>
public interface IUiScheduler
{
    /// <summary>当前是否已经在 UI 线程上（无壳环境恒为 true）。</summary>
    bool IsOnUiThread { get; }

    /// <summary>投递一个不等待结果的动作。</summary>
    void Post(Action action, UiPriority priority = UiPriority.Normal);

    /// <summary>在 UI 线程上执行并等待完成。</summary>
    Task InvokeAsync(Action action, UiPriority priority = UiPriority.Normal);

    /// <summary>在 UI 线程上执行并等待返回值。</summary>
    Task<T> InvokeAsync<T>(Func<T> func, UiPriority priority = UiPriority.Normal);

    /// <summary>异步等待（<paramref name="delay" /> 小于等于 0 时立即返回）。</summary>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>在 UI 线程上跑一段异步工作（内部 await 仍留在 UI 线程上下文）。</summary>
    Task RunOnUiThreadAsync(Func<Task> work, UiPriority priority = UiPriority.Normal);
}

/// <summary>
///     应用退出前的收尾回调：宿主在 <c>App.StopAsync</c> 里按 <see cref="Priority" /> 升序等待所有实现。
///     <para>
///         插件在 <c>Initialize</c> 里注册 <c>services.AddSingleton&lt;IAppShutdownParticipant, ...&gt;()</c> 即可，
///         用来把状态落盘、关闭网络连接。实现必须自己控制耗时（宿主会限时等待）。
///     </para>
/// </summary>
public interface IAppShutdownParticipant
{
    /// <summary>参与者 id（记日志用）。</summary>
    string Id { get; }

    /// <summary>越小越先执行。</summary>
    int Priority { get; }

    /// <summary>退出前回调。</summary>
    ValueTask OnShuttingDownAsync(CancellationToken cancellationToken = default);
}
