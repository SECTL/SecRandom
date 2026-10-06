namespace SecRandom.Core.Abstraction.Services.Presentation;

/// <summary>
///     Plugin side of draw result presentation. Register an implementation during
///     <c>PluginBase.Initialize</c>:
///     <code>services.AddSingleton&lt;IDrawResultPresenter, MyRecruitPresenter&gt;();</code>
///     and the host offers every committed draw round (roll call, lottery, quick draw, remote and mobile
///     channels) to it.
/// </summary>
/// <remarks>
///     Presenters run on the UI thread. <see cref="PresentAsync" /> should not throw: a presenter that throws
///     is logged and skipped for that round, and the host falls back to its own presentation. Return
///     <see cref="DrawPresentationDecision.NotHandled" /> when the round is not yours (for example when the
///     user turned the effect off) so other presenters and the host can still handle it.
/// </remarks>
public interface IDrawResultPresenter
{
    /// <summary>Stable unique id, used for diagnostics and unregistering.</summary>
    string Id { get; }

    /// <summary>Human readable name shown in host diagnostics.</summary>
    string DisplayName { get; }

    /// <summary>Higher priority presenters are asked first.</summary>
    int Priority => 0;

    /// <summary>Cheap check whether this presenter wants the request. Must not throw.</summary>
    bool CanPresent(DrawPresentationRequest request);

    /// <summary>Present the round, or decline it.</summary>
    Task<DrawPresentationDecision> PresentAsync(DrawPresentationRequest request,
        CancellationToken cancellationToken = default);
}
