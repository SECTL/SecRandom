using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Icons;
using SecRandom.Mobile.Controls;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views;

/// <summary>
/// 概览页：2×2 指标网格（学生数 / 奖品数 / 点名轮次 / 抽奖轮次）。
/// 抽奖相关两项按启动时注入的功能能力投影，关闭时不占位。
/// </summary>
public sealed partial class MobileOverviewPage : UserControl
{
    public MobileOverviewPage(IProfileService profileService, IMobileCapabilities capabilities)
    {
        InitializeComponent();
        var metrics = new List<(string Label, string Value, string Glyph)>
        {
            (LR.M_Students,
                FormatCount(profileService.CurrentStudentList?.Students.Count(student => student.IsCandidate) ?? 0),
                FluentIcons.PeopleFilled)
        };
        if (capabilities.IsLotteryEnabled)
        {
            metrics.Add((LR.M_Prizes,
                FormatCount(profileService.CurrentPrizeList?.Prizes.Count(prize => prize.IsCandidate) ?? 0),
                FluentIcons.GiftFilled));
        }
        metrics.Add((LR.M_RollCallRounds,
            FormatCount(profileService.CurrentStudentHistory?.TotalRounds ?? 0),
            FluentIcons.TargetFilled));
        if (capabilities.IsLotteryEnabled)
        {
            metrics.Add((LR.M_LotteryRounds,
                FormatCount(profileService.CurrentPrizeHistory?.TotalRounds ?? 0),
                FluentIcons.TrophyFilled));
        }

        var grid = this.FindControl<Grid>("MetricsGrid")!;
        // 按行优先顺序填充：抽奖关闭时剩余两项自动重排为一行，不留空位。
        for (var i = 0; i < metrics.Count; i++)
        {
            Control card = CreateMetricCard(metrics[i]);
            grid.Children.Add(card);
            Grid.SetRow(card, i / 2);
            Grid.SetColumn(card, i % 2);
        }

        MobileAnimations.PlayPageEnter(this.FindControl<ScrollViewer>("PageScroll")!);
    }

    private static string FormatCount(int value) =>
        value.ToString(System.Globalization.CultureInfo.CurrentCulture);

    private static Control CreateMetricCard((string Label, string Value, string Glyph) metric)
    {
        var icon = MobileViewFactory.CreateIcon(metric.Glyph, 20);
        var value = new TextBlock
        {
            Text = metric.Value,
            // 指标数值沿用历史 28pt，比结果页 34pt 低一级。
            FontSize = 28,
            FontWeight = FontWeight.SemiBold
        };
        var label = new TextBlock
        {
            Text = metric.Label,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };

        return new MobileCard
        {
            BorderThickness = new Thickness(0),
            Content = new StackPanel
            {
                Spacing = 4,
                Children = { icon, value, label }
            }
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
