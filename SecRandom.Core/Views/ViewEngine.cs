using Microsoft.Extensions.DependencyInjection;

namespace SecRandom.Core.Views;

public sealed class ViewEngine(IServiceProvider services, IViewRegistry registry, IViewHostProvider hostProvider) : IViewEngine
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

                await existingSession.Host.ActivateAsync(existingSession.View, cancellationToken).ConfigureAwait(false);
                return new ViewHandle(existingSession, CloseSessionAsync);
            }

            if (!registry.TryGet(viewId, out var registration) || registration is null)
                throw new KeyNotFoundException($"View '{viewId}' is not registered.");

            var hostSelection = await hostProvider.GetHostAsync(options, cancellationToken).ConfigureAwait(false);
            if (!hostSelection.IsSuccess || hostSelection.Host is null)
                throw new ViewHostUnavailableException(hostSelection, viewId);

            ObserveHost(hostSelection.Host);
            var view = ActivatorUtilities.CreateInstance(services, registration.ViewType) as ViewBase
                       ?? throw new InvalidOperationException($"View '{viewId}' could not be created as a ViewBase.");
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

    public async Task<ViewCloseResult> ShowModalAsync(string viewId, ViewShowOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = options is null
            ? new ViewShowOptions { Presentation = ViewPresentation.Modal }
            : new ViewShowOptions
            {
                ActivationPreference = options.ActivationPreference,
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

        ViewSession? session;
        lock (_gate)
        {
            _sessionsById.TryGetValue(viewId, out session);
        }

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

        if (!session.View.TryBeginClose(request.Reason, request.Result, request.IsCancelable, out var closeResult))
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
        session.View.CompleteClose(closeResult);
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
            _ = CloseHostAsync(host, ViewCloseReason.HostShutdown);
    }
}
