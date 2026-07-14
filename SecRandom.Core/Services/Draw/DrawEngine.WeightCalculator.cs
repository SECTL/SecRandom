using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

public partial class DrawEngine
{
    /*
     * 算法来源：
     * https://github.com/SECTL/SecRandom/tree/v2.3.7/app/common/roll_call/roll_call_utils.py L280-L412
     * https://github.com/SECTL/SecRandom/tree/v2.3.7/app/common/history/weight_utils.py L272-L407
     */
    public List<WeightedCandidate<Student>> CalculateStudentWeight(
        List<Student> candidates,
        IReadOnlyDictionary<Student, History>? historyCacheOverride = null,
        string courseName = "")
    {
        if (candidates.Count == 0)
            return [];

        var fairSettings = ConfigData.FairDrawSettings;
        var baseWeight = SanitizeFinite(fairSettings.BaseWeight, 1.0);
        var historyCache = historyCacheOverride ?? BuildStudentHistoryCache(candidates, courseName);
        IReadOnlyDictionary<string, int> groupStats = string.IsNullOrWhiteSpace(courseName)
            ? StudentHistory.GroupStats
            : BuildCourseBalanceStats(courseName, item => item.RecordGroup);
        IReadOnlyDictionary<string, int> genderStats = string.IsNullOrWhiteSpace(courseName)
            ? StudentHistory.GenderStatus
            : BuildCourseBalanceStats(courseName, item => item.RecordGender);
        var maxStudentDrawCount = historyCache.Values.Select(history => history.TotalCount).DefaultIfEmpty(0).Max();

        if (!fairSettings.FairDraw)
            return candidates.Select(s => new WeightedCandidate<Student> { Candidate = s, Weight = baseWeight }).ToList();

        List<WeightedCandidate<Student>> calculatedStudentWeight = [];

        foreach (var candidate in candidates)
        {
            var currentCount = historyCache.GetValueOrDefault(candidate)?.TotalCount ?? 0;
            var lastDrawnTime = historyCache.TryGetValue(candidate, out var history)
                ? history.LastDrawnTime
                : DateTime.MinValue;

            var frequencyIndex = CalculateFrequencyIndex(
                fairSettings.FrequencyFunction,
                currentCount,
                maxStudentDrawCount);

            if (fairSettings.ColdStartEnabled &&
                StudentHistory.TotalStats < Math.Max(1, fairSettings.ColdStartRounds))
                frequencyIndex = Math.Min(0.8 + frequencyIndex * 0.2, frequencyIndex);

            frequencyIndex *= SanitizeFinite(fairSettings.FrequencyWeight, 1.0);

            var groupIndex = CalculateBalanceIndex(
                fairSettings.FairDrawGroup,
                candidate.Group,
                groupStats,
                fairSettings.GroupWeight);

            var genderIndex = CalculateBalanceIndex(
                fairSettings.FairDrawGender,
                candidate.Gender,
                genderStats,
                fairSettings.GenderWeight);

            var timeIndex = 0.0;
            if (fairSettings.FairDrawTime && lastDrawnTime != DateTime.MinValue)
                timeIndex = Math.Min(1, (DateTime.Now - lastDrawnTime).Days / 30.0) *
                            SanitizeFinite(fairSettings.TimeWeight, 0.5);

            var totalIndex = baseWeight + frequencyIndex + groupIndex + genderIndex + timeIndex;
            if (IsStudentShielded(lastDrawnTime))
                totalIndex = 0;

            calculatedStudentWeight.Add(new WeightedCandidate<Student> { Candidate = candidate, Weight = totalIndex });
        }

        var minWeight = Math.Max(0, SanitizeFinite(fairSettings.MinWeight, 0.5));
        var maxWeight = Math.Max(minWeight, SanitizeFinite(fairSettings.MaxWeight, 5.0));

        return calculatedStudentWeight.Select(ws => new WeightedCandidate<Student>
        {
            Candidate = ws.Candidate,
            Weight = Math.Round(ws.Weight <= 0 ? 0 : Math.Max(minWeight, Math.Min(maxWeight, ws.Weight)), 2)
        }).ToList();
    }

    private DateTime GetStudentLastDrawnTime(Student student)
    {
        return BuildStudentHistoryCache([student]).TryGetValue(student, out var history)
            ? history.LastDrawnTime
            : DateTime.MinValue;
    }

    private Dictionary<string, int> BuildCourseBalanceStats(string courseName, Func<HistoryItem, string> keySelector)
    {
        return StudentHistory.Students.Values
            .SelectMany(history => history.Histories)
            .Where(item => string.Equals(item.CourseName, courseName, StringComparison.Ordinal))
            .Select(keySelector)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .GroupBy(key => key)
            .ToDictionary(group => group.Key, group => group.Count());
    }

    private static double CalculateFrequencyIndex(
        FrequencyFunctionMode functionMode,
        int currentCount,
        int maxCount)
    {
        return functionMode switch
        {
            FrequencyFunctionMode.Linear => (maxCount - currentCount + 1.0) / (maxCount + 1.0),
            FrequencyFunctionMode.Index => maxCount == 0
                ? 1.0
                : Math.Exp((maxCount - currentCount) * 1.0 / maxCount),
            FrequencyFunctionMode.SquareRoot => Math.Sqrt(maxCount + 1.0) / Math.Sqrt(currentCount + 1.0),
            _ => Math.Sqrt(maxCount + 1.0) / Math.Sqrt(currentCount + 1.0)
        };
    }

    private double CalculateBalanceIndex(
        bool enabled,
        string key,
        IReadOnlyDictionary<string, int> stats,
        double configuredWeight)
    {
        if (!enabled)
            return 0.0;

        var weight = SanitizeFinite(configuredWeight, 0.8);
        var effectiveCount = stats.Count;
        var effectiveMaxDrawCount = stats.Values.DefaultIfEmpty(0).Max();
        var currentDrawCount = stats.GetValueOrDefault(key);
        if (effectiveCount > 3)
            return 1.0 / (0.2 * currentDrawCount + 1.0) * weight;

        if (effectiveMaxDrawCount == 0)
            return 0.2 * weight;

        if (currentDrawCount == 0)
            return 0.5 * weight;

        return weight * (1.0 - currentDrawCount * 1.0 / effectiveMaxDrawCount);
    }

    private bool IsStudentShielded(DateTime lastDrawnTime)
    {
        var fairSettings = ConfigData.FairDrawSettings;
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
