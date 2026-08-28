using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

public partial class DrawEngine
{
    internal const double ShareDebtPersonalHorizonRounds = 2.0;
    internal const double ShareDebtDimensionHorizonPerPick = 0.8;
    internal const double ShareDebtRandomFloor = 0.10;

    public List<WeightedCandidate<Student>> CalculateStudentWeight(
        List<Student> candidates,
        IReadOnlyDictionary<Student, History>? historyCacheOverride = null,
        string courseName = "")
    {
        return CalculateStudentWeight(candidates, FairDrawPolicySnapshot.FromConfig(ConfigData.FairDrawSettings), historyCacheOverride, courseName);
    }

    internal List<WeightedCandidate<Student>> CalculateStudentWeightWithMobileDesktopDefaults(
        List<Student> candidates,
        IReadOnlyDictionary<Student, History>? historyCacheOverride = null,
        string courseName = "")
    {
        return CalculateStudentWeight(candidates, FairDrawPolicySnapshot.MobileDesktopDefaultsV1, historyCacheOverride, courseName);
    }

    internal List<WeightedCandidate<Student>> CalculateStudentWeight(
        List<Student> candidates,
        FairDrawPolicySnapshot fairSettings,
        IReadOnlyDictionary<Student, History>? historyCacheOverride = null,
        string courseName = "")
    {
        if (candidates.Count == 0)
            return [];

        var baseWeight = SanitizeFinite(fairSettings.BaseWeight, 1.0);
        var historyCache = historyCacheOverride is null
            ? BuildStudentHistoryCache(candidates, courseName)
            : candidates
                .Where(historyCacheOverride.ContainsKey)
                .ToDictionary(candidate => candidate, candidate => historyCacheOverride[candidate]);
        if (!fairSettings.FairDraw)
            return candidates.Select(s => new WeightedCandidate<Student> { Candidate = s, Weight = baseWeight }).ToList();

        var counts = candidates
            .Select(candidate => Math.Max(0, historyCache.GetValueOrDefault(candidate)?.TotalCount ?? 0))
            .ToArray();
        var totalDraws = counts.Sum();
        var personalHorizon = ShareDebtPersonalHorizonRounds * candidates.Count;
        var personalDebt = counts
            .Select(count => Math.Max((totalDraws + personalHorizon) / candidates.Count - count, 0.0))
            .ToArray();
        var debtWeights = personalDebt.ToArray();

        foreach (var selector in GetEnabledDimensionSelectors(fairSettings))
        {
            var labels = candidates.Select(selector).ToArray();
            var labelMembers = labels
                .GroupBy(label => label ?? string.Empty, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var labelCounts = labels
                .Select((label, index) => (label: label ?? string.Empty, index))
                .GroupBy(item => item.label, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(item => counts[item.index]), StringComparer.Ordinal);
            var horizon = ShareDebtDimensionHorizonPerPick * Math.Max(1, fairSettings.BatchSize);
            var dimensionDebt = labelMembers.ToDictionary(
                pair => pair.Key,
                pair => Math.Max(pair.Value / (double)candidates.Count * (totalDraws + horizon)
                                 - labelCounts.GetValueOrDefault(pair.Key), 0.0),
                StringComparer.Ordinal);

            for (var index = 0; index < debtWeights.Length; index++)
                debtWeights[index] *= dimensionDebt[labels[index] ?? string.Empty];
        }
        if (debtWeights.Sum() <= 0.0 && personalDebt.Sum() > 0.0)
            debtWeights = personalDebt;

        var shielded = candidates
            .Select((candidate, index) => historyCache.TryGetValue(candidate, out var history)
                && IsStudentShielded(history.LastDrawnTime, fairSettings))
            .ToArray();
        var activeCount = shielded.Count(value => !value);
        var activeWeightSum = debtWeights
            .Select((weight, index) => shielded[index] ? 0.0 : weight)
            .Sum();

        return candidates.Select((candidate, index) =>
        {
            if (shielded[index] || activeCount == 0)
                return new WeightedCandidate<Student> { Candidate = candidate, Weight = 0.0 };

            var normalized = activeWeightSum > 0.0
                ? debtWeights[index] / activeWeightSum
                : 1.0 / activeCount;
            var probability = (1.0 - ShareDebtRandomFloor) * normalized + ShareDebtRandomFloor / activeCount;
            return new WeightedCandidate<Student> { Candidate = candidate, Weight = probability };
        }).ToList();
    }

    private static IEnumerable<Func<Student, string>> GetEnabledDimensionSelectors(FairDrawPolicySnapshot fairSettings)
    {
        if (fairSettings.FairDrawGroup)
            yield return student => student.Group;
        if (fairSettings.FairDrawGender)
            yield return student => student.Gender;
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
