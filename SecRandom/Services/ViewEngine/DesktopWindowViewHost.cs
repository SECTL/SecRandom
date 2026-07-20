using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SecRandom.Core.Views;

namespace SecRandom.Services.ViewEngine;

/// <summary>
/// Adapts an application-owned desktop window into the physical host for one logical MVE view.
/// The shell view is the MVE page; its child pages remain ordinary controls inside that shell.
/// </summary>
internal sealed class DesktopWindowViewHost : IViewHost
{
    private readonly Window _window;
    private readonly ViewHostControl _contentHost;
    private bool _isClosed;

    public DesktopWindowViewHost(Window window, string hostId)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);

        _window = window;
        _contentHost = new ViewHostControl(hostId);
        _contentHost.Destroyed += (_, _) => Destroyed?.Invoke(this, EventArgs.Empty);
        _window.Content = _contentHost;
        _window.Closed += WindowOnClosed;
    }

    public string HostId => _contentHost.HostId;
    public IReadOnlyList<ViewBase> PageStack => _contentHost.PageStack;
    public ViewBase? ActiveModalView => _contentHost.ActiveModalView;
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
        await EnsureVisibleAsync().ConfigureAwait(false);
    }

    public async Task CloseAsync(ViewBase view, CancellationToken cancellationToken = default)
    {
        await _contentHost.CloseAsync(view, cancellationToken).ConfigureAwait(false);
    }

    public Task DestroyAsync(CancellationToken cancellationToken = default) => _contentHost.DestroyAsync(cancellationToken);

    private Task EnsureVisibleAsync()
    {
        return RunOnUiThreadAsync(() =>
        {
            if (_isClosed)
                throw new ObjectDisposedException(HostId, "The desktop window host has been closed.");

            global::SecRandom.App.RestoreAndActivate(_window);
        });
    }

    private void WindowOnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _window.Closed -= WindowOnClosed;
        _contentHost.DestroyAsync().GetAwaiter().GetResult();
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
