namespace SecRandom.Core.Abstraction.Services.Presentation;

/// <summary>
///     Host dispatch point for draw result presentation. Resolvable from a plugin through
///     <c>IAppHost.TryGetService&lt;IResultPresentationService&gt;()</c>; a plugin may also call
///     <see cref="PresentAsync" /> itself to replay a round or present a result it computed on its own.
/// </summary>
public interface IResultPresentationService
{
    /// <summary>All registered presenters, highest priority first.</summary>
    IReadOnlyList<IDrawResultPresenter> Presenters { get; }

    /// <summary>The presenter that would handle <paramref name="request" />, or <c>null</c> when nobody does.</summary>
    IDrawResultPresenter? Resolve(DrawPresentationRequest request);

    /// <summary>
    ///     Offer the request to every presenter in priority order until one takes it. Presenters that throw are
    ///     logged and skipped. Returns <see cref="DrawPresentationDecision.NotHandled" /> when nobody takes the
    ///     round, in which case the host presents it with its own animation.
    /// </summary>
    Task<DrawPresentationDecision> PresentAsync(DrawPresentationRequest request,
        CancellationToken cancellationToken = default);
}
