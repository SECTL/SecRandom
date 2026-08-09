using SecRandom.Core.Enums.Configs;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

/// <summary>
///     Shared candidate filtering for every draw channel. Students: IsCandidate + group/gender scope +
///     temporary-record repeat threshold. Prizes: IsCandidate + Count-mode inventory or Pan-mode repeat
///     threshold. Repeat limits are always evaluated against temporary records, never persistent history.
/// </summary>
public static class DrawCandidateFilter
{
    public static bool MatchesScope(Student student, string groupScope, string genderScope)
    {
        return student.IsCandidate
               && (string.IsNullOrEmpty(groupScope) || student.Group == groupScope)
               && (string.IsNullOrEmpty(genderScope) || student.Gender == genderScope);
    }

    public static List<Student> FilterEligibleStudents(
        IEnumerable<Student> students,
        string groupScope,
        string genderScope,
        IReadOnlyDictionary<string, int> temporaryCounts,
        int repeatThreshold,
        Func<Student, bool>? additionalFilter = null)
    {
        return students
            .Where(student => MatchesScope(student, groupScope, genderScope))
            .Where(student => additionalFilter is null || additionalFilter(student))
            .Where(student => !DrawRepeatPolicy.HasReachedLimit(
                temporaryCounts.GetValueOrDefault(ProfileRecordIdentity.EnsureRecordId(student)),
                repeatThreshold))
            .ToList();
    }

    public static List<Prize> FilterEligiblePrizes(
        IEnumerable<Prize> prizes,
        IReadOnlyDictionary<string, int> temporaryCounts,
        LotteryDrawType drawType,
        int repeatThreshold,
        Func<Prize, bool>? additionalFilter = null)
    {
        var candidates = prizes.Where(prize => prize.IsCandidate);
        if (additionalFilter is not null)
            candidates = candidates.Where(additionalFilter);

        if (drawType == LotteryDrawType.Count)
        {
            return candidates
                .Where(prize => prize.Count - temporaryCounts.GetValueOrDefault(ProfileRecordIdentity.EnsureRecordId(prize)) > 0)
                .ToList();
        }

        return candidates
            .Where(prize => !DrawRepeatPolicy.HasReachedLimit(
                temporaryCounts.GetValueOrDefault(ProfileRecordIdentity.EnsureRecordId(prize)),
                repeatThreshold))
            .ToList();
    }
}
