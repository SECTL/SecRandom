using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Profiles;
using SecRandom.Core.Services.SimpleMode;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

internal sealed class RollCallSession(
    MainConfigHandler configHandler,
    IProfileService profileService,
    IDrawTemporaryRecordService temporaryRecordService,
    IDrawCommitService drawCommitService,
    DrawEngine drawEngine,
    IHistoryGroupCatalog historyGroupCatalog) : IRollCallSession
{
    /// <summary>简洁模式下隐藏的点名抽取设置按默认值读取。</summary>
    private RollCallSettingsConfig Settings => configHandler.Data.SimpleModeEnabled
        ? SimpleModeDefaults.RollCallSettings
        : configHandler.Data.RollCallSettings;

    public IReadOnlyList<Student> GetEligibleStudents()
    {
        var students = profileService.CurrentStudentList?.Students ?? [];
        var settings = Settings;
        var threshold = DrawRepeatPolicy.ResolveThreshold(settings.DrawMode, settings.HalfRepeat);
        var counts = temporaryRecordService.GetStudentCounts(GetListName(), string.Empty, string.Empty);
        return DrawCandidateFilter.FilterEligibleStudents(students, string.Empty, string.Empty, counts, threshold);
    }

    public DrawResult<Student> DrawOnce()
    {
        var candidates = GetEligibleStudents();
        if (candidates.Count == 0)
            return new DrawResult<Student> { Status = DrawStatus.NoEligibleCandidates };

        var drawType = Settings.DrawType;
        var prepared = drawType == DrawType.Fair
            ? drawEngine.PrepareStudentsForMobileDesktopDefaults(1, candidates, DrawSettingsType.RollCall, DrawType.Fair)
            : null;
        var output = drawType == DrawType.Fair
            ? drawEngine.DrawPreparedStudents(prepared!, 1)
            : drawEngine.DrawPreparedStudentsWithMobileDesktopDefaults(1, candidates, DrawSettingsType.RollCall, DrawType.Random);
        if (!output.IsSuccess || output.Result.Count == 0)
            return output;

        var weights = drawType == DrawType.Fair
            ? prepared!.WeightedCandidates.ToDictionary(candidate => candidate.Candidate, candidate => candidate.Weight)
            : output.Result.ToDictionary(student => student, _ => 1.0);
        drawCommitService.CommitStudentDraw(new StudentDrawCommit(
            output.Result,
            DateTime.Now,
            1,
            GetListName(),
            DrawMethod: (int)drawType,
            Weights: weights,
            RecordGroup: ResolveRecordGroup()));
        return output;
    }

    /// <summary>
    ///     这条历史记录该盖的组标记。移动端点名没有组选择器，读取范围永远是「全部历史」，但写入必须落在
    ///     本通道自己配置的默认记录组上——否则点名、闪抽、抽奖的记录会一起挤进默认组（见
    ///     <see cref="HistoryGroupScope.ResolveRecordGroup" />）。
    /// </summary>
    private string ResolveRecordGroup()
    {
        IReadOnlyList<string>? names = null;
        try
        {
            names = historyGroupCatalog.GetGroupNames(HistoryGroupKind.RollCall);
        }
        catch (Exception)
        {
            // 组目录只是书签：读不到就只影响「组名是否还存在」的判断，不能让一次抽取因此失败。
        }

        return HistoryGroupScope.ResolveRecordGroup(Settings.HistoryGroup, names);
    }

    private string GetListName() => profileService.StudentListConfig?.Name ?? "default";
}
