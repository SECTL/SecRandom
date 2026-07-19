using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

internal sealed class LotterySession(
    MainConfigHandler configHandler,
    IProfileService profileService,
    IDrawTemporaryRecordService temporaryRecordService,
    DrawEngine drawEngine) : ILotterySession
{
    public IReadOnlyList<Prize> GetEligiblePrizes()
    {
        var prizes = profileService.CurrentPrizeList?.Prizes.Where(prize => prize.IsCandidate).ToList() ?? [];
        var counts = temporaryRecordService.GetPrizeCounts(GetListName());
        if (configHandler.Data.LotterySettings.DrawType == LotteryDrawType.Count)
            return prizes.Where(prize => prize.Count - counts.GetValueOrDefault(ProfileRecordIdentity.EnsureRecordId(prize)) > 0).ToArray();

        var threshold = GetRepeatThreshold(configHandler.Data.LotterySettings.DrawMode, configHandler.Data.LotterySettings.HalfRepeat);
        return threshold <= 0
            ? prizes
            : prizes.Where(prize => counts.GetValueOrDefault(ProfileRecordIdentity.EnsureRecordId(prize)) < threshold).ToArray();
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

        profileService.RecordPrizeHistory(output.Result, DateTime.Now, 1);
        temporaryRecordService.RecordPrizes(GetListName(), output.Result);
        return output;
    }

    private string GetListName() => profileService.PrizeListConfig?.Name ?? "default";

    private static int GetRepeatThreshold(DrawMode mode, int halfRepeat) => mode switch
    {
        DrawMode.Repeat => 0,
        DrawMode.NoRepeat => 1,
        DrawMode.HalfRepeat => Math.Max(1, halfRepeat),
        _ => 1
    };
}
