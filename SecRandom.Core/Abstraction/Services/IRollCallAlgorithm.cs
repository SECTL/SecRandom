using SecRandom.Core.Models.Draw;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Draw;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

/// <summary>
/// Produces the weighted candidate pool used by the host roll-call pipeline.
/// Implementations must not select winners or mutate profile/history state.
/// </summary>
public interface IRollCallAlgorithm
{
    IReadOnlyList<WeightedCandidate<Student>> BuildCandidates(
        DrawEngine engine,
        IReadOnlyList<Student> eligibleCandidates,
        IReadOnlyDictionary<Student, History> history,
        FairDrawPolicySnapshot fairSettings,
        string courseName);
}

public interface IRollCallAlgorithmRegistry
{
    IRollCallAlgorithm Resolve(string? id);
    IReadOnlyList<IRollCallAlgorithm> GetAll();
}
