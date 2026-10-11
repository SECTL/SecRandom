using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services.Messaging;

namespace SecRandom.Core.Services.Messaging;

/// <summary>
///     插件事件总线的默认实现：按事件类型分桶，订阅异常不会影响其它订阅者。
/// </summary>
public sealed class PluginEventBus(ILogger<PluginEventBus>? logger = null) : IPluginEventBus
{
    private readonly ConcurrentDictionary<Type, List<SyncSubscription>> _syncSubscriptions = new();
    private readonly ConcurrentDictionary<Type, List<AsyncSubscription>> _asyncSubscriptions = new();

    /// <inheritdoc />
    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new SyncSubscription(handler);

        lock (GetBucket(_syncSubscriptions, typeof(TEvent)))
        {
            GetBucket(_syncSubscriptions, typeof(TEvent)).Add(subscription);
        }

        return new Unsubscriber(() =>
        {
            lock (GetBucket(_syncSubscriptions, typeof(TEvent)))
            {
                GetBucket(_syncSubscriptions, typeof(TEvent)).Remove(subscription);
            }
        });
    }

    /// <inheritdoc />
    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new AsyncSubscription(handler);

        lock (GetBucket(_asyncSubscriptions, typeof(TEvent)))
        {
            GetBucket(_asyncSubscriptions, typeof(TEvent)).Add(subscription);
        }

        return new Unsubscriber(() =>
        {
            lock (GetBucket(_asyncSubscriptions, typeof(TEvent)))
            {
                GetBucket(_asyncSubscriptions, typeof(TEvent)).Remove(subscription);
            }
        });
    }

    /// <inheritdoc />
    public void Publish<TEvent>(TEvent payload) where TEvent : class
    {
        if (payload is null)
            return;

        foreach (var subscription in Snapshot(GetBucket(_syncSubscriptions, typeof(TEvent))))
        {
            try
            {
                ((Action<TEvent>)subscription.Handler)(payload);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "插件事件 {EventType} 的订阅者抛异常，已跳过。", typeof(TEvent).Name);
            }
        }

        foreach (var subscription in Snapshot(GetBucket(_asyncSubscriptions, typeof(TEvent))))
        {
            // 同步发布不等异步订阅者：异常同样只记日志。
            _ = InvokeAsyncSafelyAsync((Func<TEvent, CancellationToken, Task>)subscription.Handler, payload);
        }
    }

    /// <inheritdoc />
    public async ValueTask PublishAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        if (payload is null)
            return;

        Publish(payload);

        foreach (var subscription in Snapshot(GetBucket(_asyncSubscriptions, typeof(TEvent))))
        {
            await InvokeAsyncSafelyAsync((Func<TEvent, CancellationToken, Task>)subscription.Handler, payload, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public int GetSubscriberCount<TEvent>() where TEvent : class =>
        Snapshot(GetBucket(_syncSubscriptions, typeof(TEvent))).Count +
        Snapshot(GetBucket(_asyncSubscriptions, typeof(TEvent))).Count;

    private async Task InvokeAsyncSafelyAsync<TEvent>(
        Func<TEvent, CancellationToken, Task> handler,
        TEvent payload,
        CancellationToken cancellationToken = default) where TEvent : class
    {
        try
        {
            await handler(payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "插件事件 {EventType} 的异步订阅者抛异常，已跳过。", typeof(TEvent).Name);
        }
    }

    private static List<SyncSubscription> GetBucket(
        ConcurrentDictionary<Type, List<SyncSubscription>> buckets,
        Type eventType) =>
        buckets.GetOrAdd(eventType, static _ => []);

    private static List<AsyncSubscription> GetBucket(
        ConcurrentDictionary<Type, List<AsyncSubscription>> buckets,
        Type eventType) =>
        buckets.GetOrAdd(eventType, static _ => []);

    private static List<SyncSubscription> Snapshot(List<SyncSubscription> bucket)
    {
        lock (bucket)
        {
            return [.. bucket];
        }
    }

    private static List<AsyncSubscription> Snapshot(List<AsyncSubscription> bucket)
    {
        lock (bucket)
        {
            return [.. bucket];
        }
    }

    private sealed record SyncSubscription(Delegate Handler);

    private sealed record AsyncSubscription(Delegate Handler);

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
