using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Services.Verification;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Draw;

public sealed record RollCallDrawSnapshot(
    IReadOnlyList<string> ListNames,
    IReadOnlyList<string> Groups,
    IReadOnlyList<string> Genders,
    IReadOnlyList<Student> Candidates,
    IReadOnlyList<Student> Remaining)
{
    public int TotalCount => Candidates.Count;
    public int RemainingCount => Remaining.Count;
}

public sealed record RollCallDrawRequest(string ListName, string Group, string Gender, int Count, string CourseName);

public sealed record RollCallDrawResult(
    IReadOnlyList<Student> Students,
    Guid ProofId,
    string DrawRoundId,
    IReadOnlyDictionary<Guid, double> FrozenWeights);

/// <summary>
/// Shared point-call use case for every UI. Presentation layers own authorization, preview and media.
/// </summary>
public sealed class RollCallDrawService(
    MainConfigHandler configHandler,
    IProfileService profileService,
    IProfileCatalogManager profileCatalogManager,
    IDrawTemporaryRecordService temporaryRecords,
    IDrawCommitService drawCommits,
    VerificationDrawCoordinator verification)
{
    public RollCallDrawSnapshot GetSnapshot(string group, string gender)
    {
        var students = profileService.CurrentStudentList?.Students ?? [];
        var candidates = students.Where(student => DrawCandidateFilter.MatchesScope(student, group, gender))
            .OrderForList().ToArray();
        var threshold = DrawRepeatPolicy.ResolveThreshold(configHandler.Data.RollCallSettings.DrawMode,
            configHandler.Data.RollCallSettings.HalfRepeat);
        var remaining = DrawCandidateFilter.FilterEligibleStudents(candidates, group, gender,
                temporaryRecords.GetStudentCounts(GetListName(), gender, group), threshold)
            .OrderForList().ToArray();
        return new RollCallDrawSnapshot(
            profileCatalogManager.GetStudentListNames(),
            GetScopedValues(student => student.Group),
            GetScopedValues(student => student.Gender),
            candidates,
            remaining);
    }

    public void SwitchList(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, GetListName(), StringComparison.Ordinal))
            return;

        if (configHandler.Data.RollCallSettings.ClearRecord == ClearRecordMode.Restarted)
            temporaryRecords.ClearStudentListOnce(name);
        profileService.LoadStudentProfile(name);
        profileCatalogManager.SetDefaultStudentList(name);
    }

    public async Task<RollCallDrawResult?> DrawAsync(RollCallDrawRequest request, CancellationToken cancellationToken = default)
    {
        SwitchList(request.ListName);
        var snapshot = GetSnapshot(request.Group, request.Gender);
        var count = Math.Clamp(request.Count, 1, snapshot.RemainingCount);
        if (snapshot.RemainingCount == 0)
            return null;

        var outcome = await verification.DrawStudentsAsync(
            count,
            snapshot.Remaining,
            DrawSettingsType.RollCall,
            DrawProofExportContext.ForStudents(GetListName(), request.Group, request.Gender, request.CourseName),
            courseName: request.CourseName,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var weights = outcome.Winners.ToDictionary(student => student, student =>
        {
            ProfileRecordIdentity.EnsureRecordId(student);
            return outcome.FrozenWeights.GetValueOrDefault(student.RecordId, 1d);
        });
        var drawRoundId = drawCommits.CommitStudentDraw(new StudentDrawCommit(
            outcome.Winners,
            DateTime.Now,
            count,
            GetListName(),
            request.Group,
            request.Gender,
            (int)configHandler.Data.RollCallSettings.DrawType,
            weights,
            request.CourseName));
        return new RollCallDrawResult(outcome.Winners, outcome.Proof.ProofId, drawRoundId, outcome.FrozenWeights);
    }

    public void Reset(string group, string gender) => temporaryRecords.ClearStudentScope(GetListName(), gender, group);

    private IReadOnlyList<string> GetScopedValues(Func<Student, string> selector) =>
        (profileService.CurrentStudentList?.Students ?? []).Where(student => student.IsCandidate)
            .Select(selector).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.CurrentCulture).OrderBy(value => value, StringComparer.CurrentCulture).ToArray();

    private string GetListName() => profileService.StudentListConfig?.Name ?? "default";
}

public sealed record LotteryDrawSnapshot(
    IReadOnlyList<string> PrizePoolNames,
    IReadOnlyList<string> StudentListNames,
    IReadOnlyList<string> Groups,
    IReadOnlyList<string> Genders,
    IReadOnlyList<Prize> Prizes,
    IReadOnlyList<Prize> Remaining,
    IReadOnlyList<Student> EligibleStudents)
{
    public int TotalCount { get; init; }
    public int RemainingCount { get; init; }
}

public sealed record LotteryDrawRequest(
    string PrizePoolName,
    string StudentListName,
    string Group,
    string Gender,
    int Count,
    string CourseName);

public sealed record LotteryDrawResult(
    IReadOnlyList<Prize> Prizes,
    IReadOnlyList<Student> AssignedStudents,
    Guid PrizeProofId,
    string DrawRoundId);

/// <summary>
/// Shared lottery use case. It preserves one transactional draw round for prizes and assignments.
/// </summary>
public sealed class LotteryDrawService(
    MainConfigHandler configHandler,
    IProfileService profileService,
    IProfileCatalogManager profileCatalogManager,
    IDrawTemporaryRecordService temporaryRecords,
    IDrawCommitService drawCommits,
    VerificationDrawCoordinator verification)
{
    public LotteryDrawSnapshot GetSnapshot(string studentListName, string group, string gender)
    {
        var prizes = (profileService.CurrentPrizeList?.Prizes ?? []).Where(prize => prize.IsCandidate).ToArray();
        var settings = configHandler.Data.LotterySettings;
        var counts = temporaryRecords.GetPrizeCounts(GetPrizePoolName());
        var remaining = DrawCandidateFilter.FilterEligiblePrizes(prizes, counts, settings.DrawType,
            DrawRepeatPolicy.ResolveThreshold(settings.DrawMode, settings.HalfRepeat)).OrderForList().ToArray();
        var students = GetEligibleStudents(studentListName, group, gender);
        return new LotteryDrawSnapshot(
            profileCatalogManager.GetPrizeListNames(),
            profileCatalogManager.GetStudentListNames(),
            GetStudentScopeValues(student => student.Group),
            GetStudentScopeValues(student => student.Gender),
            prizes,
            remaining,
            students)
        {
            TotalCount = settings.DrawType == LotteryDrawType.Count ? prizes.Sum(prize => Math.Max(0, prize.Count)) : prizes.Length,
            RemainingCount = settings.DrawType == LotteryDrawType.Count
                ? remaining.Sum(prize => Math.Max(0, prize.Count - counts.GetValueOrDefault(ProfileRecordIdentity.EnsureRecordId(prize))))
                : remaining.Length
        };
    }

    public void SwitchPrizePool(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, GetPrizePoolName(), StringComparison.Ordinal))
            return;
        if (configHandler.Data.LotterySettings.ClearRecord == ClearRecordMode.Restarted)
            temporaryRecords.ClearPrizeListOnce(name);
        profileService.LoadPrizeProfile(name);
        profileCatalogManager.SetDefaultPrizePool(name);
    }

    public void SwitchStudentList(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, GetStudentListName(), StringComparison.Ordinal))
            return;
        if (configHandler.Data.RollCallSettings.ClearRecord == ClearRecordMode.Restarted)
            temporaryRecords.ClearStudentListOnce(name);
        profileService.LoadStudentProfile(name);
        profileCatalogManager.SetDefaultStudentList(name);
    }

    public async Task<LotteryDrawResult?> DrawAsync(LotteryDrawRequest request, CancellationToken cancellationToken = default)
    {
        SwitchPrizePool(request.PrizePoolName);
        var hasStudentAssignment = !string.IsNullOrWhiteSpace(request.StudentListName);
        if (hasStudentAssignment)
            SwitchStudentList(request.StudentListName);

        var snapshot = GetSnapshot(request.StudentListName, request.Group, request.Gender);
        if (snapshot.RemainingCount == 0)
            return null;
        var count = Math.Clamp(request.Count, 1, snapshot.RemainingCount);
        if (hasStudentAssignment && snapshot.EligibleStudents.Count < count)
            return null;
        var prizes = await verification.DrawPrizesAsync(count,
            temporaryRecords.GetPrizeCounts(GetPrizePoolName()), snapshot.Prizes,
            DrawProofExportContext.ForPrizes(GetPrizePoolName()), cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Student> assigned = [];
        if (hasStudentAssignment)
        {
            assigned = (await verification.DrawStudentsAsync(count, snapshot.EligibleStudents, DrawSettingsType.RollCall,
                DrawProofExportContext.ForStudents(GetStudentListName(), request.Group, request.Gender, request.CourseName),
                prizes.Proof.ProofId, request.CourseName, cancellationToken).ConfigureAwait(false)).Winners;
            if (assigned.Count != prizes.Winners.Count)
                return null;
        }

        var roundId = drawCommits.CommitLotteryDraw(new LotteryDrawCommit(
            prizes.Winners,
            DateTime.Now,
            count,
            GetPrizePoolName(),
            assigned,
            hasStudentAssignment ? GetStudentListName() : null,
            request.Group,
            request.Gender,
            (int)configHandler.Data.LotterySettings.DrawType,
            (int)configHandler.Data.RollCallSettings.DrawType,
            request.CourseName));
        return new LotteryDrawResult(prizes.Winners, assigned, prizes.Proof.ProofId, roundId);
    }

    public void Reset(string studentListName, string group, string gender)
    {
        temporaryRecords.ClearPrizeList(GetPrizePoolName());
        if (!string.IsNullOrWhiteSpace(studentListName))
            temporaryRecords.ClearStudentScope(GetStudentListName(), gender, group);
    }

    private IReadOnlyList<Student> GetEligibleStudents(string selectedList, string group, string gender)
    {
        if (string.IsNullOrWhiteSpace(selectedList))
            return [];
        var threshold = DrawRepeatPolicy.ResolveThreshold(configHandler.Data.RollCallSettings.DrawMode,
            configHandler.Data.RollCallSettings.HalfRepeat);
        return DrawCandidateFilter.FilterEligibleStudents(profileService.CurrentStudentList?.Students ?? [], group, gender,
            temporaryRecords.GetStudentCounts(GetStudentListName(), gender, group), threshold).ToArray();
    }

    private IReadOnlyList<string> GetStudentScopeValues(Func<Student, string> selector) =>
        (profileService.CurrentStudentList?.Students ?? []).Where(student => student.IsCandidate)
            .Select(selector).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.CurrentCulture).OrderBy(value => value, StringComparer.CurrentCulture).ToArray();

    private string GetPrizePoolName() => profileService.PrizeListConfig?.Name ?? "default";
    private string GetStudentListName() => profileService.StudentListConfig?.Name ?? "default";
}
