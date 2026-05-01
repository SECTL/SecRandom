namespace SecRandom.Core.Models.Draw;

public class DrawRequest<TCandidate>
{
    public IReadOnlyList<WeightedCandidate<TCandidate>> Candidates { get; init; } = []; // 候选项列表

    public int Count { get; init; } = 1; // 抽取个数
}
