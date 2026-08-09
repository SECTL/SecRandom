namespace SecRandom.Core.Views;

/// <summary>
/// Provides the one physical host available to a single-view application.
/// </summary>
public sealed class SingleViewHostProvider : IViewHostProvider
{
    private IViewHost? _host;

    public void Attach(IViewHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_host is not null && !ReferenceEquals(_host, host))
            throw new InvalidOperationException("A single-view host is already attached.");

        _host = host;
    }

    /// <summary>
    /// Releases a previously attached host so a replacement root view can attach its own
    /// host (used by the mobile restart-free language rebuild). Detaching an unknown host
    /// is a no-op.
    /// </summary>
    public void Detach(IViewHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (ReferenceEquals(_host, host))
            _host = null;
    }

    public Task<ViewHostSelection> GetHostAsync(
        ViewShowOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ActivationPreference == ViewActivationPreference.NewHost)
            return Task.FromResult(ViewHostSelection.Unsupported(
                "This platform provides one single-view host and cannot create a new host."));

        return Task.FromResult(_host is null
            ? ViewHostSelection.Failed("The single-view host has not been attached.")
            : ViewHostSelection.Success(_host));
    }
}
