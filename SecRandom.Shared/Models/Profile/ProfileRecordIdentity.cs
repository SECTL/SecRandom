using System.Threading;

namespace SecRandom.Shared.Models.Profile;

public static class ProfileRecordIdentity
{
    public static string EnsureRecordId(Student student)
    {
        if (student.RecordId == Guid.Empty)
            student.RecordId = CreateRecordId();

        return FormatRecordId(student.RecordId);
    }

    public static string EnsureRecordId(Prize prize)
    {
        if (prize.RecordId == Guid.Empty)
            prize.RecordId = CreateRecordId();

        return FormatRecordId(prize.RecordId);
    }

    public static bool Normalize(StudentList list)
    {
        var changed = false;
        HashSet<string> usedRecordIds = [];
        foreach (var student in list.Students)
            changed |= EnsureUniqueRecordId(student, usedRecordIds);

        return changed;
    }

    public static bool Normalize(PrizeList list)
    {
        var changed = false;
        HashSet<string> usedRecordIds = [];
        foreach (var prize in list.Prizes)
            changed |= EnsureUniqueRecordId(prize, usedRecordIds);

        return changed;
    }

    public static IEnumerable<string> GetLegacyStudentHistoryKeys(Student student)
    {
        foreach (var key in YieldDistinct(student.Id, student.Name))
            yield return key;
    }

    public static IEnumerable<string> GetLegacyPrizeHistoryKeys(Prize prize)
    {
        foreach (var key in YieldDistinct(prize.Id, prize.Name))
            yield return key;
    }

    public static HashSet<string> BuildUniqueStudentLegacyKeySet(IEnumerable<Student> students)
    {
        return BuildUniqueLegacyKeySet(students.Select(GetLegacyStudentHistoryKeys));
    }

    public static HashSet<string> BuildUniquePrizeLegacyKeySet(IEnumerable<Prize> prizes)
    {
        return BuildUniqueLegacyKeySet(prizes.Select(GetLegacyPrizeHistoryKeys));
    }

    public static History? GetStudentHistory(
        StudentHistory history,
        Student student,
        Func<string, bool>? canUseLegacyKey = null)
    {
        ProfileRecordIdentityDiagnostics.StudentPrimaryLookups++;
        var recordId = EnsureRecordId(student);
        if (history.Students.TryGetValue(recordId, out var historyByRecordId))
        {
            MergeCompactRecordIdHistory(history.Students, recordId, historyByRecordId);
            return historyByRecordId;
        }

        if (TryMigrateCompactRecordIdHistory(history.Students, recordId, out var historyByCompactRecordId))
            return historyByCompactRecordId;

        foreach (var legacyKey in GetLegacyStudentHistoryKeys(student))
        {
            if (canUseLegacyKey?.Invoke(legacyKey) == false)
                continue;

            ProfileRecordIdentityDiagnostics.StudentLegacyLookups++;
            if (!history.Students.TryGetValue(legacyKey, out var legacyHistory))
                continue;

            history.Students[recordId] = legacyHistory;
            history.Students.Remove(legacyKey);
            return legacyHistory;
        }

        return null;
    }

    public static History? GetPrizeHistory(
        PrizeHistory history,
        Prize prize,
        Func<string, bool>? canUseLegacyKey = null)
    {
        ProfileRecordIdentityDiagnostics.PrizePrimaryLookups++;
        var recordId = EnsureRecordId(prize);
        if (history.Prizes.TryGetValue(recordId, out var historyByRecordId))
        {
            MergeCompactRecordIdHistory(history.Prizes, recordId, historyByRecordId);
            return historyByRecordId;
        }

        if (TryMigrateCompactRecordIdHistory(history.Prizes, recordId, out var historyByCompactRecordId))
            return historyByCompactRecordId;

        foreach (var legacyKey in GetLegacyPrizeHistoryKeys(prize))
        {
            if (canUseLegacyKey?.Invoke(legacyKey) == false)
                continue;

            ProfileRecordIdentityDiagnostics.PrizeLegacyLookups++;
            if (!history.Prizes.TryGetValue(legacyKey, out var legacyHistory))
                continue;

            history.Prizes[recordId] = legacyHistory;
            history.Prizes.Remove(legacyKey);
            return legacyHistory;
        }

        return null;
    }

    private static bool EnsureUniqueRecordId(Student student, ISet<string> usedRecordIds)
    {
        var originalRecordId = student.RecordId;
        while (student.RecordId == Guid.Empty || !usedRecordIds.Add(FormatRecordId(student.RecordId)))
            student.RecordId = CreateRecordId();

        return student.RecordId != originalRecordId;
    }

    private static bool EnsureUniqueRecordId(Prize prize, ISet<string> usedRecordIds)
    {
        var originalRecordId = prize.RecordId;
        while (prize.RecordId == Guid.Empty || !usedRecordIds.Add(FormatRecordId(prize.RecordId)))
            prize.RecordId = CreateRecordId();

        return prize.RecordId != originalRecordId;
    }

    private static IEnumerable<string> YieldDistinct(params string[] keys)
    {
        HashSet<string> yielded = [];
        foreach (var key in keys.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (yielded.Add(key))
                yield return key;
            }
    }

    private static HashSet<string> BuildUniqueLegacyKeySet(IEnumerable<IEnumerable<string>> keySets)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (var key in keySets.SelectMany(keys => keys))
            counts[key] = counts.GetValueOrDefault(key) + 1;

        return counts
            .Where(pair => pair.Value == 1)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool TryMigrateCompactRecordIdHistory(
        IDictionary<string, History> histories,
        string recordId,
        out History history)
    {
        var compactRecordId = GetCompactRecordId(recordId);
        if (!histories.TryGetValue(compactRecordId, out var compactHistory))
        {
            history = null!;
            return false;
        }

        history = compactHistory;
        histories[recordId] = history;
        histories.Remove(compactRecordId);
        return true;
    }

    private static void MergeCompactRecordIdHistory(
        IDictionary<string, History> histories,
        string recordId,
        History canonicalHistory)
    {
        var compactRecordId = GetCompactRecordId(recordId);
        if (!histories.TryGetValue(compactRecordId, out var compactHistory))
            return;

        histories.Remove(compactRecordId);
        if (ReferenceEquals(canonicalHistory, compactHistory))
            return;

        canonicalHistory.TotalCount += compactHistory.TotalCount;
        canonicalHistory.RoundsMissed = Math.Max(canonicalHistory.RoundsMissed, compactHistory.RoundsMissed);
        canonicalHistory.LastDrawnTime = canonicalHistory.LastDrawnTime >= compactHistory.LastDrawnTime
            ? canonicalHistory.LastDrawnTime
            : compactHistory.LastDrawnTime;
        foreach (var item in compactHistory.Histories)
            canonicalHistory.Histories.Add(item);
    }

    private static string GetCompactRecordId(string recordId)
    {
        return Guid.TryParse(recordId, out var parsedRecordId)
            ? parsedRecordId.ToString("N")
            : recordId;
    }

    private static Guid CreateRecordId()
    {
        return Guid.NewGuid();
    }

    private static string FormatRecordId(Guid recordId)
    {
        return recordId.ToString("D");
    }
}

public static class ProfileRecordIdentityDiagnostics
{
    public static long StudentPrimaryLookups;
    public static long StudentLegacyLookups;
    public static long PrizePrimaryLookups;
    public static long PrizeLegacyLookups;

    public static void Reset()
    {
        Interlocked.Exchange(ref StudentPrimaryLookups, 0);
        Interlocked.Exchange(ref StudentLegacyLookups, 0);
        Interlocked.Exchange(ref PrizePrimaryLookups, 0);
        Interlocked.Exchange(ref PrizeLegacyLookups, 0);
    }
}
