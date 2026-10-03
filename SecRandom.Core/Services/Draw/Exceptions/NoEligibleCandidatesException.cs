namespace SecRandom.Core.Services.Draw.Exceptions;

/// <summary>
///     The frozen pool exists but carries no drawable weight: every remaining candidate is temporarily
///     excluded (for example by the post-draw shield) or weighted at zero. This is a normal "nothing to
///     draw right now" outcome for verification draws, so callers must surface it as
///     <c>DrawStatus.NoEligibleCandidates</c> instead of letting the sampler fail.
/// </summary>
public class NoEligibleCandidatesException : Exception
{
    public NoEligibleCandidatesException()
    {
    }

    public NoEligibleCandidatesException(string message) : base(message)
    {
    }

    public NoEligibleCandidatesException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
