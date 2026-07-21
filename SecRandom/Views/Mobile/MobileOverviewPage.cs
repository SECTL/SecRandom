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
    private readonly ScrollViewer _pageScroll;
    private readonly TextBlock _studentMetricValue;
    private readonly TextBlock _rollCallRoundsMetricValue;
    private readonly Control _prizeMetricCard;
    private readonly TextBlock _prizeMetricValue;
    private readonly Control _rollCallRoundsMetricCard;
    private readonly Control _lotteryRoundsMetricCard;
    private readonly TextBlock _lotteryRoundsMetricValue;

    public MobileOverviewPage(IProfileService profileService, IMobileCapabilities capabilities)
    {
        InitializeComponent();
        _pageScroll = this.FindControl<ScrollViewer>("PageScroll")!;
        _studentMetricValue = this.FindControl<TextBlock>("StudentMetricValue")!;
        _rollCallRoundsMetricValue = this.FindControl<TextBlock>("RollCallRoundsMetricValue")!;
        _prizeMetricCard = this.FindControl<Control>("PrizeMetricCard")!;
        _prizeMetricValue = this.FindControl<TextBlock>("PrizeMetricValue")!;
        _rollCallRoundsMetricCard = this.FindControl<Control>("RollCallRoundsMetricCard")!;
        _lotteryRoundsMetricCard = this.FindControl<Control>("LotteryRoundsMetricCard")!;
        _lotteryRoundsMetricValue = this.FindControl<TextBlock>("LotteryRoundsMetricValue")!;

        _studentMetricValue.Text = FormatCount(profileService.CurrentStudentList?.Students.Count(student => student.IsCandidate) ?? 0);
        _rollCallRoundsMetricValue.Text = FormatCount(profileService.CurrentStudentHistory?.TotalRounds ?? 0);

        _prizeMetricCard.IsVisible = capabilities.IsLotteryEnabled;
        _lotteryRoundsMetricCard.IsVisible = capabilities.IsLotteryEnabled;
        if (capabilities.IsLotteryEnabled)
        {
            _prizeMetricValue.Text = FormatCount(profileService.CurrentPrizeList?.Prizes.Count(prize => prize.IsCandidate) ?? 0);
            _lotteryRoundsMetricValue.Text = FormatCount(profileService.CurrentPrizeHistory?.TotalRounds ?? 0);
        }
        else
        {
            Grid.SetRow(_rollCallRoundsMetricCard, 0);
            Grid.SetColumn(_rollCallRoundsMetricCard, 1);
        }

        MobileAnimations.PlayPageEnter(_pageScroll);
    }

    private static string FormatCount(int value) =>
        value.ToString(System.Globalization.CultureInfo.CurrentCulture);

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
