namespace SecRandom.Models.Draw;

public struct WeightedCandidate<TCandidate>
{
    public TCandidate Candidate { get; init; }
    public double Weight { get; init; }
}
