using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

public partial class DrawEngine
{
    // Kept for the anonymous audit payload shape only: the unified deficit weight no longer uses
    // share debt, dimension horizons, or the uniform random floor.
    internal const double ShareDebtPersonalHorizonRounds = 2.0;
    internal const double ShareDebtDimensionHorizonPerPick = 0.8;
    internal const double ShareDebtRandomFloor = 0.10;

    /// <summary>
    ///     The largest lead a candidate may hold over the least-drawn member, in whole draws. Weight falls by one
    ///     for every draw a candidate is ahead of the minimum, so the least-drawn members always dominate while
    ///     the tier one draw ahead still keeps a real chance. Reaching this lead makes the weight exactly zero,
    ///     which is what keeps a 3-vs-0 history from ever appearing: a member two draws ahead can be drawn (to
    ///     three) only after the minimum has moved to one.
    /// </summary>
    internal const int MaxLeadTiers = 2;

    /// <summary>
    ///     Keeps every positive weight drawable after the fixed-point freeze, so a mathematically tiny weight is
    ///     never rounded into an unusable zero. It never lifts a real zero: exclusion stays exact.
    /// </summary>
    internal const double MinimumPositiveProbability = 1e-6;

    public List<WeightedCandidate<Student>> CalculateStudentWeight(
        List<Student> candidates,
        IReadOnlyDictionary<Student, History>? historyCacheOverride = null,
        string courseName = "",
        string? historyGroup = null)
    {
        return CalculateStudentWeight(candidates, FairDrawPolicySnapshot.FromConfig(ConfigData.FairDrawSettings), historyCacheOverride, courseName, historyGroup);
    }

    internal List<WeightedCandidate<Student>> CalculateStudentWeightWithMobileDesktopDefaults(
        List<Student> candidates,
        IReadOnlyDictionary<Student, History>? historyCacheOverride = null,
        string courseName = "",
        string? historyGroup = null)
    {
        return CalculateStudentWeight(candidates, FairDrawPolicySnapshot.MobileDesktopDefaultsV1, historyCacheOverride, courseName, historyGroup);
    }

    internal List<WeightedCandidate<Student>> CalculateStudentWeight(
        List<Student> candidates,
        FairDrawPolicySnapshot fairSettings,
        IReadOnlyDictionary<Student, History>? historyCacheOverride = null,
        string courseName = "",
        string? historyGroup = null)
    {
        if (candidates.Count == 0)
            return [];

        var baseWeight = SanitizeFinite(fairSettings.BaseWeight, 1.0);
        var historyCache = historyCacheOverride is null
            ? BuildStudentHistoryCache(candidates, courseName, historyGroup)
            : candidates
                .Where(historyCacheOverride.ContainsKey)
                .ToDictionary(candidate => candidate, candidate => historyCacheOverride[candidate]);
        if (!fairSettings.FairDraw)
            return candidates.Select(s => new WeightedCandidate<Student> { Candidate = s, Weight = baseWeight }).ToList();

        var counts = candidates
            .Select(candidate => Math.Max(0, historyCache.GetValueOrDefault(candidate)?.TotalCount ?? 0))
            .ToArray();
        var shielded = candidates
            .Select((candidate, index) => historyCache.TryGetValue(candidate, out var history)
                && IsStudentShielded(history.LastDrawnTime, fairSettings))
            .ToArray();

        return CalculateDeficitWeights(candidates, counts, shielded, fairSettings);
    }

    /// <summary>
    ///     The one weight function shared by both fair-draw modes: a candidate's weight is the distance to the
    ///     average tier (<c>floor(mean) + 0.5</c>), so counts above the average drop to exactly zero while the
    ///     average tier itself keeps half a share. Uniform-first runs it on the average-gap pool and keeps the
    ///     whole pool drawable; random-first runs it on every eligible candidate and only requires the requested
    ///     count, which is how a hard candidate gate becomes a zero probability without changing the math.
    /// </summary>
    private static List<WeightedCandidate<Student>> CalculateDeficitWeights(
        List<Student> candidates,
        int[] counts,
        bool[] shielded,
        FairDrawPolicySnapshot fairSettings)
    {
        var minCount = counts.Length == 0 ? 0 : counts.Min();
        // While somebody is still at zero, cap the lead at one draw: nobody can be drawn a second time before
        // everyone has been drawn once. That first full round is the part a class actually watches; only after
        // it completes does the wider two-draw ceiling apply and the real randomness show up.
        var leadCap = minCount == 0 ? 1 : MaxLeadTiers;
        var ceiling = minCount + leadCap;

        var required = fairSettings.Mode == FairDrawMode.UniformFirst
            ? candidates.Count
            : Math.Clamp(fairSettings.RequiredCount, 1, candidates.Count);
        if (counts.Count(count => count < ceiling) < required)
        {
            var sortedCounts = counts.OrderBy(count => count).ToArray();
            ceiling = Math.Max(ceiling, sortedCounts[Math.Clamp(required, 1, sortedCounts.Length) - 1] + 1);
        }

        var weights = counts
            .Select(count => Math.Max(ceiling - count, 0.0))
            .ToArray();
        var activeWeightSum = weights
            .Select((weight, index) => shielded[index] ? 0.0 : weight)
            .Sum();

        return candidates.Select((candidate, index) =>
        {
            if (shielded[index] || weights[index] <= 0.0)
                return new WeightedCandidate<Student> { Candidate = candidate, Weight = 0.0 };

            var probability = activeWeightSum > 0.0 ? weights[index] / activeWeightSum : 0.0;
            if (probability > 0.0)
                probability = Math.Max(probability, MinimumPositiveProbability);
            return new WeightedCandidate<Student> { Candidate = candidate, Weight = probability };
        }).ToList();
    }

    private bool IsStudentShielded(DateTime lastDrawnTime)
    {
        return IsStudentShielded(lastDrawnTime, FairDrawPolicySnapshot.FromConfig(ConfigData.FairDrawSettings));
    }

    private static bool IsStudentShielded(DateTime lastDrawnTime, FairDrawPolicySnapshot fairSettings)
    {
        if (!fairSettings.ShieldEnabled || fairSettings.ShieldTime <= 0 || lastDrawnTime == DateTime.MinValue)
            return false;

        var shieldDuration = fairSettings.ShieldTimeUnit switch
        {
            ShieldTimeUnit.Seconds => TimeSpan.FromSeconds(fairSettings.ShieldTime),
            ShieldTimeUnit.Minutes => TimeSpan.FromMinutes(fairSettings.ShieldTime),
            ShieldTimeUnit.Hours => TimeSpan.FromHours(fairSettings.ShieldTime),
            _ => TimeSpan.FromMinutes(fairSettings.ShieldTime)
        };

        return DateTime.Now - lastDrawnTime < shieldDuration;
    }

    private static double SanitizeFinite(double value, double fallback)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
    }
}
