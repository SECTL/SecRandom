using SecRandom.Core.Interfaces;
using SecRandom.Core.Models.Orchestration;

namespace SecRandom.Core.Services.Orchestration;

/// <summary>
///     Turns one round's five lists into the concrete draw order for the still-undrawn participants.
/// </summary>
/// <remarks>
///     <para>
///         A round is a <b>queue</b>: whoever comes out leaves the queue and everyone behind moves up one place.
///         That is why 「顺序」 is the round's lineup and not a set of absolute slots — a member scheduled third
///         comes out third (or right after whoever of the first two was postponed), instead of being re-seated
///         third inside the shrinking remainder and therefore never reached by a one-person draw.
///     </para>
///     <para>Resolution order, honouring the rule switches:</para>
///     <list type="number">
///         <item>排斥 (<c>Exclude</c>) removes the member from every round; 延后 (<c>Defer</c>) removes it from
///         this round only; already-drawn members are removed as well;</item>
///         <item>必中 (<c>Guarantee</c>) puts this round's guaranteed members in front — internally ordered by the
///         effective weight and then by the order they were added, so a weight still decides who of them goes
///         first;</item>
///         <item>顺序 (<c>Order</c>) puts the round's queue in front, in queue order; when both switches are off
///         nothing jumps ahead;</item>
///         <item>the rest is a weighted fill over the positive effective weights (weighted sampling without
///         replacement), and a zero weight is <b>not</b> a removal: it merely moves the member behind the weighted
///         ones, most-drawn first, so a round always seats everyone.</item>
///     </list>
///     <para>
///         必中 and 顺序 are mutually exclusive at the switch level, so at most one of the two front groups is
///         populated. If a hand-edited file still carries both, 必中 wins and the queue seats the remainder —
///         nobody is ever seated twice.
///     </para>
/// </remarks>
public static class OrchestrationOrdering
{
    public static IReadOnlyList<Guid> Resolve(
        IReadOnlyList<OrchestrationCandidate> candidates,
        OrchestrationRound round,
        IReadOnlySet<Guid> alreadyDrawn,
        IRandomSource random,
        OrchestrationRules rules)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(alreadyDrawn);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(rules);

        var skipped = rules.Defer ? round.Skipped.ToHashSet() : [];
        var eligible = candidates
            .Where(candidate => rules.Exclude is false || !candidate.IsExcluded)
            .Where(candidate => !skipped.Contains(candidate.RecordId))
            .Where(candidate => !alreadyDrawn.Contains(candidate.RecordId))
            .GroupBy(candidate => candidate.RecordId)
            .Select(group => group.First())
            .ToList();

        var byRecordId = eligible.ToDictionary(candidate => candidate.RecordId);

        List<Guid> front;
        if (rules.Guarantee && round.Guaranteed.Count > 0)
        {
            front = round.Guaranteed
                .Where(byRecordId.ContainsKey)
                .Distinct()
                .OrderByDescending(recordId => EffectiveWeight(byRecordId[recordId], round, rules))
                .ToList();
            // OrderByDescending 是稳定排序，权重相同时保持必中名单里的原始次序。
        }
        else if (rules.Order && round.Order.Count > 0)
        {
            front = round.Order.Where(byRecordId.ContainsKey).Distinct().ToList();
        }
        else
        {
            front = [];
        }

        var placed = front.ToHashSet();
        var free = eligible.Where(candidate => !placed.Contains(candidate.RecordId)).ToList();

        // 每轮手动权重是乘数，不改变零集：权重 0 仍然只是"排到后段"，而不是把人踢出去。
        var weighted = free.Where(candidate => EffectiveWeight(candidate, round, rules) > 0).ToList();
        var deferred = free.Where(candidate => EffectiveWeight(candidate, round, rules) <= 0).ToList();
        deferred.Sort((left, right) => left.DrawCount != right.DrawCount
            ? right.DrawCount.CompareTo(left.DrawCount)
            : left.LastDrawn.CompareTo(right.LastDrawn));

        return front
            .Concat(WeightedOrder(weighted, round, rules, random))
            .Concat(deferred.Select(candidate => candidate.RecordId))
            .ToList();
    }

    /// <summary>
    ///     每轮手动权重只是原始权重的乘数（默认 1，钳在 0..100）；它不参与"谁被排除"的判定，关掉权重名单后
    ///     手动值仍留在数据里但完全不参与排序。
    /// </summary>
    private static double EffectiveWeight(
        OrchestrationCandidate candidate,
        OrchestrationRound round,
        OrchestrationRules rules)
    {
        var manual = rules.Weight && round.Weights.TryGetValue(candidate.RecordId, out var configured)
            ? configured
            : 1d;
        return Math.Max(0d, candidate.SamplingWeight) * Math.Clamp(manual, 0d, 100d);
    }

    private static IEnumerable<Guid> WeightedOrder(
        List<OrchestrationCandidate> pool,
        OrchestrationRound round,
        OrchestrationRules rules,
        IRandomSource random)
    {
        var remaining = pool.ToList();
        var weights = pool.Select(candidate => EffectiveWeight(candidate, round, rules)).ToList();
        for (var drawn = 0; drawn < pool.Count; drawn++)
        {
            var total = weights.Sum();
            var target = random.NextDouble() * total;
            var index = 0;
            while (index < remaining.Count - 1 && target >= weights[index])
            {
                target -= weights[index];
                index++;
            }

            yield return remaining[index].RecordId;
            remaining.RemoveAt(index);
            weights.RemoveAt(index);
        }
    }
}