using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Draw.Exceptions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

public partial class DrawEngine
{
    internal const int AverageGapProtectionThreshold = 1;

    private List<Student> FilterStudents(
        Func<Student, bool> filter,
        int drawCount,
        IReadOnlyDictionary<Student, History>? historyCacheOverride = null,
        bool applyAverageGapProtection = false)
    {
        var filteredList = StudentList.Students.Where(filter).ToList();
        var historyCache = historyCacheOverride ?? BuildStudentHistoryCache(filteredList);

        return ApplyAverageGapProtection(filteredList, drawCount, historyCache, applyAverageGapProtection);
    }

    private List<Student> FilterStudents(
        Func<Student, bool> filter,
        int drawCount,
        IReadOnlyDictionary<Student, History>? historyCacheOverride,
        StudentDrawExecutionPolicy executionPolicy)
    {
        var filteredList = StudentList.Students.Where(filter).ToList();
        var historyCache = historyCacheOverride ?? BuildStudentHistoryCache(filteredList);

        return ApplyAverageGapProtection(filteredList, drawCount, historyCache, executionPolicy);
    }

    private List<Student> FilterPreparedStudents(
        IReadOnlyCollection<Student> candidates,
        int drawCount,
        IReadOnlyDictionary<Student, History>? historyCacheOverride = null,
        bool applyAverageGapProtection = false)
    {
        var filteredList = candidates.Where(student => student.IsCandidate).ToList();
        var historyCache = historyCacheOverride ?? BuildStudentHistoryCache(filteredList);

        return ApplyAverageGapProtection(filteredList, drawCount, historyCache, applyAverageGapProtection);
    }

    private List<Student> FilterPreparedStudents(
        IReadOnlyCollection<Student> candidates,
        int drawCount,
        IReadOnlyDictionary<Student, History>? historyCacheOverride,
        StudentDrawExecutionPolicy executionPolicy)
    {
        var filteredList = candidates.Where(student => student.IsCandidate).ToList();
        var historyCache = historyCacheOverride ?? BuildStudentHistoryCache(filteredList);

        return ApplyAverageGapProtection(filteredList, drawCount, historyCache, executionPolicy);
    }

    private List<Student> ApplyAverageGapProtection(
        List<Student> filteredList,
        int drawCount,
        IReadOnlyDictionary<Student, History> historyCache,
        bool applyAverageGapProtection)
    {
        return ApplyAverageGapProtection(
            filteredList,
            drawCount,
            historyCache,
            new StudentDrawExecutionPolicy(
                "LegacyImplicit",
                0,
                applyAverageGapProtection ? DrawType.Fair : DrawType.Random,
                FairDrawPolicySnapshot.FromConfig(ConfigData.FairDrawSettings)));
    }

    private static List<Student> ApplyAverageGapProtection(
        List<Student> filteredList,
        int drawCount,
        IReadOnlyDictionary<Student, History> historyCache,
        StudentDrawExecutionPolicy executionPolicy)
    {
        if (filteredList.Count == 0)
            throw new CandidateNotFoundException();

        if (drawCount > filteredList.Count)
            throw new RepeatLimitExhaustedException();

        // Average-gap protection is intentionally mandatory for fair draws. It is
        // the hard candidate gate; the fair algorithm only ranks this pool later.
        if (executionPolicy.DrawType != DrawType.Fair)
            return filteredList;

        var countByStudent = filteredList.ToDictionary(
            s => s,
            s => historyCache.GetValueOrDefault(s)?.TotalCount ?? 0
        );

        var avg = countByStudent.Values.Average();
        var minDrawCount = countByStudent.Values.Min();
        var maxDrawCount = countByStudent.Values.Max();
        var maxEligibleDrawCount = avg;
        if (maxDrawCount - minDrawCount > AverageGapProtectionThreshold)
            maxEligibleDrawCount = Math.Min(maxEligibleDrawCount, minDrawCount + AverageGapProtectionThreshold);

        var pool = filteredList
            .Where(s => countByStudent[s] <= maxEligibleDrawCount)
            .ToList();

        if (pool.Count < drawCount)
        {
            foreach (var level in filteredList
                         .Where(student => !pool.Contains(student))
                         .GroupBy(student => countByStudent[student])
                         .OrderBy(group => group.Key))
            {
                pool.AddRange(level);
                if (pool.Count >= drawCount)
                    break;
            }
        }

        if (pool.Count == 0 || pool.Count < drawCount)
            throw new RepeatLimitExhaustedException();

        return pool;
    }

    private int GetStudentDrawCount(Student student)
    {
        return BuildStudentHistoryCache([student]).GetValueOrDefault(student)?.TotalCount ?? 0;
    }

    private List<Prize> FilterPrizes(
        Func<Prize, bool> filter,
        int count,
        IReadOnlyDictionary<Prize, History> historyCache)
    {
        var currentPool = PrizeList.Prizes
            .Where(p => p.IsCandidate)
            .Where(filter)
            .ToList();

        if (currentPool.Count == 0)
            throw new CandidateNotFoundException();

        if (ConfigData.LotterySettings.DrawType == LotteryDrawType.Count)
        {
            currentPool = currentPool
                .Where(p => p.Count - (historyCache.GetValueOrDefault(p)?.TotalCount ?? 0) > 0)
                .ToList();

            if (currentPool.Count == 0)
                throw new RepeatLimitExhaustedException();

            return currentPool;
        }

        var repeatThreshold = GetLotteryRepeatThreshold();
        if (repeatThreshold > 0)
            currentPool = currentPool
                .Where(p => (historyCache.GetValueOrDefault(p)?.TotalCount ?? 0) < repeatThreshold)
                .ToList();

        if (currentPool.Count == 0 || count > currentPool.Count)
            throw new RepeatLimitExhaustedException();

        return currentPool;
    }

    private int GetPrizeDrawCount(Prize prize)
    {
        return BuildPrizeHistoryCache([prize]).GetValueOrDefault(prize)?.TotalCount ?? 0;
    }

    private Dictionary<Student, History> BuildStudentHistoryCache(IEnumerable<Student> candidates, string courseName = "")
    {
        var legacyKeySet = BuildUniqueStudentLegacyKeySet();
        Dictionary<Student, History> cache = new();
        foreach (var candidate in candidates)
        {
            var history = ProfileRecordIdentity.GetStudentHistory(
                StudentHistory,
                candidate,
                legacyKeySet.Contains);
            if (history is not null)
                cache[candidate] = string.IsNullOrWhiteSpace(courseName)
                    ? history
                    : FilterHistoryForCourse(history, courseName);
        }

        return cache;
    }

    private static History FilterHistoryForCourse(History history, string courseName)
    {
        var matches = history.Histories
            .Where(item => string.Equals(item.CourseName, courseName, StringComparison.Ordinal))
            .OrderBy(item => item.DrawTime)
            .ToArray();
        History result = new()
        {
            TotalCount = matches.Length,
            LastDrawnTime = matches.LastOrDefault()?.DrawTime ?? DateTime.MinValue
        };
        foreach (var item in matches)
            result.Histories.Add(item);
        return result;
    }

    private Dictionary<Prize, History> BuildPrizeHistoryCache(IEnumerable<Prize> candidates)
    {
        var legacyKeySet = BuildUniquePrizeLegacyKeySet();
        Dictionary<Prize, History> cache = new();
        foreach (var candidate in candidates)
        {
            var history = ProfileRecordIdentity.GetPrizeHistory(
                PrizeHistory,
                candidate,
                legacyKeySet.Contains);
            if (history is not null)
                cache[candidate] = history;
        }

        return cache;
    }

    private HashSet<string> BuildUniqueStudentLegacyKeySet()
    {
        return ProfileRecordIdentity.BuildUniqueStudentLegacyKeySet(StudentList.Students);
    }

    private HashSet<string> BuildUniquePrizeLegacyKeySet()
    {
        return ProfileRecordIdentity.BuildUniquePrizeLegacyKeySet(PrizeList.Prizes);
    }
}
