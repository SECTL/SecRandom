namespace SecRandom.Models.Draw;

public class WeightedCandidate<TCandidate>
{
    public required TCandidate Candidate { get; init; }
    public double Weight { get; set; }
}
