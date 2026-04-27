using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Models.Draw;

public class DrawRequest<TCandidate>
{
    public IReadOnlyList<WeightedCandidate<TCandidate>> Candidates { get; init; } = []; // 候选项列表

    public int Count { get; init; } = 1; // 抽取个数
}

public class StudentDrawRequest
{
    public int Count { get; set; } = 1;
    public Func<Student,bool>? Filter { get; set; }
}

public class PrizeDrawRequest
{
    public int Count { get; set; } = 1;
    public Func<Prize,bool>? Filter { get; set; }
}

