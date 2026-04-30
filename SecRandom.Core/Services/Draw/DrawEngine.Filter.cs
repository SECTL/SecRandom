using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Draw.Exceptions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

public partial class DrawEngine
{
    private List<Student> filterStudents(Func<Student, bool> filter, int drawCount)
    {
        //先过滤班级，小组，性别
        var filteredList = studentList.Students.Where(filter).ToList();

        //？没人了？
        if (filteredList.Count == 0)
            throw new CandidateNotFoundException();

        if (drawCount > filteredList.Count)
            throw new RepeatLimitExhaustedException();

        //平均值差值保护
        if (!configData.FairDrawSettings.EnableAvgGapProtection)
            return filteredList;

        var countByStudent = filteredList.ToDictionary(
            s => s,
            getStudentDrawCount
        );

        double avg = countByStudent.Values.Average();
        int minDrawCount = countByStudent.Values.Min();
        int maxDrawCount = countByStudent.Values.Max();
        var pool = filteredList
            .Where(s => countByStudent[s] <= avg)
            .ToList();

        if (maxDrawCount - minDrawCount > configData.FairDrawSettings.GapThreshold)
        {
            var filteredWithoutMax = filteredList
                .Where(s => countByStudent[s] < maxDrawCount)
                .ToList();

            if (filteredWithoutMax.Count > 0)
            {
                double newAvg = filteredWithoutMax.Average(s => countByStudent[s]);
                pool = filteredWithoutMax
                    .Where(s => countByStudent[s] <= newAvg)
                    .ToList();
            }
        }

        int requiredSize = Math.Max(drawCount, configData.FairDrawSettings.MinPoolSize);
        if (pool.Count < requiredSize)
        {
            int threshold = (int)Math.Ceiling(avg);
            var expandedPool = expandStudentPool(filteredList, countByStudent, requiredSize, threshold, maxDrawCount);

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

    private int getStudentDrawCount(Student student)
    {
        if (!string.IsNullOrWhiteSpace(student.Id) &&
            studentHistory.Students.TryGetValue(student.Id, out var historyById))
            return historyById.TotalCount;

        if (!string.IsNullOrWhiteSpace(student.Name) &&
            studentHistory.Students.TryGetValue(student.Name, out var historyByName))
            return historyByName.TotalCount;

        return 0;
    }

    private static List<Student> expandStudentPool(
        List<Student> candidates,
        IReadOnlyDictionary<Student, int> countByStudent,
        int requiredSize,
        int initialThreshold,
        int maxDrawCount)
    {
        int threshold = initialThreshold;
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

    private List<Prize> filterPrizes(Func<Prize, bool> filter, int count)
    {
        var currentPool = prizeList.Prizes
            .Where(p => p.Exists)
            .Where(filter)
            .ToList();

        if (currentPool.Count == 0)
            throw new CandidateNotFoundException();

        if (configData.LotterySettings.DrawType == LotteryDrawType.Count)
        {
            currentPool = currentPool
                .Where(p => p.Count - getPrizeDrawCount(p) > 0)
                .ToList();

            if (currentPool.Count == 0)
                throw new RepeatLimitExhaustedException();

            return currentPool;
        }

        var repeatThreshold = getLotteryRepeatThreshold();
        if (repeatThreshold > 0)
        {
            currentPool = currentPool
                .Where(p => getPrizeDrawCount(p) < repeatThreshold)
                .ToList();
        }

        if (currentPool.Count == 0 || count > currentPool.Count)
            throw new RepeatLimitExhaustedException();

        return currentPool;

    }

    private int getPrizeDrawCount(Prize prize)
    {
        if (!string.IsNullOrWhiteSpace(prize.Name) &&
            prizeHistory.Prizes.TryGetValue(prize.Name, out var historyByName))
            return historyByName.TotalCount;

        return 0;
    }
}
