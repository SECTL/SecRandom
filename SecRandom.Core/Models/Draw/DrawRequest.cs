namespace SecRandom.Core.Models.Draw;

public class DrawRequest<TCandidate>
{
    public IReadOnlyList<TCandidate> Candidates { get; init; } = []; // 候选项列表

    public int Count { get; init; } = 1; // 抽取个数

    public int MaxRepeats { get; init; } = 0; // 不重复 / 半重复抽取选项

    public Func<TCandidate, double>? WeightSelector { get; init; }
}
