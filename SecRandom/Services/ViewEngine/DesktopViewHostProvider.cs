using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SecRandom.Core.Views;

namespace SecRandom.Services.ViewEngine;

public sealed class DesktopViewHostProvider : IViewHostProvider
{
    private readonly List<DesktopViewHostWindow> _hosts = [];
    private readonly Dictionary<string, IViewHost> _embeddedHosts = new(StringComparer.Ordinal);
    private int _nextHostNumber;

    public Task<ViewHostSelection> GetHostAsync(
        ViewShowOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return RunOnUiThreadAsync(() =>
        {
            if (!string.IsNullOrWhiteSpace(options.HostId))
            {
                if (options.ActivationPreference == ViewActivationPreference.NewHost)
                    return ViewHostSelection.Unsupported("A named host cannot be combined with creating a new host.");

                return _embeddedHosts.TryGetValue(options.HostId, out var embeddedHost)
                    ? ViewHostSelection.Success(embeddedHost)
                    : ViewHostSelection.Failed($"Desktop view host '{options.HostId}' is not registered.");
            }

            var existing = _hosts.LastOrDefault(host => !host.IsClosed);
            if (options.ActivationPreference == ViewActivationPreference.ExistingHost)
            {
                return existing is null
                    ? ViewHostSelection.Failed("No desktop view host is currently open.")
                    : ViewHostSelection.Success(existing);
            }

            if (options.ActivationPreference == ViewActivationPreference.Default && existing is not null)
                return ViewHostSelection.Success(existing);

            var host = new DesktopViewHostWindow($"desktop.view-host.{++_nextHostNumber}");
            host.Destroyed += HostOnDestroyed;
            _hosts.Add(host);
            return ViewHostSelection.Success(host);
        });
    }

    public Task RegisterEmbeddedHostAsync(IViewHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        return RunOnUiThreadAsync(() => RegisterEmbeddedHost(host));
    }

    public void RegisterEmbeddedHost(IViewHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        // 无 Avalonia 应用（单元测试）时跳过 UI 线程强制检查，与 RunOnUiThreadAsync 的内联执行一致。
        if (Application.Current is not null && !Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Desktop embedded hosts must be registered on the UI thread.");

        if (_embeddedHosts.TryGetValue(host.HostId, out var existing))
        {
            if (ReferenceEquals(existing, host))
                return;

            // 自愈：同 ID 旧实例（关窗清理竞态中的残留）先同步销毁注销，再注册新实例，
            // 替代直接抛异常导致整个窗口创建失败。DestroyAsync 的 UI 线程段同步完成。
            existing.DestroyAsync().GetAwaiter().GetResult();
            UnregisterEmbeddedHost(existing);
        }

        _embeddedHosts.Add(host.HostId, host);
        host.Destroyed += EmbeddedHostOnDestroyed;
    }

    public Task UnregisterEmbeddedHostAsync(IViewHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        return RunOnUiThreadAsync(() => UnregisterEmbeddedHost(host));
    }

    private void HostOnDestroyed(object? sender, EventArgs e)
    {
        if (sender is DesktopViewHostWindow host)
        {
            host.Destroyed -= HostOnDestroyed;
            _hosts.Remove(host);
        }
    }

    private void EmbeddedHostOnDestroyed(object? sender, EventArgs e)
    {
        if (sender is IViewHost host)
            UnregisterEmbeddedHost(host);
    }

    private void UnregisterEmbeddedHost(IViewHost host)
    {
        if (!_embeddedHosts.TryGetValue(host.HostId, out var existing) || !ReferenceEquals(existing, host))
            return;

        existing.Destroyed -= EmbeddedHostOnDestroyed;
        _embeddedHosts.Remove(host.HostId);
    }

    private static Task<T> RunOnUiThreadAsync<T>(Func<T> action)
    {
        // Application.Current 为 null（如单元测试无 Avalonia 应用）时直接内联执行，
        // 避免向不存在/未 pumping 的 UI 调度器投递任务而永久悬挂。
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
            return Task.FromResult(action());

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    private static Task RunOnUiThreadAsync(Action action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}

internal sealed class DesktopViewHostWindow : Window, IViewHost
{
    private readonly ViewHostControl _contentHost;
    private bool _allowClose;
    private bool _isClosed;
    private bool _isUserCloseRequestPending;

    public DesktopViewHostWindow(string hostId)
    {
        _contentHost = new ViewHostControl(hostId);
        _contentHost.Destroyed += (_, _) => Destroyed?.Invoke(this, EventArgs.Empty);

        Title = "SecRandom";
        Width = 720;
        Height = 480;
        MinWidth = 320;
        MinHeight = 240;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = _contentHost;

        Closing += WindowOnClosing;
        Closed += WindowOnClosed;
    }

    public string HostId => _contentHost.HostId;
    public IReadOnlyList<ViewBase> PageStack => _contentHost.PageStack;
    public ViewBase? ActiveModalView => _contentHost.ActiveModalView;
    public bool IsClosed => _isClosed;
    public event EventHandler? Destroyed;

    public async Task ShowPageAsync(ViewBase view, CancellationToken cancellationToken = default)
    {
        await _contentHost.ShowPageAsync(view, cancellationToken).ConfigureAwait(false);
        await EnsureVisibleAsync().ConfigureAwait(false);
    }

    public async Task ShowModalAsync(ViewBase view, CancellationToken cancellationToken = default)
    {
        await _contentHost.ShowModalAsync(view, cancellationToken).ConfigureAwait(false);
        await EnsureVisibleAsync().ConfigureAwait(false);
    }

    public async Task ActivateAsync(ViewBase view, CancellationToken cancellationToken = default)
    {
        await _contentHost.ActivateAsync(view, cancellationToken).ConfigureAwait(false);
        await RunOnUiThreadAsync(() =>
        {
            if (!IsVisible)
                Show();
            Activate();
        }).ConfigureAwait(false);
    }

    public async Task CloseAsync(ViewBase view, CancellationToken cancellationToken = default)
    {
        await _contentHost.CloseAsync(view, cancellationToken).ConfigureAwait(false);
        await CloseWindowWhenEmptyAsync().ConfigureAwait(false);
    }

    public Task DestroyAsync(CancellationToken cancellationToken = default)
    {
        return RunOnUiThreadAsync(() =>
        {
            if (_isClosed)
                return;

            _allowClose = true;
            Close();
        });
    }

    private void WindowOnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
            return;

        if (e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return;

        e.Cancel = true;
        if (_isUserCloseRequestPending)
            return;

        _isUserCloseRequestPending = true;
        _ = RequestUserCloseAsync();
    }

    private async Task RequestUserCloseAsync()
    {
        try
        {
            while (true)
            {
                var closed = await _contentHost.CloseActiveViewAsync().ConfigureAwait(false);
                if (closed)
                    continue;

                if (ActiveModalView is not null || PageStack.Count != 0)
                    return;

                await DestroyAsync().ConfigureAwait(false);
                return;
            }
        }
        finally
        {
            _isUserCloseRequestPending = false;
        }
    }

    private Task EnsureVisibleAsync()
    {
        return RunOnUiThreadAsync(() =>
        {
            if (!IsVisible)
                Show();
            Activate();
        });
    }

    private Task CloseWindowWhenEmptyAsync()
    {
        return RunOnUiThreadAsync(() =>
        {
            if (_isClosed || ActiveModalView is not null || PageStack.Count != 0)
                return;

            _allowClose = true;
            Close();
        });
    }

    private void WindowOnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _ = _contentHost.DestroyAsync();
    }

    private static Task RunOnUiThreadAsync(Action action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}
