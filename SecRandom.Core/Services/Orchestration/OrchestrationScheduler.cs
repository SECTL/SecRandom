using SecRandom.Core.Interfaces;
using SecRandom.Core.Models.Orchestration;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services.Profiles;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Orchestration;

/// <summary>
///     Resolves one round of an arranged draw into the concrete order the participants come out in.
/// </summary>
/// <remarks>
///     The weights reuse the configured fair-draw settings (base weight, shield, mode) through
///     <see cref="DrawEngine.CalculateStudentWeight" />, and a per-round manual weight multiplies them. The five
///     rule lists are applied by <see cref="OrchestrationOrdering.Resolve" />, which honours
///     <see cref="OrchestrationProfile.Rules" />: a switched-off list keeps its data but takes no part in the order.
/// </remarks>
public sealed class OrchestrationScheduler(DrawEngine drawEngine)
{
    public IReadOnlyList<Student> OrderRound(
        OrchestrationProfile profile,
        OrchestrationRound round,
        IReadOnlyList<Student> participants,
        IReadOnlySet<Guid> alreadyDrawn,
        IRandomSource random,
        string courseName = "",
        string? historyGroup = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(alreadyDrawn);
        ArgumentNullException.ThrowIfNull(random);

        var pool = participants.Where(candidate => candidate.IsCandidate).ToList();
        if (pool.Count == 0)
            return [];

        var weights = drawEngine.CalculateStudentWeight(pool, null, courseName, historyGroup);
        var figures = drawEngine.GetStudentHistoryFigures(pool, courseName, historyGroup);

        var candidates = new List<OrchestrationCandidate>(weights.Count);
        foreach (var weighted in weights)
        {
            var student = weighted.Candidate;
            var recordId = RecordIdOf(student);
            var member = profile.FindMember(recordId);
            var figure = figures.TryGetValue(recordId, out var entry)
                ? entry
                : (Count: 0, LastDrawn: DateTime.MinValue);
            // 每轮手动权重由 OrchestrationOrdering 统一乘进来，这里只给公平算法算出的原始权重。
            candidates.Add(new OrchestrationCandidate(
                recordId,
                Math.Max(0d, weighted.Weight),
                figure.Count,
                figure.LastDrawn,
                member?.IsExcluded == true));
        }

        var ordered = OrchestrationOrdering.Resolve(candidates, round, alreadyDrawn, random, profile.Rules);
        var byRecordId = pool
            .GroupBy(RecordIdOf)
            .ToDictionary(group => group.Key, group => group.First());
        return ordered
            .Where(byRecordId.ContainsKey)
            .Select(recordId => byRecordId[recordId])
            .ToList();
    }

    private static Guid RecordIdOf(Student student)
    {
        if (student.RecordId == Guid.Empty)
            ProfileRecordIdentity.EnsureRecordId(student);
        return student.RecordId;
    }

    /// <summary>
    ///     Prize-side counterpart of the student <c>OrderRound</c> overload.
    /// </summary>
    /// <remarks>
    ///     A prize pool has no fair-draw weighting: the pool's own <see cref="Prize.Weight" /> is the base weight and the
    ///     per-round manual weight multiplies it, so an unconfigured round keeps the pool's existing proportions.
    ///     必中 and 顺序 apply the same way, and a postponed prize is skipped for that round only.
    /// </remarks>
    public IReadOnlyList<Prize> OrderRound(
        OrchestrationProfile profile,
        OrchestrationRound round,
        IReadOnlyList<Prize> participants,
        IReadOnlyDictionary<Guid, int> drawnCounts,
        IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(drawnCounts);
        ArgumentNullException.ThrowIfNull(random);

        var pool = participants.Where(candidate => candidate.IsCandidate).ToList();
        if (pool.Count == 0)
            return [];

        var candidates = new List<OrchestrationCandidate>(pool.Count);
        foreach (var prize in pool)
        {
            var recordId = RecordIdOf(prize);
            var member = profile.FindMember(recordId);
            candidates.Add(new OrchestrationCandidate(
                recordId,
                Math.Max(0d, prize.Weight),
                drawnCounts.GetValueOrDefault(recordId),
                DateTime.MinValue,
                member?.IsExcluded == true));
        }

        var ordered = OrchestrationOrdering.Resolve(candidates, round, new HashSet<Guid>(), random, profile.Rules);
        var byRecordId = pool
            .GroupBy(RecordIdOf)
            .ToDictionary(group => group.Key, group => group.First());
        return ordered
            .Where(byRecordId.ContainsKey)
            .Select(recordId => byRecordId[recordId])
            .ToList();
    }

    private static Guid RecordIdOf(Prize prize)
    {
        if (prize.RecordId == Guid.Empty)
            ProfileRecordIdentity.EnsureRecordId(prize);
        return prize.RecordId;
    }
}