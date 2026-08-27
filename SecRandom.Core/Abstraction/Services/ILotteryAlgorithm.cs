using SecRandom.Core.Models.Draw;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

public interface ILotteryAlgorithm
{
    IReadOnlyList<WeightedCandidate<Prize>> BuildCandidates(
        IReadOnlyList<Prize> prizes,
        IReadOnlyDictionary<Prize, History> historyCache);
}

public interface ILotteryAlgorithmRegistry
{
    ILotteryAlgorithm Resolve(string? id);
    IReadOnlyList<ILotteryAlgorithm> GetAll();
}
