using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Abstraction.Services;

namespace SecRandom.Views.Mobile;

/// <summary>
/// The fixed metric-card hierarchy lives in AXAML. Runtime capability projection only
/// chooses whether lottery cards are visible and writes the current profile counts.
/// </summary>
public sealed partial class MobileOverviewPage : UserControl
{
    public MobileOverviewPage(IProfileService profileService, IMobileCapabilities capabilities)
    {
        InitializeComponent();

        StudentMetricValue.Text = FormatCount(profileService.CurrentStudentList?.Students.Count(student => student.IsCandidate) ?? 0);
        RollCallRoundsMetricValue.Text = FormatCount(profileService.CurrentStudentHistory?.TotalRounds ?? 0);

        PrizeMetricCard.IsVisible = capabilities.IsLotteryEnabled;
        LotteryRoundsMetricCard.IsVisible = capabilities.IsLotteryEnabled;
        if (capabilities.IsLotteryEnabled)
        {
            PrizeMetricValue.Text = FormatCount(profileService.CurrentPrizeList?.Prizes.Count(prize => prize.IsCandidate) ?? 0);
            LotteryRoundsMetricValue.Text = FormatCount(profileService.CurrentPrizeHistory?.TotalRounds ?? 0);
        }
        else
        {
            Grid.SetRow(RollCallRoundsMetricCard, 0);
            Grid.SetColumn(RollCallRoundsMetricCard, 1);
        }

        MobileAnimations.PlayPageEnter(PageScroll);
    }

    private static string FormatCount(int value) =>
        value.ToString(System.Globalization.CultureInfo.CurrentCulture);

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
