using System;
using System.Threading;
using Avalonia.Threading;

namespace SecRandom.Helpers;

/// <summary>
/// 把 <see cref="System.IO.FileSystemWatcher"/> 的连续变更事件合并成一次回调。
/// <para>
/// 文件监视器对一次保存通常连发多条事件（写入 + 属性更新 + 临时文件改名），
/// 而在回调里重建下拉项、重算过滤条件和计数相当昂贵，落在主线程上就是可见的卡顿。
/// 这里在最后一次事件之后静置 <c>delay</c> 才投递回调，且用「重新计时」的方式保证
/// 一批事件只触发一次刷新。
/// </para>
/// </summary>
internal sealed class FileWatcherDebouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Action _callback;
    private readonly object _gate = new();
    private readonly Timer _timer;
    private bool _disposed;

    public FileWatcherDebouncer(TimeSpan delay, Action callback)
    {
        _delay = delay;
        _callback = callback;
        _timer = new Timer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>记录一次变更，重置静置计时。</summary>
    public void Notify()
    {
        if (_disposed)
            return;

        lock (_gate)
        {
            if (_disposed)
                return;

            try
            {
                _timer.Change(_delay, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // 释放与通知竞争，忽略。
            }
        }
    }

    private void OnTimer(object? state)
    {
        if (_disposed)
            return;

        // 回到 UI 线程再刷新，调用方的刷新逻辑依赖绑定与集合通知。
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
                _callback();
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _timer.Dispose();
        }
    }
}
