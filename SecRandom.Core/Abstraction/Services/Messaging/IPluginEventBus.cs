using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Core.Abstraction.Services.Theming;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services.Messaging;

/// <summary>
///     插件间事件总线：宿主发布自己的事件，插件之间也可以用它通信，而不必互相引用。
///     <para>订阅返回的 <see cref="IDisposable" /> 释放即退订；插件应在卸载/退出时释放。</para>
///     <para>回调异常会被吞掉并记日志，不会影响其它订阅者。</para>
/// </summary>
public interface IPluginEventBus
{
    /// <summary>同步订阅。</summary>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;

    /// <summary>异步订阅（按注册顺序 await，见 <see cref="PublishAsync" />）。</summary>
    IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class;

    /// <summary>同步发布（异步订阅者会被 fire-and-forget）。</summary>
    void Publish<TEvent>(TEvent payload) where TEvent : class;

    /// <summary>异步发布并等待异步订阅者完成。</summary>
    ValueTask PublishAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default) where TEvent : class;

    /// <summary>某个事件的订阅者数量（诊断用）。</summary>
    int GetSubscriberCount<TEvent>() where TEvent : class;
}

/// <summary>宿主内置事件。插件订阅这些类型即可，不需要宿主额外注册。</summary>
public static class HostEvents
{
    /// <summary>一次抽签完成（落库之后）。</summary>
    public sealed record DrawCompleted(
        DrawPresentationChannel Channel,
        IReadOnlyList<Student> Students,
        IReadOnlyList<Prize> Prizes,
        string ListName,
        string PrizeListName,
        string RoundId,
        DateTime DrawTime,
        int RequestedCount);

    /// <summary>插件加载完成。</summary>
    public sealed record PluginLoaded(string PluginId, string Name, string Version);

    /// <summary>名单或奖池变化。</summary>
    public sealed record ProfileChanged(string ListName, bool IsPrizeList);

    /// <summary>主题切换。</summary>
    public sealed record ThemeChanged(ThemeKind Theme);

    /// <summary>应用启动完成。</summary>
    public sealed record AppStarted;

    /// <summary>应用准备退出。</summary>
    public sealed record AppStopping;
}
