using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

public sealed class FairRollCallAlgorithm : IRollCallAlgorithm
{
    public IReadOnlyList<WeightedCandidate<Student>> BuildCandidates(
        DrawEngine engine,
        IReadOnlyList<Student> eligibleCandidates,
        IReadOnlyDictionary<Student, History> history,
        FairDrawPolicySnapshot fairSettings,
        string courseName)
    {
        return engine.CalculateStudentWeight(eligibleCandidates.ToList(), fairSettings, history, courseName);
    }
}

public sealed class RandomRollCallAlgorithm : IRollCallAlgorithm
{
    public IReadOnlyList<WeightedCandidate<Student>> BuildCandidates(
        DrawEngine engine,
        IReadOnlyList<Student> eligibleCandidates,
        IReadOnlyDictionary<Student, History> history,
        FairDrawPolicySnapshot fairSettings,
        string courseName) =>
        eligibleCandidates.Select(student => new WeightedCandidate<Student>
        {
            Candidate = student,
            Weight = 1.0
        }).ToArray();
}

public sealed class InventoryLotteryAlgorithm : ILotteryAlgorithm
{
    public IReadOnlyList<WeightedCandidate<Prize>> BuildCandidates(
        IReadOnlyList<Prize> prizes, IReadOnlyDictionary<Prize, History> historyCache)
    {
        List<WeightedCandidate<Prize>> result = [];
        foreach (var prize in prizes)
        {
            var remaining = Math.Max(0, prize.Count - (historyCache.GetValueOrDefault(prize)?.TotalCount ?? 0));
            for (var i = 0; i < remaining; i++)
                result.Add(new WeightedCandidate<Prize> { Candidate = prize, Weight = 1.0 });
        }
        return result;
    }
}

public sealed class WeightedLotteryAlgorithm : ILotteryAlgorithm
{
    public IReadOnlyList<WeightedCandidate<Prize>> BuildCandidates(
        IReadOnlyList<Prize> prizes, IReadOnlyDictionary<Prize, History> historyCache) =>
        prizes.Select(prize => new WeightedCandidate<Prize> { Candidate = prize, Weight = prize.Weight }).ToArray();
}
