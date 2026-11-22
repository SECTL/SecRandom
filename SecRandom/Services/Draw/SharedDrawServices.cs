using System.Globalization;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Abstraction.Services.Messaging;
using SecRandom.Core.Abstraction.Services.Pipeline;
using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Interfaces;
using SecRandom.Core.Models.Orchestration;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services.Orchestration;
using SecRandom.Core.Services.Profiles;
using SecRandom.Core.Services.Stats;
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

public sealed record RollCallDrawRequest(string ListName, string Group, string Gender, int Count, string CourseName,
    string? HistoryGroup = null, bool DrawGroups = false);

public sealed record RollCallDrawResult(
    IReadOnlyList<Student> Students,
    Guid ProofId,
    string DrawRoundId,
    IReadOnlyDictionary<Guid, double> FrozenWeights,
    string? Note = null,
    bool SuppressPresentation = false);

/// <summary>One "no roster" number draw for the roll-call page (inclusive integer range).</summary>
public sealed record RollCallNumberDrawRequest(
    int RangeStart,
    int RangeEnd,
    int Count,
    string CourseName,
    string? HistoryGroup = null);

public sealed record RollCallNumberDrawResult(
    IReadOnlyList<Student> Numbers,
    Guid ProofId,
    string DrawRoundId,
    IReadOnlyDictionary<Guid, double> FrozenWeights,
    string? Note = null,
    bool SuppressPresentation = false);

public sealed record RollCallNumberSnapshot(int RangeStart, int RangeEnd, IReadOnlyList<Student> Remaining)
{
    public int TotalCount => RangeEnd >= RangeStart ? RangeEnd - RangeStart + 1 : 0;
    public int RemainingCount => Remaining.Count;
}

/// <summary>
///     解析「这条历史记录该盖哪个组标记」。
/// </summary>
/// <remarks>
///     读取范围（<c>HistoryGroup</c>）和写入归属（<c>RecordGroup</c>）是两件事：读取范围为空表示“不按组筛历史”，
///     写入则必须落在本抽取通道自己配置的默认记录组上。否则隐藏选择器时，点名、闪抽、抽奖、编排的记录会
///     一起挤进默认记录组，重新打开就再也拆不回各自的组。组目录读不到时保留配置名——组名只是标记，把记录
///     静默挪进默认组是不可逆的信息丢失。
/// </remarks>
internal static class HistoryRecordGroup
{
    public static string Resolve(
        IHistoryGroupCatalog? catalog,
        ILogger logger,
        HistoryGroupKind kind,
        string configuredGroup,
        string? readScope)
    {
        if (readScope is not null)
            return readScope;

        IReadOnlyList<string>? names = null;
        if (catalog is not null)
        {
            try
            {
                names = catalog.GetGroupNames(kind);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "读取历史记录组目录失败，本次按配置名写入记录组。");
            }
        }

        return HistoryGroupScope.ResolveRecordGroup(configuredGroup, names);
    }
}

/// <summary>
/// Shared point-call use case for every UI. Presentation layers own authorization, preview and media.
/// </summary>
public sealed class RollCallDrawService(
    MainConfigHandler configHandler,
    IProfileService profileService,
    IProfileCatalogManager profileCatalogManager,
    IDrawTemporaryRecordService temporaryRecords,
    IDrawCommitService drawCommits,
    VerificationDrawCoordinator verification,
    PlatformUsageReportService usageReport,
    ILogger<RollCallDrawService> logger,
    IDrawPipelineService? pipeline = null,
    IPluginEventBus? eventBus = null,
    IRandomNumberStatisticsService? randomNumberStatistics = null,
    IHistoryGroupCatalog? historyGroupCatalog = null,
    IOrchestrationStore? orchestrationStore = null,
    OrchestrationScheduler? orchestrationScheduler = null,
    IRandomSource? randomSource = null)
{
    private static readonly CryptoRandomSource FallbackRandom = new();

    public RollCallDrawSnapshot GetSnapshot(string group, string gender)
    {
        var students = profileService.CurrentStudentList?.Students ?? [];
        var candidates = students.Where(student => DrawCandidateFilter.MatchesScope(student, group, gender))
            .OrderForList().ToArray();
        // 插件候选集过滤器在"谁可能被抽到"这一层生效：预览与真正抽签共用同一份候选集，
        // 抽取证明与历史因此也只覆盖过滤后的范围。
        candidates = [.. ApplyCandidateFilters(candidates, group, gender)];
        var threshold = DrawRepeatPolicy.ResolveThreshold(configHandler.Data.EffectiveRollCallSettings.DrawMode,
            configHandler.Data.EffectiveRollCallSettings.HalfRepeat);
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

        if (configHandler.Data.EffectiveRollCallSettings.ClearRecord == ClearRecordMode.Restarted)
            temporaryRecords.ClearStudentListOnce(name);
        profileService.LoadStudentProfile(name);
        profileCatalogManager.SetDefaultStudentList(name);
    }

    public async Task<RollCallDrawResult?> DrawAsync(RollCallDrawRequest request, CancellationToken cancellationToken = default)
    {
        SwitchList(request.ListName);
        if (request.DrawGroups)
            return await DrawGroupsAsync(request, cancellationToken).ConfigureAwait(false);
        var snapshot = GetSnapshot(request.Group, request.Gender);
        var count = Math.Clamp(request.Count, 1, snapshot.RemainingCount);
        if (snapshot.RemainingCount == 0)
            return null;

        // 编排抽取不抽样：本轮的顺序事先排定，抽取只是按序取人。没有可用编排名单时静默回退随机抽取，
        // 因为主页面按要求不暴露编排状态，一个缺失的名单不该把点名按钮变成死键。
        var plan = configHandler.Data.EffectiveRollCallSettings.DrawMode == DrawMode.Orchestrated
            ? PrepareOrchestration(request, snapshot)
            : null;
        VerificationDrawOutcome<Student>? outcome;
        if (plan is not null)
        {
            if (plan.Order.Count == 0)
            {
                if (plan.CanAdvanceWhenEmpty)
                    AdvanceOrchestration(plan, request, 0);
                return null;
            }
            // Exclude/defer rules may leave fewer drawable members than the requested count.
            // Keep the proof, commit metadata, and event count aligned with the actual plan prefix.
            count = Math.Min(count, plan.Order.Count);
            outcome = await verification.DrawOrchestratedStudentsAsync(
                plan.Order.Take(count).ToArray(),
                plan.Round,
                plan.RoundCount,
                DrawProofExportContext.ForStudents(GetListName(), request.Group, request.Gender, request.CourseName),
                courseName: request.CourseName,
                historyGroup: request.HistoryGroup,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            outcome = await verification.DrawStudentsAsync(
                count,
                snapshot.Remaining,
                DrawSettingsType.RollCall,
                DrawProofExportContext.ForStudents(GetListName(), request.Group, request.Gender, request.CourseName),
                courseName: request.CourseName,
                historyGroup: request.HistoryGroup,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        // Every remaining member can be temporarily excluded (post-draw shield), so nothing is drawable yet.
        if (outcome is null)
            return null;

        // 插件结果后处理器：可以批注这次抽签、决定要不要写历史、要不要让宿主跳过自己的呈现动画。
        // 抽中的名单本身不允许改——抽取证明已经生成，改写会破坏凭证与审计链。
        var edit = pipeline?.ApplyPostProcessors(
            BuildRollCallContext(request.Group, request.Gender, request.CourseName, count),
            new DrawResultEdit(outcome.Winners, []));
        if (edit is not null && !edit.Students.SequenceEqual(outcome.Winners))
        {
            logger.LogWarning("插件结果后处理器试图改写已抽中的名单，为保护抽取证明与审计链，本次改写被忽略。");
            edit = edit with { Students = outcome.Winners, Prizes = [] };
        }

        var weights = outcome.Winners.ToDictionary(student => student, student =>
        {
            ProfileRecordIdentity.EnsureRecordId(student);
            // 编排的位置写在证明的权重槽里，它不是一个抽样权重，不能当权重记进结果。
            return plan is null ? outcome.FrozenWeights.GetValueOrDefault(student.RecordId, 1d) : 1d;
        });
        var recordGroup = HistoryRecordGroup.Resolve(historyGroupCatalog, logger, HistoryGroupKind.RollCall,
            configHandler.Data.EffectiveRollCallSettings.HistoryGroup, request.HistoryGroup);
        var drawRoundId = edit?.SuppressHistory == true
            ? edit.OverrideRoundId ?? string.Empty
            : drawCommits.CommitStudentDraw(new StudentDrawCommit(
                outcome.Winners,
                DateTime.Now,
                count,
                GetListName(),
                request.Group,
                request.Gender,
                (int)configHandler.Data.EffectiveRollCallSettings.DrawType,
                weights,
                request.CourseName,
                edit?.OverrideRoundId,
                request.HistoryGroup,
                recordGroup));
        try
        {
            verification.Publish(outcome);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "点名已提交，但抽取证明发布失败，证明将保持待发布状态。ProofId={ProofId}", outcome.Proof.ProofId);
        }

        // 一轮抽完（本轮的排布已全部出场）就推进到下一轮，并清掉本轮临时记录，否则下轮开局人人都是「已抽」。
        if (plan is not null)
            AdvanceOrchestration(plan, request, Math.Min(count, plan.Order.Count));
        // Counted only after the commit succeeded, so a rolled-back draw is never reported as a draw.
        usageReport.RecordRollCall();
        // 抽签完成事件：插件可以据此记账、写自己的日志，而不必轮询历史。
        eventBus?.Publish(new HostEvents.DrawCompleted(
            DrawPresentationChannel.RollCall,
            outcome.Winners,
            [],
            GetListName(),
            string.Empty,
            drawRoundId,
            DateTime.Now,
            count));
        return new RollCallDrawResult(outcome.Winners, outcome.Proof.ProofId, drawRoundId, outcome.FrozenWeights,
            edit?.Note, edit?.SuppressPresentation ?? false);
    }

    private async Task<RollCallDrawResult?> DrawGroupsAsync(RollCallDrawRequest request, CancellationToken cancellationToken)
    {
        var settings = configHandler.Data.EffectiveRollCallSettings;
        var groups = GroupDrawCandidates.Build(GetSnapshot("", request.Gender).Candidates, request.Gender);
        var eligible = GroupDrawCandidates.Eligible(groups, temporaryRecords.GetGroupCounts(GetListName()),
            settings.DrawMode == DrawMode.Orchestrated ? settings.SampledDrawMode : settings.DrawMode, settings.HalfRepeat);
        if (eligible.Count == 0 || request.Count <= 0 || request.Count > eligible.Count)
            return null;
        var outcome = await verification.DrawGroupsAsync(request.Count, eligible, DrawSettingsType.RollCall,
            DrawProofExportContext.ForStudents(GetListName(), "", request.Gender, request.CourseName),
            courseName: request.CourseName, historyGroup: request.HistoryGroup, cancellationToken: cancellationToken,
            drawMode: settings.DrawMode == DrawMode.Orchestrated ? settings.SampledDrawMode : settings.DrawMode,
            halfRepeat: settings.HalfRepeat).ConfigureAwait(false);
        if (outcome is null)
            return null;
        var roundId = drawCommits.CommitStudentDraw(new StudentDrawCommit(outcome.Winners, DateTime.Now,
            request.Count, GetListName(), GenderScope: request.Gender,
            CourseName: request.CourseName, HistoryGroup: request.HistoryGroup,
            RecordGroup: HistoryRecordGroup.Resolve(historyGroupCatalog, logger, HistoryGroupKind.RollCall,
                settings.HistoryGroup, request.HistoryGroup),
            GroupWinners: outcome.Winners.Select(student => student.Group).ToArray(),
            IncludeGroupMember: settings.GroupMemberDisplay != GroupMemberDisplayMode.GroupOnly));
        verification.Publish(outcome);
        usageReport.RecordRollCall();
        return new RollCallDrawResult(outcome.Winners, outcome.Proof.ProofId, roundId, outcome.FrozenWeights);
    }

    public void Reset(string group, string gender)
    {
        // 编排里「重置」也算一轮：即使没抽完，也按用户的语义进入下一轮。
        if (configHandler.Data.EffectiveRollCallSettings.DrawMode == DrawMode.Orchestrated
            && orchestrationStore is not null)
        {
            var profile = orchestrationStore.FindActive(OrchestrationTarget.RollCall, GetListName()).FirstOrDefault();
            if (profile is not null)
                orchestrationStore.SaveRuntime(AdvanceRuntime(profile, orchestrationStore.GetRuntime(profile)));
        }

        temporaryRecords.ClearStudentScope(GetListName(), gender, group);
    }

    private OrchestrationPlan? PrepareOrchestration(RollCallDrawRequest request, RollCallDrawSnapshot snapshot)
    {
        if (orchestrationStore is null || orchestrationScheduler is null)
            return null;
        var profile = orchestrationStore.FindActive(OrchestrationTarget.RollCall, GetListName()).FirstOrDefault();
        if (profile is null)
            return null;

        var runtime = orchestrationStore.GetRuntime(profile);
        var drawn = new HashSet<Guid>();
        foreach (var key in temporaryRecords.GetStudentCounts(GetListName(), request.Gender, request.Group).Keys)
        {
            if (Guid.TryParse(key, out var recordId))
                drawn.Add(recordId);
        }

        var round = profile.GetRound(runtime.Round);
        var order = orchestrationScheduler.OrderRound(
            profile,
            round,
            snapshot.Remaining,
            drawn,
            randomSource ?? FallbackRandom,
            request.CourseName,
            request.HistoryGroup);
        var nonExcludedIds = snapshot.Remaining
            .Select(student =>
            {
                ProfileRecordIdentity.EnsureRecordId(student);
                return student.RecordId;
            })
            .Where(recordId => profile.FindMember(recordId) is not { IsExcluded: true })
            .Distinct()
            .ToArray();
        var canAdvanceWhenEmpty = nonExcludedIds.Length > 0
            && (nonExcludedIds.All(drawn.Contains)
                || (profile.Rules.Defer && round.Skipped.Count > 0 && nonExcludedIds.All(round.Skipped.Contains)));
        return new OrchestrationPlan(profile, runtime.Round, profile.EffectiveRoundCount, order, canAdvanceWhenEmpty);
    }

    private void AdvanceOrchestration(OrchestrationPlan plan, RollCallDrawRequest request, int drawnCount)
    {
        if (orchestrationStore is null || drawnCount < plan.Order.Count)
            return;

        orchestrationStore.SaveRuntime(AdvanceRuntime(plan.Profile, orchestrationStore.GetRuntime(plan.Profile)));
        // 下一轮所有人重新入池：不清掉本轮临时记录，下一轮开局就会人人被判为「已抽」。
        temporaryRecords.ClearStudentScope(GetListName(), request.Gender, request.Group);
    }

    private static OrchestrationRuntime AdvanceRuntime(OrchestrationProfile profile, OrchestrationRuntime runtime)
    {
        runtime.Round = runtime.Round >= profile.EffectiveRoundCount ? 1 : runtime.Round + 1;
        runtime.UpdatedAt = DateTime.UtcNow;
        return runtime;
    }

    private sealed record OrchestrationPlan(
        OrchestrationProfile Profile,
        int Round,
        int RoundCount,
        IReadOnlyList<Student> Order,
        bool CanAdvanceWhenEmpty);

    /// <summary>Temporary-record scope for number draws; deliberately not a real roster name.</summary>
    public const string NumberListName = "__number_list__";

    public RollCallNumberSnapshot GetNumberSnapshot(int rangeStart, int rangeEnd)
    {
        if (!RosterSelection.IsValidRange(rangeStart, rangeEnd))
            return new RollCallNumberSnapshot(rangeStart, rangeEnd, []);
        var tickets = NumberTickets.CreateStudents(rangeStart, rangeEnd);
        var settings = configHandler.Data.EffectiveRollCallSettings;
        var counts = temporaryRecords.GetStudentCounts(NumberListName, string.Empty, string.Empty);
        var remaining = DrawCandidateFilter.FilterEligibleStudents(
            tickets, string.Empty, string.Empty, counts,
            DrawRepeatPolicy.ResolveThreshold(settings.DrawMode, settings.HalfRepeat));
        return new RollCallNumberSnapshot(rangeStart, rangeEnd, remaining.ToArray());
    }

    public async Task<RollCallNumberDrawResult?> DrawNumbersAsync(RollCallNumberDrawRequest request,
        CancellationToken cancellationToken = default)
    {
        var snapshot = GetNumberSnapshot(request.RangeStart, request.RangeEnd);
        if (snapshot.RemainingCount == 0)
            return null;
        var count = Math.Clamp(request.Count, 1, snapshot.RemainingCount);
        var outcome = await verification.DrawNumberStudentsAsync(
            count, request.RangeStart, request.RangeEnd, snapshot.Remaining,
            DrawProofExportContext.ForStudents(NumberListName, string.Empty, string.Empty, request.CourseName),
            cancellationToken).ConfigureAwait(false);
        if (outcome is null)
            return null;

        var weights = outcome.Winners.ToDictionary(student => student, student =>
        {
            ProfileRecordIdentity.EnsureRecordId(student);
            return outcome.FrozenWeights.GetValueOrDefault(student.RecordId, 1d);
        });
        var drawRoundId = drawCommits.CommitNumberDraw(new NumberDrawCommit(
            outcome.Winners,
            NumberListName,
            string.Empty,
            string.Empty,
            null,
            null,
            DateTime.Now,
            count,
            request.RangeStart,
            request.RangeEnd));
        try
        {
            verification.Publish(outcome);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "数字点名已提交，但抽取证明发布失败，证明将保持待发布状态。ProofId={ProofId}", outcome.Proof.ProofId);
        }

        randomNumberStatistics?.Record(
            "roll_call",
            request.RangeStart,
            request.RangeEnd,
            ExtractNumbers(outcome.Winners));
        usageReport.RecordRollCall();
        eventBus?.Publish(new HostEvents.DrawCompleted(
            DrawPresentationChannel.RollCall,
            outcome.Winners,
            [],
            NumberListName,
            string.Empty,
            drawRoundId,
            DateTime.Now,
            count));
        return new RollCallNumberDrawResult(outcome.Winners, outcome.Proof.ProofId, drawRoundId, outcome.FrozenWeights);
    }

    public void ResetNumbers(
        RandomNumberResetBehavior behavior = RandomNumberResetBehavior.TemporaryOnly)
    {
        temporaryRecords.ClearStudentScope(NumberListName, string.Empty, string.Empty);
        if (behavior == RandomNumberResetBehavior.TemporaryAndStatistics)
            randomNumberStatistics?.Clear();
    }

    private DrawPipelineContext BuildRollCallContext(string group, string gender, string courseName, int requestedCount) =>
        new(DrawPresentationChannel.RollCall,
            GetListName(),
            string.Empty,
            group ?? string.Empty,
            gender ?? string.Empty,
            courseName ?? string.Empty,
            requestedCount,
            (int)configHandler.Data.EffectiveRollCallSettings.DrawType);

    private IReadOnlyList<Student> ApplyCandidateFilters(IReadOnlyList<Student> candidates, string group, string gender) =>
        pipeline is null
            ? candidates
            : pipeline.ApplyFilters(BuildRollCallContext(group, gender, string.Empty, 0), candidates, []).Students;

    private IReadOnlyList<string> GetScopedValues(Func<Student, string> selector) =>
        (profileService.CurrentStudentList?.Students ?? []).Where(student => student.IsCandidate)
            .Select(selector).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.CurrentCulture).OrderBy(value => value, StringComparer.CurrentCulture).ToArray();

    private string GetListName() => profileService.StudentListConfig?.Name ?? "default";

    private static IReadOnlyList<int> ExtractNumbers(IEnumerable<Student> students) =>
        students.Select(student => ParseNumber(student.Name)).Where(number => number is not null)
            .Select(number => number!.Value).ToArray();

    private static int? ParseNumber(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
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
    string CourseName,
    string? HistoryGroup = null,
    bool DrawGroups = false);

public sealed record LotteryDrawResult(
    IReadOnlyList<Prize> Prizes,
    IReadOnlyList<Student> AssignedStudents,
    Guid PrizeProofId,
    string DrawRoundId,
    string? Note = null,
    bool SuppressPresentation = false);

/// <summary>One "no roster" number draw for the lottery page; members may still be assigned from a roster.</summary>
public sealed record LotteryNumberDrawRequest(
    int RangeStart,
    int RangeEnd,
    int Count,
    string CourseName,
    string StudentListName,
    string Group,
    string Gender,
    string? HistoryGroup = null);

public sealed record LotteryNumberDrawResult(
    IReadOnlyList<Prize> Numbers,
    IReadOnlyList<Student> AssignedStudents,
    Guid PrizeProofId,
    string DrawRoundId,
    string? Note = null,
    bool SuppressPresentation = false);

public sealed record LotteryNumberSnapshot(int RangeStart, int RangeEnd, IReadOnlyList<Prize> Remaining)
{
    public int TotalCount => RangeEnd >= RangeStart ? RangeEnd - RangeStart + 1 : 0;
    public int RemainingCount => Remaining.Count;
}

/// <summary>
/// Shared lottery use case. It preserves one transactional draw round for prizes and assignments.
/// </summary>
public sealed class LotteryDrawService(
    MainConfigHandler configHandler,
    IProfileService profileService,
    IProfileCatalogManager profileCatalogManager,
    IDrawTemporaryRecordService temporaryRecords,
    IDrawCommitService drawCommits,
    VerificationDrawCoordinator verification,
    PlatformUsageReportService usageReport,
    ILogger<LotteryDrawService> logger,
    IDrawPipelineService? pipeline = null,
    IPluginEventBus? eventBus = null,
    IRandomNumberStatisticsService? randomNumberStatistics = null,
    IHistoryGroupCatalog? historyGroupCatalog = null,
    IOrchestrationStore? orchestrationStore = null,
    OrchestrationScheduler? orchestrationScheduler = null,
    IRandomSource? randomSource = null)
{
    public LotteryDrawSnapshot GetSnapshot(string studentListName, string group, string gender)
    {
        var prizes = (profileService.CurrentPrizeList?.Prizes ?? []).Where(prize => prize.IsCandidate).ToArray();
        var settings = configHandler.Data.EffectiveLotterySettings;
        var counts = temporaryRecords.GetPrizeCounts(GetPrizePoolName());
        var remaining = DrawCandidateFilter.FilterEligiblePrizes(prizes, counts, settings.DrawType,
            DrawRepeatPolicy.ResolveThreshold(settings.DrawMode, settings.HalfRepeat)).OrderForList().ToArray();
        var students = GetEligibleStudents(studentListName, group, gender);
        // 插件候选集过滤器同时作用于奖池与参与学生（与点名通道共用同一套过滤点）。
        var filtered = pipeline?.ApplyFilters(
            BuildLotteryContext(group, gender, string.Empty, 0), students, prizes);
        if (filtered is not null)
        {
            prizes = [.. filtered.Prizes];
            students = [.. filtered.Students];
            remaining = DrawCandidateFilter.FilterEligiblePrizes(prizes, counts, settings.DrawType,
                DrawRepeatPolicy.ResolveThreshold(settings.DrawMode, settings.HalfRepeat)).OrderForList().ToArray();
        }
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
        if (configHandler.Data.EffectiveLotterySettings.ClearRecord == ClearRecordMode.Restarted)
            temporaryRecords.ClearPrizeListOnce(name);
        profileService.LoadPrizeProfile(name);
        profileCatalogManager.SetDefaultPrizePool(name);
    }

    public void SwitchStudentList(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, GetStudentListName(), StringComparison.Ordinal))
            return;
        if (configHandler.Data.EffectiveRollCallSettings.ClearRecord == ClearRecordMode.Restarted)
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
        // 编排抽取不抽样：本轮的奖品顺序事先排定，抽取只是按序取奖。没有可用编排名单时静默回退随机抽奖，
        // 与点名通道一致（主页面不暴露编排状态，缺名单不该让抽奖按钮变成死键）。
        var plan = configHandler.Data.EffectiveLotterySettings.DrawMode == DrawMode.Orchestrated
            ? PrepareOrchestration(snapshot)
            : null;
        if (plan is not null)
        {
            if (plan.Order.Count == 0)
            {
                // A round with only deferred/already-drawn participants is complete even when the
                // raw snapshot still contains entries. Global exclusion is different: keep the
                // empty state visible instead of silently cycling through rounds forever.
                if (plan.CanAdvanceWhenEmpty)
                    AdvanceOrchestration(plan);
                return null;
            }
            // Exclude/defer rules may leave fewer drawable inventory units than the raw
            // snapshot. Commit and student assignment must use the actual planned count.
            count = Math.Min(count, plan.Order.Count);
        }
        var groupCandidates = request.DrawGroups && hasStudentAssignment
            ? GetEligibleGroups(request.Gender, request.CourseName)
            : [];
        if (hasStudentAssignment && (request.DrawGroups ? groupCandidates.Count : snapshot.EligibleStudents.Count) < count)
            return null;

        VerificationDrawOutcome<Prize>? prizes;
        if (plan is not null)
        {
            prizes = await verification.DrawOrchestratedPrizesAsync(
                plan.Order.Take(count).ToArray(),
                plan.Round,
                plan.RoundCount,
                DrawProofExportContext.ForPrizes(GetPrizePoolName()),
                cancellationToken,
                countMode: configHandler.Data.EffectiveLotterySettings.DrawType == LotteryDrawType.Count).ConfigureAwait(false);
        }
        else
        {
            prizes = await verification.DrawPrizesAsync(count,
                temporaryRecords.GetPrizeCounts(GetPrizePoolName()), snapshot.Prizes,
                DrawProofExportContext.ForPrizes(GetPrizePoolName()),
                request.HistoryGroup,
                cancellationToken).ConfigureAwait(false);
        }
        if (prizes is null)
            return null;
        IReadOnlyList<Student> assigned = [];
        VerificationDrawOutcome<Student>? assignedOutcome = null;
        if (hasStudentAssignment)
        {
            // 抽奖轮里附带的「分配学生」是这一轮自己的随机分配：点名通道即使配了编排，也不能把点名排布
            // 塞进抽奖证明，所以这里显式按「无重复限制」抽样（候选集已在 GetSnapshot 里按点名设置过滤过）。
            assignedOutcome = request.DrawGroups
                ? await verification.DrawGroupsAsync(count, groupCandidates, DrawSettingsType.RollCall,
                    DrawProofExportContext.ForStudents(GetStudentListName(), request.Group, request.Gender, request.CourseName),
                    parentProofId: prizes.Proof.ProofId, courseName: request.CourseName, historyGroup: request.HistoryGroup,
                    drawMode: DrawMode.Repeat, channel: "lottery", cancellationToken: cancellationToken).ConfigureAwait(false)
                : await verification.DrawStudentsAsync(count, snapshot.EligibleStudents, DrawSettingsType.RollCall,
                DrawProofExportContext.ForStudents(GetStudentListName(), request.Group, request.Gender, request.CourseName),
                prizes.Proof.ProofId, request.CourseName, request.HistoryGroup, cancellationToken,
                configHandler.Data.EffectiveRollCallSettings.DrawMode == DrawMode.Orchestrated ? DrawMode.Repeat : null,
                channel: "lottery").ConfigureAwait(false);
            if (assignedOutcome is null)
                return null;
            assigned = assignedOutcome.Winners;
            if (assigned.Count != prizes.Winners.Count)
                return null;
        }

        // 插件结果后处理器：同点名通道，只能批注 / 决定是否写历史 / 是否让宿主跳过自己的呈现动画，
        // 抽出的奖与分配名单本身不允许改——抽取证明已经生成，改写会破坏凭证与审计链。
        var edit = pipeline?.ApplyPostProcessors(
            BuildLotteryContext(request.Group, request.Gender, request.CourseName, count),
            new DrawResultEdit(assigned, prizes.Winners));
        if (edit is not null &&
            (!edit.Students.SequenceEqual(assigned) || !edit.Prizes.SequenceEqual(prizes.Winners)))
        {
            logger.LogWarning("插件结果后处理器试图改写已抽出的奖或学生，为保护抽取证明与审计链，本次改写被忽略。");
            edit = edit with { Students = assigned, Prizes = prizes.Winners };
        }

        var recordGroup = HistoryRecordGroup.Resolve(historyGroupCatalog, logger, HistoryGroupKind.Lottery,
            configHandler.Data.EffectiveLotterySettings.HistoryGroup, request.HistoryGroup);
        // 抽奖附带抽中的学生在学生历史里另算一条通道：未按组筛选时落点名通道的默认记录组，
        // 而不是奖池那一组的名字（两边是两份独立的历史文件，组名互不可见）。
        var studentRecordGroup = HistoryRecordGroup.Resolve(historyGroupCatalog, logger, HistoryGroupKind.RollCall,
            configHandler.Data.EffectiveRollCallSettings.HistoryGroup, request.HistoryGroup);
        var roundId = edit?.SuppressHistory == true
            ? edit.OverrideRoundId ?? string.Empty
            : drawCommits.CommitLotteryDraw(new LotteryDrawCommit(
                prizes.Winners,
                DateTime.Now,
                count,
                GetPrizePoolName(),
                assigned,
                hasStudentAssignment ? GetStudentListName() : null,
                request.Group,
                request.Gender,
                (int)configHandler.Data.EffectiveLotterySettings.DrawType,
                (int)configHandler.Data.EffectiveRollCallSettings.DrawType,
                request.CourseName,
                edit?.OverrideRoundId,
                request.HistoryGroup,
                recordGroup,
                studentRecordGroup,
                request.DrawGroups ? assigned.Select(student => student.Group).ToArray() : null,
                request.DrawGroups && configHandler.Data.EffectiveLotterySettings.ShowGroupMember));
        PublishProof(prizes);
        if (assignedOutcome is not null)
            PublishProof(assignedOutcome);

        // 一轮抽完（奖池被这次抽干，或剩无可抽）就推进到下一轮，并清掉本轮临时记录，否则下轮开局全员「已抽」。
        // Advance once every drawable unit in the current plan has been consumed. The raw
        // snapshot may still contain units excluded or deferred by orchestration rules.
        if (plan is not null && count >= plan.Order.Count)
            AdvanceOrchestration(plan);

        usageReport.RecordLottery();
        // 抽签完成事件：奖品与分配到的学生一起广播，插件不必自己拼历史。
        eventBus?.Publish(new HostEvents.DrawCompleted(
            DrawPresentationChannel.Lottery,
            assigned,
            prizes.Winners,
            hasStudentAssignment ? GetStudentListName() : string.Empty,
            GetPrizePoolName(),
            roundId,
            DateTime.Now,
            count));
        return new LotteryDrawResult(prizes.Winners, assigned, prizes.Proof.ProofId, roundId,
            edit?.Note, edit?.SuppressPresentation ?? false);
    }

    public void Reset(string studentListName, string group, string gender, bool drawGroups = false)
    {
        // 编排里「重置」也算一轮：即使没抽完，也按用户的语义进入下一轮。
        if (configHandler.Data.EffectiveLotterySettings.DrawMode == DrawMode.Orchestrated
            && orchestrationStore is not null)
        {
            var profile = orchestrationStore.FindActive(OrchestrationTarget.Lottery, GetPrizePoolName()).FirstOrDefault();
            if (profile is not null)
                orchestrationStore.SaveRuntime(AdvanceRuntime(profile, orchestrationStore.GetRuntime(profile)));
        }

        temporaryRecords.ClearPrizeList(GetPrizePoolName());
        if (!string.IsNullOrWhiteSpace(studentListName))
        {
            if (drawGroups)
                temporaryRecords.ClearGroups(GetStudentListName());
            else
                temporaryRecords.ClearStudentScope(GetStudentListName(), gender, group);
        }
    }

    private LotteryOrchestrationPlan? PrepareOrchestration(LotteryDrawSnapshot snapshot)
    {
        if (orchestrationStore is null || orchestrationScheduler is null)
            return null;
        var profile = orchestrationStore.FindActive(OrchestrationTarget.Lottery, GetPrizePoolName()).FirstOrDefault();
        if (profile is null)
            return null;

        var runtime = orchestrationStore.GetRuntime(profile);
        var drawn = new Dictionary<Guid, int>();
        foreach (var entry in temporaryRecords.GetPrizeCounts(GetPrizePoolName()))
        {
            // Temporary prize records use the canonical D-format key, while older stores may use N.
            // Accept both so Count orchestration expands only the inventory units still drawable.
            if (Guid.TryParse(entry.Key, out var recordId))
                drawn[recordId] = entry.Value;
        }

        var round = profile.GetRound(runtime.Round);
        var expandInventory = configHandler.Data.EffectiveLotterySettings.DrawType == LotteryDrawType.Count;
        var order = orchestrationScheduler.OrderRound(
            profile,
            round,
            snapshot.Remaining,
            drawn,
            randomSource ?? FallbackRandom,
            expandInventory: expandInventory);
        var nonExcludedIds = snapshot.Remaining
            .Select(prize => prize.RecordId)
            .Where(recordId => profile.FindMember(recordId) is not { IsExcluded: true })
            .Distinct()
            .ToArray();
        var canAdvanceWhenEmpty = nonExcludedIds.Length > 0
            && (nonExcludedIds.All(recordId => drawn.GetValueOrDefault(recordId) > 0)
                || (profile.Rules.Defer && round.Skipped.Count > 0 && nonExcludedIds.All(round.Skipped.Contains)));
        return new LotteryOrchestrationPlan(profile, runtime.Round, profile.EffectiveRoundCount, order,
            canAdvanceWhenEmpty);
    }

    private void AdvanceOrchestration(LotteryOrchestrationPlan plan)
    {
        if (orchestrationStore is null)
            return;

        orchestrationStore.SaveRuntime(AdvanceRuntime(plan.Profile, orchestrationStore.GetRuntime(plan.Profile)));
        // 下一轮整池重新可抽：不清掉本轮临时记录，下一轮开局就会人人被判为「已抽」。
        temporaryRecords.ClearPrizeList(GetPrizePoolName());
    }

    private static OrchestrationRuntime AdvanceRuntime(OrchestrationProfile profile, OrchestrationRuntime runtime)
    {
        runtime.Round = runtime.Round >= profile.EffectiveRoundCount ? 1 : runtime.Round + 1;
        runtime.UpdatedAt = DateTime.UtcNow;
        return runtime;
    }

    private sealed record LotteryOrchestrationPlan(
        OrchestrationProfile Profile,
        int Round,
        int RoundCount,
        IReadOnlyList<Prize> Order,
        bool CanAdvanceWhenEmpty);

    private static readonly CryptoRandomSource FallbackRandom = new();

    /// <summary>Temporary-record scope for number prize draws; deliberately not a real prize-pool name.</summary>
    public const string NumberPrizePoolName = "__number_prize_pool__";

    public LotteryNumberSnapshot GetNumberSnapshot(int rangeStart, int rangeEnd)
    {
        if (!RosterSelection.IsValidRange(rangeStart, rangeEnd))
            return new LotteryNumberSnapshot(rangeStart, rangeEnd, []);
        var prizes = NumberTickets.CreatePrizes(rangeStart, rangeEnd);
        var settings = configHandler.Data.EffectiveLotterySettings;
        var counts = temporaryRecords.GetPrizeCounts(NumberPrizePoolName);
        var remaining = DrawCandidateFilter.FilterEligiblePrizes(
            prizes, counts, settings.DrawType,
            DrawRepeatPolicy.ResolveThreshold(settings.DrawMode, settings.HalfRepeat));
        return new LotteryNumberSnapshot(rangeStart, rangeEnd, remaining.ToArray());
    }

    public async Task<LotteryNumberDrawResult?> DrawNumbersAsync(LotteryNumberDrawRequest request,
        CancellationToken cancellationToken = default)
    {
        var hasStudentAssignment = !string.IsNullOrWhiteSpace(request.StudentListName);
        if (hasStudentAssignment)
            SwitchStudentList(request.StudentListName);

        var snapshot = GetNumberSnapshot(request.RangeStart, request.RangeEnd);
        if (snapshot.RemainingCount == 0)
            return null;
        var count = Math.Clamp(request.Count, 1, snapshot.RemainingCount);
        IReadOnlyList<Student> eligible = hasStudentAssignment
            ? GetEligibleStudents(request.StudentListName, request.Group, request.Gender)
            : [];
        if (hasStudentAssignment && eligible.Count < count)
            return null;

        var prizes = await verification.DrawNumberPrizesAsync(
            count, request.RangeStart, request.RangeEnd, snapshot.Remaining,
            DrawProofExportContext.ForPrizes(NumberPrizePoolName),
            cancellationToken).ConfigureAwait(false);
        if (prizes is null)
            return null;

        IReadOnlyList<Student> assigned = [];
        VerificationDrawOutcome<Student>? assignedOutcome = null;
        if (hasStudentAssignment)
        {
            assignedOutcome = await verification.DrawStudentsAsync(count, eligible, DrawSettingsType.RollCall,
                DrawProofExportContext.ForStudents(GetStudentListName(), request.Group, request.Gender, request.CourseName),
                prizes.Proof.ProofId, request.CourseName, request.HistoryGroup, cancellationToken,
                channel: "lottery").ConfigureAwait(false);
            if (assignedOutcome is null)
                return null;
            assigned = assignedOutcome.Winners;
            if (assigned.Count != prizes.Winners.Count)
                return null;
        }

        var drawRoundId = drawCommits.CommitNumberDraw(new NumberDrawCommit(
            hasStudentAssignment ? assigned : null,
            hasStudentAssignment ? GetStudentListName() : null,
            request.Group,
            request.Gender,
            prizes.Winners,
            NumberPrizePoolName,
            DateTime.Now,
            count,
            request.RangeStart,
            request.RangeEnd));
        PublishProof(prizes);
        if (assignedOutcome is not null)
            PublishProof(assignedOutcome);

        randomNumberStatistics?.Record(
            "lottery",
            request.RangeStart,
            request.RangeEnd,
            ExtractNumbers(prizes.Winners));
        usageReport.RecordLottery();
        eventBus?.Publish(new HostEvents.DrawCompleted(
            DrawPresentationChannel.Lottery,
            assigned,
            prizes.Winners,
            hasStudentAssignment ? GetStudentListName() : string.Empty,
            NumberPrizePoolName,
            drawRoundId,
            DateTime.Now,
            count));
        return new LotteryNumberDrawResult(prizes.Winners, assigned, prizes.Proof.ProofId, drawRoundId);
    }

    public void ResetNumbers(string studentListName, string group, string gender,
        RandomNumberResetBehavior behavior = RandomNumberResetBehavior.TemporaryOnly)
    {
        temporaryRecords.ClearPrizeList(NumberPrizePoolName);
        if (!string.IsNullOrWhiteSpace(studentListName))
            temporaryRecords.ClearStudentScope(studentListName, gender, group);
        if (behavior == RandomNumberResetBehavior.TemporaryAndStatistics)
            randomNumberStatistics?.Clear();
    }

    private DrawPipelineContext BuildLotteryContext(string group, string gender, string courseName, int requestedCount) =>
        new(DrawPresentationChannel.Lottery,
            GetStudentListName(),
            GetPrizePoolName(),
            group ?? string.Empty,
            gender ?? string.Empty,
            courseName ?? string.Empty,
            requestedCount,
            (int)configHandler.Data.EffectiveLotterySettings.DrawType);

    private void PublishProof<TCandidate>(VerificationDrawOutcome<TCandidate> outcome)
        where TCandidate : class
    {
        try
        {
            verification.Publish(outcome);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "抽奖已提交，但抽取证明发布失败，证明将保持待发布状态。ProofId={ProofId}", outcome.Proof.ProofId);
        }
    }

    private IReadOnlyList<Student> GetEligibleStudents(string selectedList, string group, string gender)
    {
        if (string.IsNullOrWhiteSpace(selectedList))
            return [];
        var threshold = DrawRepeatPolicy.ResolveThreshold(configHandler.Data.EffectiveRollCallSettings.DrawMode,
            configHandler.Data.EffectiveRollCallSettings.HalfRepeat);
        return DrawCandidateFilter.FilterEligibleStudents(profileService.CurrentStudentList?.Students ?? [], group, gender,
            temporaryRecords.GetStudentCounts(GetStudentListName(), gender, group), threshold).ToArray();
    }

    public IReadOnlyList<GroupDrawCandidates.Group> GetEligibleGroups(string gender, string courseName = "")
    {
        var students = (profileService.CurrentStudentList?.Students ?? [])
            .Where(student => DrawCandidateFilter.MatchesScope(student, "", gender)).ToArray();
        var prizes = (profileService.CurrentPrizeList?.Prizes ?? []).Where(prize => prize.IsCandidate).ToArray();
        var filtered = pipeline?.ApplyFilters(BuildLotteryContext("", gender, courseName, 0), students, prizes);
        var groups = GroupDrawCandidates.Build(filtered?.Students ?? students, gender);
        var settings = configHandler.Data.EffectiveRollCallSettings;
        return GroupDrawCandidates.Eligible(groups, temporaryRecords.GetGroupCounts(GetStudentListName()),
            settings.DrawMode == DrawMode.Orchestrated ? settings.SampledDrawMode : settings.DrawMode, settings.HalfRepeat);
    }

    private IReadOnlyList<string> GetStudentScopeValues(Func<Student, string> selector) =>
        (profileService.CurrentStudentList?.Students ?? []).Where(student => student.IsCandidate)
            .Select(selector).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.CurrentCulture).OrderBy(value => value, StringComparer.CurrentCulture).ToArray();

    private string GetPrizePoolName() => profileService.PrizeListConfig?.Name ?? "default";
    private string GetStudentListName() => profileService.StudentListConfig?.Name ?? "default";

    private static IReadOnlyList<int> ExtractNumbers(IEnumerable<Prize> prizes) =>
        prizes.Select(prize => ParseNumber(prize.Name)).Where(number => number is not null)
            .Select(number => number!.Value).ToArray();

    private static int? ParseNumber(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
}
