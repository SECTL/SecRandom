using Avalonia.Xaml.Interactions.Custom;
using SecRandom.Core.Models.Draw;

namespace SecRandom.Core.Services.Draw;

public class WeightedDrawEngine<TCandidate>
{
    private readonly IRandomSource _random;

    public WeightedDrawEngine(IRandomSource random)
    {
        _random = random;
    }

    public DrawResult<TCandidate> Draw(DrawRequest<TCandidate> request)
    {
        if (request.Count == 0)
            return new DrawResult<TCandidate>
            {
                Status = DrawStatus.NoCandidates
            };
        // 获取总权重
        double totalW = 0;
        foreach (var cand in request.Candidates)
        {
            if (cand.Weight < 0)
                return new DrawResult<TCandidate>
                {
                    Status = DrawStatus.InvalidWeight
                };
            totalW += cand.Weight;
        }

        if (totalW == 0)
            return new DrawResult<TCandidate>
            {
                Status = DrawStatus.NoEligibleCandidates
            };

        var candidates = request.Candidates.ToList();
        List<TCandidate> res = [];
        for (int i = 1; i <= request.Count; i++)
        {
            double r = _random.NextDouble() * totalW;
            for (int j = 0; j < candidates.Count; j++)
            {
                r -= candidates[j].Weight;
                if (r.CompareTo(0.0) < 0)
                {
                    res.Add(candidates[j].Candidate);
                    candidates.RemoveAt(j);
                    break;
                }
            }
        }

        return new DrawResult<TCandidate>
        {
            Status = DrawStatus.Success,
            Result = res
        };
    }
}
