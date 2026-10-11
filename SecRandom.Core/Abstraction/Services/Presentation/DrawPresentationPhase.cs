namespace SecRandom.Core.Abstraction.Services.Presentation;

/// <summary>
///     Presentation phase of a draw round.
///     <see cref="Preview" /> is the rolling / placeholder phase that normally runs while the draw is still
///     being computed. <see cref="Reveal" /> is the moment the committed result is shown to the audience.
/// </summary>
public enum DrawPresentationPhase
{
    /// <summary>Rolling / waiting phase. Runs concurrently with the draw itself.</summary>
    Preview = 0,

    /// <summary>The committed result is being shown.</summary>
    Reveal = 1
}
