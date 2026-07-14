using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Draw.Exceptions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

public partial class DrawEngine
{
    private List<Student> FilterStudents(
        Func<Student, bool> filter,
        int drawCount,
        IReadOnlyDictionary<Student, History>? historyCacheOverride = null)
    {
        var filteredList = StudentList.Students.Where(filter).ToList();
        var historyCache = historyCacheOverride ?? BuildStudentHistoryCache(filteredList);

        if (filteredList.Count == 0)
            throw new CandidateNotFoundException();

        if (drawCount > filteredList.Count)
            throw new RepeatLimitExhaustedException();

        var fairSettings = ConfigData.FairDrawSettings;
        if (!fairSettings.EnableAvgGapProtection)
            return filteredList;

        var countByStudent = filteredList.ToDictionary(
            s => s,
            s => historyCache.GetValueOrDefault(s)?.TotalCount ?? 0
        );

        var avg = countByStudent.Values.Average();
        var minDrawCount = countByStudent.Values.Min();
        var maxDrawCount = countByStudent.Values.Max();
        var pool = filteredList
            .Where(s => countByStudent[s] <= avg)
            .ToList();

        if (maxDrawCount - minDrawCount > Math.Max(0, fairSettings.GapThreshold))
        {
            var filteredWithoutMax = filteredList
                .Where(s => countByStudent[s] < maxDrawCount)
                .ToList();

            if (filteredWithoutMax.Count > 0)
            {
                var newAvg = filteredWithoutMax.Average(s => countByStudent[s]);
                pool = filteredWithoutMax
                    .Where(s => countByStudent[s] <= newAvg)
                    .ToList();
            }
        }

        var requiredSize = Math.Max(drawCount, Math.Max(1, fairSettings.MinPoolSize));
        if (pool.Count < requiredSize)
        {
            var threshold = (int)Math.Ceiling(avg);
            var expandedPool = ExpandStudentPool(filteredList, countByStudent, requiredSize, threshold, maxDrawCount);

            if (expandedPool.Count < requiredSize)
                expandedPool = filteredList.ToList();

            pool = expandedPool
                .OrderBy(s => countByStudent[s])
                .Take(requiredSize)
                .ToList();
        }

        if (pool.Count == 0 || pool.Count < drawCount)
            throw new RepeatLimitExhaustedException();

        return pool;
    }

    private int GetStudentDrawCount(Student student)
    {
        return BuildStudentHistoryCache([student]).GetValueOrDefault(student)?.TotalCount ?? 0;
    }

    private static List<Student> ExpandStudentPool(
        List<Student> candidates,
        IReadOnlyDictionary<Student, int> countByStudent,
        int requiredSize,
        int initialThreshold,
        int maxDrawCount)
    {
        var threshold = initialThreshold;
        var pool = candidates
            .Where(s => countByStudent[s] <= threshold)
            .ToList();

        while (pool.Count < requiredSize && threshold < maxDrawCount)
        {
            threshold++;
            pool = candidates
                .Where(s => countByStudent[s] <= threshold)
                .ToList();
        }

        return pool;
    }

    private List<Prize> FilterPrizes(
        Func<Prize, bool> filter,
        int count,
        IReadOnlyDictionary<Prize, History> historyCache)
    {
        var currentPool = PrizeList.Prizes
            .Where(p => p.Exists)
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
