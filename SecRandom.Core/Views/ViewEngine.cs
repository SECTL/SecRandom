using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace SecRandom.Core.Views;

internal sealed class ViewEngine(IServiceProvider services, IViewRegistry registry, IViewHostProvider hostProvider) : IViewEngine
{
    private readonly Dictionary<string, ViewSession> _sessionsById = new(StringComparer.Ordinal);
    private readonly Dictionary<ViewBase, ViewSession> _sessionsByView = [];
    private readonly HashSet<IViewHost> _observedHosts = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IViewHandle> ShowAsync(string viewId, ViewShowOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewId);
        options ??= new ViewShowOptions();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessionsById.TryGetValue(viewId, out var existingSession))
            {
                if (!options.ReuseExistingView)
                    throw new InvalidOperationException($"View '{viewId}' is already active.");

                try
                {
                    await existingSession.Host.ActivateAsync(existingSession.View, cancellationToken).ConfigureAwait(false);
                    return new ViewHandle(existingSession, CloseSessionAsync);
                }
                catch (ObjectDisposedException)
                {
                    // host 已销毁但会话尚未摘除（桌面关窗清理竞态）：按 HostDestroyed 结束残留会话，
                    // 然后落到下面的新建路径重建视图，而不是把异常抛给调用方。
                    _sessionsById.Remove(viewId);
                    _sessionsByView.Remove(existingSession.View);
                    var staleResult = ViewCloseResult.AlreadyClosed(ViewCloseReason.HostDestroyed);
                    await RunOnUiThreadAsync(() => existingSession.View.CompleteClose(staleResult), cancellationToken)
                        .ConfigureAwait(false);
                    existingSession.TryComplete(staleResult);
                }
            }

            if (!registry.TryGet(viewId, out var registration) || registration is null)
                throw new KeyNotFoundException($"View '{viewId}' is not registered.");

            var hostSelection = await hostProvider.GetHostAsync(options, cancellationToken).ConfigureAwait(false);
            if (!hostSelection.IsSuccess || hostSelection.Host is null)
                throw new ViewHostUnavailableException(hostSelection, viewId);

            ObserveHost(hostSelection.Host);
            var view = await CreateViewAsync(registration, cancellationToken).ConfigureAwait(false);
            var presentation = options.Presentation ?? registration.DefaultPresentation;
            var session = new ViewSession(viewId, view, hostSelection.Host, presentation);
            view.Attach(viewId, (request, token) => CloseSessionAsync(session, request, token));
            _sessionsById.Add(viewId, session);
            _sessionsByView.Add(view, session);

            try
            {
                if (presentation == ViewPresentation.Modal)
                    await hostSelection.Host.ShowModalAsync(view, cancellationToken).ConfigureAwait(false);
                else
                    await hostSelection.Host.ShowPageAsync(view, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _sessionsById.Remove(viewId);
                _sessionsByView.Remove(view);
                view.Detach();
                throw;
            }

            return new ViewHandle(session, CloseSessionAsync);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IViewHandle> ShowExclusiveAsync(string hostId, string viewId, ViewShowOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewId);

        List<ViewSession> siblingPages;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            siblingPages = _sessionsById.Values
                .Where(session => !string.Equals(session.ViewId, viewId, StringComparison.Ordinal)
                                  && session.Presentation == ViewPresentation.Page
                                  && string.Equals(session.Host.HostId, hostId, StringComparison.Ordinal))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var session in siblingPages)
            await CloseSessionAsync(session, new ViewCloseRequest(ViewCloseReason.Programmatic, null, false), cancellationToken)
                .ConfigureAwait(false);

        var exclusiveOptions = new ViewShowOptions
        {
            ActivationPreference = options?.ActivationPreference ?? ViewActivationPreference.Default,
            HostId = hostId,
            Presentation = options?.Presentation,
            ReuseExistingView = options?.ReuseExistingView ?? true
        };
        return await ShowAsync(viewId, exclusiveOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ViewCloseResult> ShowModalAsync(string viewId, ViewShowOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = options is null
            ? new ViewShowOptions { Presentation = ViewPresentation.Modal }
            : new ViewShowOptions
            {
                ActivationPreference = options.ActivationPreference,
                HostId = options.HostId,
                Presentation = ViewPresentation.Modal,
                ReuseExistingView = options.ReuseExistingView
            };

        var handle = await ShowAsync(viewId, options, cancellationToken);
        return await handle.Completion;
    }

    public Task<ViewCloseResult> CloseAsync(
        string viewId,
        ViewCloseReason reason = ViewCloseReason.Programmatic,
        object? result = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewId);
        return CloseByIdAsync(viewId, reason, result, cancellationToken);
    }

    private async Task<ViewCloseResult> CloseByIdAsync(
        string viewId,
        ViewCloseReason reason,
        object? result,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return !_sessionsById.TryGetValue(viewId, out var session)
                ? ViewCloseResult.AlreadyClosed(reason, result)
                : await CloseSessionCoreAsync(session, new ViewCloseRequest(reason, result, true), cancellationToken)
                    .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ViewCloseResult> CloseSessionAsync(
        ViewSession session,
        ViewCloseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CloseSessionCoreAsync(session, request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ViewCloseResult> CloseSessionCoreAsync(
        ViewSession session,
        ViewCloseRequest request,
        CancellationToken cancellationToken)
    {
        if (!_sessionsByView.TryGetValue(session.View, out var activeSession) || !ReferenceEquals(activeSession, session))
            return ViewCloseResult.AlreadyClosed(request.Reason, request.Result);

        var closeResult = await RunOnUiThreadAsync(() =>
        {
            return session.View.TryBeginClose(request.Reason, request.Result, request.IsCancelable, out var result)
                ? result
                : result;
        }, cancellationToken).ConfigureAwait(false);
        if (!closeResult.WasClosed)
            return closeResult;

        try
        {
            await session.Host.CloseAsync(session.View, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ViewCloseResult.Failed(request.Reason, request.Result, ex.Message);
        }

        _sessionsById.Remove(session.ViewId);
        _sessionsByView.Remove(session.View);
        await RunOnUiThreadAsync(() => session.View.CompleteClose(closeResult), cancellationToken)
            .ConfigureAwait(false);
        session.TryComplete(closeResult);
        return closeResult;
    }

    public async Task CloseHostAsync(IViewHost host, ViewCloseReason reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        List<ViewSession> sessions;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            sessions = _sessionsById.Values.Where(session => ReferenceEquals(session.Host, host)).ToList();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var session in sessions)
            await CloseSessionAsync(session, new ViewCloseRequest(reason, null, false), cancellationToken)
                .ConfigureAwait(false);
    }

    private void ObserveHost(IViewHost host)
    {
        if (!_observedHosts.Add(host))
            return;

        host.Destroyed += HostOnDestroyed;
    }

    private void HostOnDestroyed(object? sender, EventArgs e)
    {
        if (sender is IViewHost host)
            _ = HandleHostDestroyedAsync(host);
    }

    private async Task HandleHostDestroyedAsync(IViewHost host)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_observedHosts.Remove(host))
                return;

            host.Destroyed -= HostOnDestroyed;
        }
        finally
        {
            _gate.Release();
        }

        await CloseHostAsync(host, ViewCloseReason.HostDestroyed).ConfigureAwait(false);
    }

    private Task<ViewBase> CreateViewAsync(ViewRegistration registration, CancellationToken cancellationToken)
    {
        ViewBase Create()
        {
            if (!string.IsNullOrWhiteSpace(registration.PluginId))
            {
                return Activator.CreateInstance(registration.ViewType) as ViewBase
                       ?? throw new InvalidOperationException(
                           $"Plugin view '{registration.Id}' could not be created as a ViewBase.");
            }

            return ActivatorUtilities.CreateInstance(services, registration.ViewType) as ViewBase
                   ?? throw new InvalidOperationException($"View '{registration.Id}' could not be created as a ViewBase.");
        }

        return RunOnUiThreadAsync(Create, cancellationToken);
    }

    private static Task<T> RunOnUiThreadAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
            return Task.FromResult(action());

        return Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).GetTask();
    }

    private static Task RunOnUiThreadAsync(Action action, CancellationToken cancellationToken)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).GetTask();
    }
}
