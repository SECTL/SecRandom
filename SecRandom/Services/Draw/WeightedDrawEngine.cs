using System.Collections.Generic;
using System.Linq;
using SecRandom.Models.Draw;

namespace SecRandom.Services.Draw;

public class WeightedDrawEngine<TCandidate>
{
    private readonly IRandomSource random;

    public WeightedDrawEngine(IRandomSource random)
    {
        this.random = random;
    }

    public DrawResult<TCandidate> Draw(DrawRequest<TCandidate> request)
    {
        if (request.Count == 0)
            return new DrawResult<TCandidate>
            {
                Status = DrawStatus.NoCandidates
            };
        if (request.Count > request.Candidates.Count)
            return new DrawResult<TCandidate>
            {
                Status = DrawStatus.Failure
            };

        // 获取总权重
        double totalW = request.Candidates.Sum(c => c.Weight);

        if (totalW == 0)
            return new DrawResult<TCandidate>
            {
                Status = DrawStatus.NoEligibleCandidates
            };

        var candidates = request.Candidates.ToList();
        List<TCandidate> res = [];
        for (int i = 1; i <= request.Count; i++)
        {
            double r = random.NextDouble() * totalW;
            for (int j = 0; j < candidates.Count; j++)
            {
                r -= candidates[j].Weight;
                if (r < 0)
                {
                    res.Add(candidates[j].Candidate);
                    totalW -= candidates[j].Weight;
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
