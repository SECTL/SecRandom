using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Profiles;
using SecRandom.Core.Services.SimpleMode;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

internal sealed class LotterySession(
    MainConfigHandler configHandler,
    IProfileService profileService,
    IDrawTemporaryRecordService temporaryRecordService,
    IDrawCommitService drawCommitService,
    DrawEngine drawEngine,
    IHistoryGroupCatalog historyGroupCatalog) : ILotterySession
{
    private LotterySettingsConfig Settings =>
        configHandler.Data.SimpleModeEnabled ? SimpleModeDefaults.LotterySettings : configHandler.Data.LotterySettings;

    public IReadOnlyList<Prize> GetEligiblePrizes()
    {
        var prizes = profileService.CurrentPrizeList?.Prizes ?? [];
        var counts = temporaryRecordService.GetPrizeCounts(GetListName());
        var settings = Settings;
        return DrawCandidateFilter.FilterEligiblePrizes(
            prizes,
            counts,
            settings.DrawType,
            DrawRepeatPolicy.ResolveThreshold(settings.DrawMode, settings.HalfRepeat));
    }

    public DrawResult<Prize> DrawOnce()
    {
        var candidates = GetEligiblePrizes();
        if (candidates.Count == 0)
            return new DrawResult<Prize> { Status = DrawStatus.NoEligibleCandidates };

        var output = drawEngine.DrawPrizeWithTemporaryCounts(
            1,
            prize => candidates.Contains(prize),
            temporaryRecordService.GetPrizeCounts(GetListName()));
        if (!output.IsSuccess || output.Result.Count == 0)
            return output;

        drawCommitService.CommitLotteryDraw(new LotteryDrawCommit(
            output.Result,
            DateTime.Now,
            1,
            GetListName(),
            PrizeDrawMethod: (int)Settings.DrawType,
            RecordGroup: ResolveRecordGroup()));
        return output;
    }

    /// <summary>
    ///     这条奖品历史该盖的组标记：抽奖有自己的默认记录组，绝不能因为移动端没有组选择器就并进默认组。
    /// </summary>
    private string ResolveRecordGroup()
    {
        IReadOnlyList<string>? names = null;
        try
        {
            names = historyGroupCatalog.GetGroupNames(HistoryGroupKind.Lottery);
        }
        catch (Exception)
        {
            // 组目录只是书签：读不到只影响「组名是否还存在」的判断，不能让一次抽取因此失败。
        }

        return HistoryGroupScope.ResolveRecordGroup(Settings.HistoryGroup, names);
    }

    private string GetListName() => profileService.PrizeListConfig?.Name ?? "default";
}
