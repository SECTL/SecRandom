using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services.Views;
using SecRandom.Views;

namespace SecRandom.Services.Plugins;

/// <summary>
///     <see cref="IWindowService" /> 的宿主实现：用 Avalonia <see cref="Window" /> 承载插件内容，
///     支持非模态开窗、带返回值的模态对话框、激活与关闭。
///     <para>
///         同一个 id 再次 <see cref="Show" /> / <see cref="ShowDialogAsync" /> 只会激活已有窗口，不会开第二个。
///         对话框结果由插件调用 <see cref="Close" /> 回传；用户直接关窗则结果为 null。
///         所有窗口操作都封送到 UI 线程，窗口表用 <see cref="ConcurrentDictionary{TKey,TValue}" /> 保护。
///     </para>
///     <para>
///         无活动 TopLevel（无头/移动端）时 <see cref="IsSupported" /> 为 false，
///         此时 <see cref="Show" /> 仍然返回生成的 id 但不会真的开窗，<see cref="OpenWindowIds" /> 也不包含它。
///     </para>
/// </summary>
public sealed class PluginWindowService : IWindowService
{
    private readonly ConcurrentDictionary<string, WindowEntry> _windows = new(StringComparer.Ordinal);
    private readonly ILogger<PluginWindowService>? _logger;

    /// <summary>
    ///     初始化 <see cref="PluginWindowService" />。
    /// </summary>
    /// <param name="logger">日志服务；宿主已 <c>AddLogging</c>，因此 DI 可直接解析，省略时为 null。</param>
    public PluginWindowService(ILogger<PluginWindowService>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<WindowClosedEventArgs>? WindowClosed;

    /// <inheritdoc />
    public IReadOnlyList<string> OpenWindowIds => _windows.Keys.ToList();

    /// <summary>存在活动 TopLevel（主壳或宿主根窗口）时才支持插件开窗。</summary>
    public bool IsSupported
    {
        get
        {
            try
            {
                if (MainView.Current is not null)
                    return true;

                return Application.Current is App app && app.GetRootWindow() is not null;
            }
            catch (Exception)
            {
                // GetRootWindow 在没有活动窗口时抛 InvalidOperationException；某些平台下探测也可能失败，
                // 这里统一按“不支持开窗”降级。
                return false;
            }
        }
    }

    /// <inheritdoc />
    public string Show(WindowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = ResolveId(request.Id);

        if (!IsSupported)
        {
            _logger?.LogWarning("宿主没有活动 TopLevel，插件窗口 {WindowId} 未真正打开。", id);
            return id;
        }

        if (_windows.ContainsKey(id))
        {
            Activate(id);
            return id;
        }

        RunOnUiThread(() => ShowCore(request, id));
        return id;
    }

    /// <inheritdoc />
    public async Task<object?> ShowDialogAsync(WindowRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = ResolveId(request.Id);

        if (cancellationToken.IsCancellationRequested)
            return null;

        if (!IsSupported)
        {
            _logger?.LogWarning("宿主没有活动 TopLevel，插件对话框 {WindowId} 未真正打开。", id);
            return null;
        }

        var entry = await RunOnUiThreadAsync(() => OpenDialogCore(request, id)).ConfigureAwait(false);
        if (entry is null)
            return null;

        using var registration = cancellationToken.Register(
            static state =>
            {
                var (target, service) = ((WindowEntry, PluginWindowService))state!;
                target.ClosedByUser = false;
                target.ResultProvided = true;
                target.Result = null;
                service.RequestClose(target);
            },
            (entry, this));

        try
        {
            return await entry.Completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool Activate(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        if (!_windows.TryGetValue(id, out var entry))
            return false;

        RunOnUiThread(() =>
        {
            try
            {
                entry.Window.Activate();
                if (entry.Window.WindowState == WindowState.Minimized)
                    entry.Window.WindowState = WindowState.Normal;
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "激活插件窗口 {WindowId} 失败。", entry.Id);
            }
        });

        return true;
    }

    /// <inheritdoc />
    public void Close(string id, object? result = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        if (!_windows.TryGetValue(id, out var entry))
            return;

        entry.ClosedByUser = false;
        entry.ResultProvided = true;
        entry.Result = result;
        RequestClose(entry);
    }

    /// <inheritdoc />
    public void CloseAll()
    {
        var entries = _windows.Values.ToList();
        if (entries.Count == 0)
            return;

        foreach (var entry in entries)
        {
            entry.ClosedByUser = false;
            entry.ResultProvided = true;
            entry.Result = null;
        }

        try
        {
            RunOnUiThread(() =>
            {
                foreach (var entry in entries)
                {
                    try
                    {
                        if (!entry.IsClosed)
                            entry.Window.Close();
                    }
                    catch (Exception exception)
                    {
                        _logger?.LogWarning(exception, "关闭插件窗口 {WindowId} 失败。", entry.Id);
                    }
                }
            });
        }
        catch (Exception exception)
        {
            // App 退出路径不允许抛异常（调度器可能已经停止）。
            _logger?.LogWarning(exception, "关闭全部插件窗口失败。");
        }
    }

    /// <summary>非模态开窗（必须在 UI 线程调用）。</summary>
    private void ShowCore(WindowRequest request, string id)
    {
        if (_windows.TryGetValue(id, out var existing))
        {
            TryActivate(existing);
            return;
        }

        var window = CreateWindow(request);
        var entry = new WindowEntry(id, window);
        AttachHandlers(entry, request);

        if (!_windows.TryAdd(id, entry))
        {
            // 并发竞态：已经有同 id 窗口，丢弃本次创建的窗口。
            CloseWindowSilently(window);
            if (_windows.TryGetValue(id, out var raced))
                TryActivate(raced);
            return;
        }

        try
        {
            window.Show();
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "打开插件窗口 {WindowId} 失败。", id);
            _windows.TryRemove(id, out _);
            entry.Completion.TrySetResult(null);
        }
    }

    /// <summary>模态开窗（必须在 UI 线程调用）；返回用于等待结果的窗口记录。</summary>
    private WindowEntry? OpenDialogCore(WindowRequest request, string id)
    {
        if (_windows.TryGetValue(id, out var existing))
        {
            TryActivate(existing);
            return existing;
        }

        var window = CreateWindow(request);
        var entry = new WindowEntry(id, window);
        AttachHandlers(entry, request);

        if (!_windows.TryAdd(id, entry))
        {
            CloseWindowSilently(window);
            return _windows.TryGetValue(id, out var raced) ? raced : null;
        }

        if (TryGetOwner() is not { } owner || ReferenceEquals(owner, window))
        {
            // 拿不到宿主窗口：按约定退回非模态，并立即以 null 完成。
            _logger?.LogWarning("无法获取宿主窗口，插件对话框 {WindowId} 退回非模态并立即返回 null。", id);
            try
            {
                window.Show();
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "打开插件对话框 {WindowId} 失败。", id);
                _windows.TryRemove(id, out _);
            }

            entry.ResultProvided = true;
            entry.Result = null;
            entry.Completion.TrySetResult(null);
            return entry;
        }

        try
        {
            ObserveFault(window.ShowDialog(owner));
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "打开插件对话框 {WindowId} 失败。", id);
            _windows.TryRemove(id, out _);
            entry.Completion.TrySetResult(null);
        }

        return entry;
    }

    /// <summary>创建承载插件内容的窗口。</summary>
    private static Window CreateWindow(WindowRequest request)
    {
        var window = new Window
        {
            Title = request.Title ?? string.Empty,
            Content = request.Content,
            CanResize = request.CanResize,
            Topmost = request.Topmost,
            ShowInTaskbar = request.ShowInTaskbar,
            WindowStartupLocation = request.CenterOnScreen
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.Manual
        };

        if (request.Width is { } width)
            window.Width = width;
        if (request.Height is { } height)
            window.Height = height;
        if (request.Width is null && request.Height is null)
            window.SizeToContent = SizeToContent.WidthAndHeight;

        if (request.MinWidth is { } minWidth)
            window.MinWidth = minWidth;
        if (request.MinHeight is { } minHeight)
            window.MinHeight = minHeight;

        if (!request.ShowTitleBar)
        {
            // 纯内容窗：去掉系统装饰，由插件内容自己提供关闭方式。
            window.WindowDecorations = WindowDecorations.None;
            window.ExtendClientAreaToDecorationsHint = false;
            window.ExtendClientAreaTitleBarHeightHint = -1;
        }

        return window;
    }

    /// <summary>挂上关闭与 Esc 处理。</summary>
    private void AttachHandlers(WindowEntry entry, WindowRequest request)
    {
        entry.Window.Closed += (_, _) => OnWindowClosed(entry);

        if (!request.CloseOnEscape)
            return;

        entry.Window.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;

            args.Handled = true;
            entry.ClosedByUser = true;
            RequestClose(entry);
        };
    }

    /// <summary>窗口真正关闭后：移出窗口表、完成对话框任务、抛 <see cref="WindowClosed" />。</summary>
    private void OnWindowClosed(WindowEntry entry)
    {
        entry.IsClosed = true;
        _windows.TryRemove(entry.Id, out _);
        entry.Completion.TrySetResult(entry.ResultProvided ? entry.Result : null);

        try
        {
            WindowClosed?.Invoke(this, new WindowClosedEventArgs(entry.Id, entry.ClosedByUser));
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "插件窗口 {WindowId} 的关闭事件处理失败。", entry.Id);
        }
    }

    /// <summary>取宿主窗口作为模态 owner；取不到返回 null。</summary>
    private static Window? TryGetOwner()
    {
        if (MainView.Current is { } view && TopLevel.GetTopLevel(view) is Window fromView)
            return fromView;

        try
        {
            if (Application.Current is App app && app.GetRootWindow() is Window root)
                return root;
        }
        catch (InvalidOperationException)
        {
            // 没有活动窗口：退回非模态。
        }

        return null;
    }

    /// <summary>请求关闭窗口；任何线程都可以调用。</summary>
    private void RequestClose(WindowEntry entry)
    {
        void CloseCore()
        {
            try
            {
                if (!entry.IsClosed)
                    entry.Window.Close();
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "关闭插件窗口 {WindowId} 失败。", entry.Id);
            }
        }

        try
        {
            RunOnUiThread(CloseCore);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "调度插件窗口 {WindowId} 关闭失败。", entry.Id);
        }
    }

    /// <summary>激活已经存在的窗口，失败只记日志。</summary>
    private void TryActivate(WindowEntry entry)
    {
        try
        {
            entry.Window.Activate();
            if (entry.Window.WindowState == WindowState.Minimized)
                entry.Window.WindowState = WindowState.Normal;
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "激活插件窗口 {WindowId} 失败。", entry.Id);
        }
    }

    /// <summary>丢弃一个还没挂进窗口表的窗口。</summary>
    private static void CloseWindowSilently(Window window)
    {
        try
        {
            window.Close();
        }
        catch (Exception)
        {
            // 窗口可能还没显示过：忽略。
        }
    }

    /// <summary>观察模态任务异常，避免未观察异常。</summary>
    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>请求 id 为空时生成一个。</summary>
    private static string ResolveId(string? id)
    {
        return string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    }

    /// <summary>封送一个 UI 操作（对齐宿主 <c>MainViewAdapter</c> 的写法）。</summary>
    private static void RunOnUiThread(Action action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    /// <summary>封送一个返回值的 UI 操作并等待结果。</summary>
    private static async Task<T> RunOnUiThreadAsync<T>(Func<T> action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
            return action();

        return await Dispatcher.UIThread.InvokeAsync(action).GetTask().ConfigureAwait(false);
    }

    /// <summary>一个已打开插件窗口的记录。</summary>
    private sealed class WindowEntry
    {
        public WindowEntry(string id, Window window)
        {
            Id = id;
            Window = window;
        }

        public string Id { get; }

        public Window Window { get; }

        /// <summary>模态对话框的返回值任务。</summary>
        public TaskCompletionSource<object?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>true 表示用户关的（点关闭/按 Esc）。</summary>
        public volatile bool ClosedByUser = true;

        /// <summary>结果是否已由代码给出（<see cref="Close" /> / 取消 / 无 owner 降级）。</summary>
        public volatile bool ResultProvided;

        /// <summary>对话框返回值。</summary>
        public volatile object? Result;

        /// <summary>窗口是否已经真正关闭。</summary>
        public volatile bool IsClosed;
    }
}
