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

    public static History? GetStudentHistory(
        StudentHistory history,
        Student student,
        Func<string, bool>? canUseLegacyKey = null)
    {
        ProfileRecordIdentityDiagnostics.StudentPrimaryLookups++;
        var recordId = EnsureRecordId(student);
        if (history.Students.TryGetValue(recordId, out var historyByRecordId))
            return historyByRecordId;

        foreach (var legacyKey in GetLegacyStudentHistoryKeys(student))
        {
            if (canUseLegacyKey?.Invoke(legacyKey) == false)
                continue;

            ProfileRecordIdentityDiagnostics.StudentLegacyLookups++;
            if (!history.Students.TryGetValue(legacyKey, out var legacyHistory))
                continue;

            history.Students[recordId] = legacyHistory;
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
            return historyByRecordId;

        foreach (var legacyKey in GetLegacyPrizeHistoryKeys(prize))
        {
            if (canUseLegacyKey?.Invoke(legacyKey) == false)
                continue;

            ProfileRecordIdentityDiagnostics.PrizeLegacyLookups++;
            if (!history.Prizes.TryGetValue(legacyKey, out var legacyHistory))
                continue;

            history.Prizes[recordId] = legacyHistory;
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
