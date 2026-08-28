using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

internal sealed class LotterySession(
    MainConfigHandler configHandler,
    IProfileService profileService,
    IDrawTemporaryRecordService temporaryRecordService,
    IDrawCommitService drawCommitService,
    DrawEngine drawEngine) : ILotterySession
{
    public IReadOnlyList<Prize> GetEligiblePrizes()
    {
        var prizes = profileService.CurrentPrizeList?.Prizes ?? [];
        var counts = temporaryRecordService.GetPrizeCounts(GetListName());
        var settings = configHandler.Data.LotterySettings;
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
            PrizeDrawMethod: (int)configHandler.Data.LotterySettings.DrawType));
        return output;
    }

    private string GetListName() => profileService.PrizeListConfig?.Name ?? "default";
}
