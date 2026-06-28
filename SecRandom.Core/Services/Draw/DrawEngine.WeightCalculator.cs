using SecRandom.Core.Models.Draw;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

public partial class DrawEngine
{
    private const double FairBaseWeight = 1.0;
    private const double FairFrequencyWeight = 1.0;
    private const double FairGroupWeight = 0.8;
    private const double FairGenderWeight = 0.8;
    private const double FairTimeWeight = 0.5;
    private const double FairMinWeight = 0.5;
    private const double FairMaxWeight = 5.0;
    private const int FairColdStartRounds = 10;
    private const bool FairGroupEnabled = true;
    private const bool FairGenderEnabled = true;
    private const bool FairTimeEnabled = true;
    private const bool FairColdStartEnabled = true;

    /*
     * 算法来源：
     * https://github.com/SECTL/SecRandom/tree/v2.3.7/app/common/roll_call/roll_call_utils.py L280-L412
     * https://github.com/SECTL/SecRandom/tree/v2.3.7/app/common/history/weight_utils.py L272-L407
     */
    public List<WeightedCandidate<Student>> CalculateStudentWeight(
        List<Student> candidates,
        IReadOnlyDictionary<Student, History>? historyCacheOverride = null)
    {
        if (candidates.Count == 0)
            return [];

        var historyCache = historyCacheOverride ?? BuildStudentHistoryCache(candidates);
        var maxStudentDrawCount = historyCache.Values.Select(history => history.TotalCount).DefaultIfEmpty(0).Max();

        if (!ConfigData.FairDrawSettings.FairDraw)
            return candidates.Select(s => new WeightedCandidate<Student> { Candidate = s, Weight = FairBaseWeight }).ToList();

        List<WeightedCandidate<Student>> calculatedStudentWeight = [];

        foreach (var candidate in candidates)
        {
            var currentCount = historyCache.GetValueOrDefault(candidate)?.TotalCount ?? 0;

            var freqencyIndex = Math.Sqrt((maxStudentDrawCount + 1.0) / (currentCount + 1.0));

            if (FairColdStartEnabled &&
                StudentHistory.TotalStats < FairColdStartRounds)
                freqencyIndex = Math.Max(0.8 + 0.2 * freqencyIndex, freqencyIndex);

            freqencyIndex *= FairFrequencyWeight;

            var groupIndex = 0.0;
            if (FairGroupEnabled)
            {
                var effectiveGroupCount = StudentHistory.GroupStats.Count;
                var effectiveGroupMaxDrawCount = StudentHistory.GroupStats.Values.DefaultIfEmpty(0).Max();
                var currentGroupDrawCount = StudentHistory.GroupStats.GetValueOrDefault(candidate.Group);
                if (effectiveGroupCount > 3)
                {
                    groupIndex = 1.0 / (0.2 * currentGroupDrawCount + 1.0) * FairGroupWeight;
                }
                else
                {
                    if (effectiveGroupMaxDrawCount == 0)
                        groupIndex = 0.2 * FairGroupWeight;
                    else if (currentGroupDrawCount == 0)
                        groupIndex = 0.5 * FairGroupWeight;
                    else
                        groupIndex = FairGroupWeight * (1.0 - currentGroupDrawCount * 1.0 / effectiveGroupMaxDrawCount);
                }
            }

            var genderIndex = 0.0;
            if (FairGenderEnabled)
            {
                var effectiveGenderCount = StudentHistory.GenderStatus.Count;
                var effectiveGenderMaxDrawCount = StudentHistory.GenderStatus.Values.DefaultIfEmpty(0).Max();
                var currentGenderDrawCount = StudentHistory.GenderStatus.GetValueOrDefault(candidate.Gender);
                if (effectiveGenderCount > 3)
                {
                    genderIndex = 1.0 / (0.2 * currentGenderDrawCount + 1.0) * FairGenderWeight;
                }
                else
                {
                    if (effectiveGenderMaxDrawCount == 0)
                        genderIndex = 0.2 * FairGenderWeight;
                    else if (currentGenderDrawCount == 0)
                        genderIndex = 0.5 * FairGenderWeight;
                    else
                        genderIndex = FairGenderWeight * (1.0 - currentGenderDrawCount * 1.0 / effectiveGenderMaxDrawCount);
                }
            }

            var timeIndex = 0.0;
            if (FairTimeEnabled)
            {
                var lastDrawnTime = historyCache.TryGetValue(candidate, out var history)
                    ? history.LastDrawnTime
                    : DateTime.MinValue;
                if (lastDrawnTime != DateTime.MinValue)
                    timeIndex = Math.Min(1, (DateTime.Now - lastDrawnTime).Days / 30.0) * FairTimeWeight;
            }

            var totalIndex = FairBaseWeight + freqencyIndex + groupIndex + genderIndex + timeIndex;
            calculatedStudentWeight.Add(new WeightedCandidate<Student> { Candidate = candidate, Weight = totalIndex });
        }

        calculatedStudentWeight = calculatedStudentWeight.Select(ws => new WeightedCandidate<Student>
        {
            Candidate = ws.Candidate,
            Weight = Math.Round(Math.Max(FairMinWeight / 10.0, Math.Min(FairMaxWeight, ws.Weight)), 2)
        }).ToList();

        return calculatedStudentWeight;
    }

    private DateTime GetStudentLastDrawnTime(Student student)
    {
        return BuildStudentHistoryCache([student]).TryGetValue(student, out var history)
            ? history.LastDrawnTime
            : DateTime.MinValue;
    }

}
