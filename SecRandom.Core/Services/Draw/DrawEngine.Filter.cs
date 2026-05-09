using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Draw.Exceptions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

public partial class DrawEngine
{
    private List<Student> FilterStudents(Func<Student, bool> filter, int drawCount)
    {
        //先过滤班级，小组，性别
        var filteredList = StudentList.Students.Where(filter).ToList();

        //？没人了？
        if (filteredList.Count == 0)
            throw new CandidateNotFoundException();

        if (drawCount > filteredList.Count)
            throw new RepeatLimitExhaustedException();

        //平均值差值保护
        if (!ConfigData.FairDrawSettings.EnableAvgGapProtection)
            return filteredList;

        var countByStudent = filteredList.ToDictionary(
            s => s,
            GetStudentDrawCount
        );

        var avg = countByStudent.Values.Average();
        var minDrawCount = countByStudent.Values.Min();
        var maxDrawCount = countByStudent.Values.Max();
        var pool = filteredList
            .Where(s => countByStudent[s] <= avg)
            .ToList();

        if (maxDrawCount - minDrawCount > ConfigData.FairDrawSettings.GapThreshold)
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

        var requiredSize = Math.Max(drawCount, ConfigData.FairDrawSettings.MinPoolSize);
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
        if (!string.IsNullOrWhiteSpace(student.Id) &&
            StudentHistory.Students.TryGetValue(student.Id, out var historyById))
            return historyById.TotalCount;

        if (!string.IsNullOrWhiteSpace(student.Name) &&
            StudentHistory.Students.TryGetValue(student.Name, out var historyByName))
            return historyByName.TotalCount;

        return 0;
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

    private List<Prize> FilterPrizes(Func<Prize, bool> filter, int count)
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
                .Where(p => p.Count - GetPrizeDrawCount(p) > 0)
                .ToList();

            if (currentPool.Count == 0)
                throw new RepeatLimitExhaustedException();

            return currentPool;
        }

        var repeatThreshold = GetLotteryRepeatThreshold();
        if (repeatThreshold > 0)
            currentPool = currentPool
                .Where(p => GetPrizeDrawCount(p) < repeatThreshold)
                .ToList();

        if (currentPool.Count == 0 || count > currentPool.Count)
            throw new RepeatLimitExhaustedException();

        return currentPool;
    }

    private int GetPrizeDrawCount(Prize prize)
    {
        if (!string.IsNullOrWhiteSpace(prize.Name) &&
            PrizeHistory.Prizes.TryGetValue(prize.Name, out var historyByName))
            return historyByName.TotalCount;

        return 0;
    }
}