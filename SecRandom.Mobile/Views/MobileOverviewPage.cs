using Avalonia.Controls;
using SecRandom.Core.Views;
using SecRandom.Core.Abstraction.Services;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views;

public sealed class MobileOverviewPage : ViewBase
{
    public MobileOverviewPage(IProfileService profileService)
    {
        var studentCount = profileService.CurrentStudentList?.Students.Count(student => student.IsCandidate) ?? 0;
        var prizeCount = profileService.CurrentPrizeList?.Prizes.Count(prize => prize.IsCandidate) ?? 0;

        Content = MobileUi.CreateScroll([
            MobileUi.CreateLabel(LR.S_DataOverview),
            MobileUi.CreateTitle(LR.P_Overview),
            MobileUi.CreateMetric(LR.M_Students, studentCount.ToString(System.Globalization.CultureInfo.CurrentCulture), MobileTheme.PrimaryWash),
            MobileUi.CreateMetric(LR.M_Prizes, prizeCount.ToString(System.Globalization.CultureInfo.CurrentCulture), MobileTheme.WarmWash),
            MobileUi.CreateMetric(LR.M_RollCallRounds, (profileService.CurrentStudentHistory?.TotalRounds ?? 0).ToString(System.Globalization.CultureInfo.CurrentCulture), MobileTheme.SurfaceMuted),
            MobileUi.CreateMetric(LR.M_LotteryRounds, (profileService.CurrentPrizeHistory?.TotalRounds ?? 0).ToString(System.Globalization.CultureInfo.CurrentCulture), MobileTheme.SurfaceMuted)
        ]);
    }
}
