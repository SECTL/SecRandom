namespace SecRandom.Core.Abstraction.Services.Presentation;

/// <summary>
///     What a presenter did with a <see cref="DrawPresentationRequest" />.
/// </summary>
/// <param name="Handled">
///     <c>true</c> when the presenter took over this phase, so the host skips its own animation for it.
/// </param>
/// <param name="HideHostResult">
///     <c>true</c> when the host's built-in result area must also stay hidden for this round because the
///     presenter draws everything itself. Ignored unless <paramref name="Handled" /> is <c>true</c>.
/// </param>
public sealed record DrawPresentationDecision(bool Handled, bool HideHostResult = false)
{
    /// <summary>Decline the round; the host keeps its own presentation.</summary>
    public static DrawPresentationDecision NotHandled { get; } = new(false);

    /// <summary>Take over the phase.</summary>
    /// <param name="hideHostResult">Also hide the host's built-in result area.</param>
    public static DrawPresentationDecision Taken(bool hideHostResult = false) => new(true, hideHostResult);
}
