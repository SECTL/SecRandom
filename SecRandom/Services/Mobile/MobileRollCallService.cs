using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Views.Mobile;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Mobile;

internal sealed record MobileRollCallSnapshot(
    IReadOnlyList<Student> Candidates,
    IReadOnlyList<Student> Remaining)
{
    internal int TotalCount => Candidates.Count;
    internal int RemainingCount => Remaining.Count;
}

/// <summary>
/// Mobile-only point-call orchestration. Core remains authoritative for filtering,
/// sampling, repeat policy, and transactional commits.
/// </summary>
public sealed class MobileRollCallService(
    MainConfigHandler configHandler,
    IProfileService profileService,
    IProfileCatalogManager profileCatalogManager,
    IDrawTemporaryRecordService temporaryRecordService,
    IDrawCommitService drawCommitService,
    DrawEngine drawEngine)
{
    internal IReadOnlyList<string> GetListNames() => profileCatalogManager.GetStudentListNames();

    internal IReadOnlyList<string> GetGroups() => GetScopedValues(student => student.Group);

    internal IReadOnlyList<string> GetGenders() => GetScopedValues(student => student.Gender);

    internal MobileRollCallSnapshot GetSnapshot(string group, string gender)
    {
        var students = profileService.CurrentStudentList?.Students ?? [];
        var candidates = students
            .Where(student => DrawCandidateFilter.MatchesScope(student, group, gender))
            .OrderForList()
            .ToArray();
        var settings = configHandler.Data.RollCallSettings;
        var threshold = DrawRepeatPolicy.ResolveThreshold(settings.DrawMode, settings.HalfRepeat);
        var counts = temporaryRecordService.GetStudentCounts(GetListName(), gender, group);
        var remaining = DrawCandidateFilter.FilterEligibleStudents(candidates, group, gender, counts, threshold)
            .OrderForList()
            .ToArray();
        return new MobileRollCallSnapshot(candidates, remaining);
    }

    internal DrawResult<Student> Draw(int requestedCount, string group, string gender)
    {
        var candidates = GetSnapshot(group, gender).Remaining;
        if (candidates.Count == 0)
            return new DrawResult<Student> { Status = DrawStatus.NoEligibleCandidates };

        var count = Math.Clamp(requestedCount, 1, candidates.Count);
        var drawType = configHandler.Data.RollCallSettings.DrawType;
        var prepared = drawType == DrawType.Fair
            ? drawEngine.PrepareStudentsForMobileDesktopDefaults(count, candidates, DrawSettingsType.RollCall, DrawType.Fair)
            : null;
        var output = drawType == DrawType.Fair
            ? drawEngine.DrawPreparedStudents(prepared!, count)
            : drawEngine.DrawPreparedStudentsWithMobileDesktopDefaults(
                count,
                candidates,
                DrawSettingsType.RollCall,
                DrawType.Random);
        if (!output.IsSuccess || output.Result.Count == 0)
            return output;

        var weights = drawType == DrawType.Fair
            ? prepared!.WeightedCandidates.ToDictionary(candidate => candidate.Candidate, candidate => candidate.Weight)
            : output.Result.ToDictionary(student => student, _ => 1.0);
        drawCommitService.CommitStudentDraw(new StudentDrawCommit(
            output.Result,
            DateTime.Now,
            count,
            GetListName(),
            group,
            gender,
            (int)drawType,
            weights));
        return output;
    }

    internal void SwitchList(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, GetListName(), StringComparison.Ordinal))
            return;

        if (configHandler.Data.RollCallSettings.ClearRecord == ClearRecordMode.Restarted)
            temporaryRecordService.ClearStudentListOnce(name);
        profileService.LoadStudentProfile(name);
        profileCatalogManager.SetDefaultStudentList(name);
    }

    internal void ClearTemporaryRecords(string group, string gender) =>
        temporaryRecordService.ClearStudentScope(GetListName(), gender, group);

    private IReadOnlyList<string> GetScopedValues(Func<Student, string> selector) =>
        (profileService.CurrentStudentList?.Students ?? [])
        .Where(student => student.IsCandidate)
        .Select(selector)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.CurrentCulture)
        .OrderBy(value => value, StringComparer.CurrentCulture)
        .ToArray();

    private string GetListName() => profileService.StudentListConfig?.Name ?? MobileDefaults.ProfileName;
}
