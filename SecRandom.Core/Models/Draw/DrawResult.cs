namespace SecRandom.Core.Models.Draw;

public enum DrawStatus
{
    Success,
    NoCandidates,
    NoEligibleCandidates,
    RepeatLimitExhausted,
    Failure
}

public class DrawResult<TCandidate>
{
    public IReadOnlyList<TCandidate> Result { get; init; } = [];
    public DrawStatus Status { get; init; }
    public bool IsSuccess => Status == DrawStatus.Success;
}
