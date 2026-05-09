using SecRandom.Core.Interfaces;
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
        if (request.Count <= 0)
            return new DrawResult<TCandidate>
            {
                Status = DrawStatus.Failure
            };

        if (request.Candidates.Count == 0)
            return new DrawResult<TCandidate>
            {
                Status = DrawStatus.NoCandidates
            };

        if (request.Count > request.Candidates.Count)
            return new DrawResult<TCandidate>
            {
                Status = DrawStatus.Failure
            };

        if (request.Candidates.Any(c => c.Weight < 0 || double.IsNaN(c.Weight) || double.IsInfinity(c.Weight)))
            return new DrawResult<TCandidate>
            {
                Status = DrawStatus.InvalidWeight
            };

        if (request.Count > request.Candidates.Count(c => c.Weight > 0))
            return new DrawResult<TCandidate>
            {
                Status = DrawStatus.NoEligibleCandidates
            };

        // 获取总权重
        var totalW = request.Candidates.Sum(c => c.Weight);

        if (totalW <= 0)
            return new DrawResult<TCandidate>
            {
                Status = DrawStatus.NoEligibleCandidates
            };

        var candidates = request.Candidates.ToList();
        List<TCandidate> res = [];
        for (var i = 1; i <= request.Count; i++)
        {
            var r = _random.NextDouble() * totalW;
            for (var j = 0; j < candidates.Count; j++)
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