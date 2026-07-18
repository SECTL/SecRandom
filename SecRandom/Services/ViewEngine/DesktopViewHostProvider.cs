using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using SecRandom.Core.Views;

namespace SecRandom.Services.ViewEngine;

public sealed class DesktopViewHostProvider : IViewHostProvider
{
    private readonly List<DesktopViewHostWindow> _hosts = [];
    private int _nextHostNumber;

    public Task<ViewHostSelection> GetHostAsync(
        ViewShowOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return RunOnUiThreadAsync(() =>
        {
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

    private void HostOnDestroyed(object? sender, EventArgs e)
    {
        if (sender is DesktopViewHostWindow host)
        {
            host.Destroyed -= HostOnDestroyed;
            _hosts.Remove(host);
        }
    }

    private static Task<T> RunOnUiThreadAsync<T>(Func<T> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return Task.FromResult(action());

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
                var activeView = ActiveModalView ?? PageStack.LastOrDefault();
                if (activeView is null)
                {
                    await DestroyAsync().ConfigureAwait(false);
                    return;
                }

                var closeResult = await activeView.CloseAsync(reason: ViewCloseReason.User).ConfigureAwait(false);
                if (!closeResult.WasClosed)
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
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}
