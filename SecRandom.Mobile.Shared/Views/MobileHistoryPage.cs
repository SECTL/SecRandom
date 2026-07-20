using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Icons;
using SecRandom.Core.Views;
using SecRandom.Mobile.Controls;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views;

/// <summary>
/// 历史页：最近 30 条抽取记录卡片化（类型图标 + 名称 + 相对时间），
/// 空态使用 <see cref="MobileEmptyState"/> 并引导回抽取页。
/// </summary>
public sealed class MobileHistoryPage : ViewBase
{
    private readonly IProfileService _profileService;
    private readonly IHistoryQueryService _historyQueryService;
    private readonly IDrawTemporaryRecordService _temporaryRecordService;
    private readonly IViewEngine _viewEngine;
    private bool _firstRender = true;

    public MobileHistoryPage(
        IProfileService profileService,
        IHistoryQueryService historyQueryService,
        IDrawTemporaryRecordService temporaryRecordService,
        IViewEngine viewEngine)
    {
        _profileService = profileService;
        _historyQueryService = historyQueryService;
        _temporaryRecordService = temporaryRecordService;
        _viewEngine = viewEngine;
        Render();
    }

    private void Render()
    {
        var entries = _historyQueryService.GetRecentItems(30);

        Control body;
        if (entries.Count == 0)
        {
            body = new MobileEmptyState(
                FluentIcons.HistoryFilled,
                LR.M_NoHistory,
                LR.M_NoHistoryPrompt,
                LR.C_GoToDraw,
                () => _ = _viewEngine.ShowAsync(MobileRoutes.Draw));
        }
        else
        {
            var list = new StackPanel { Spacing = MobileTheme.FindDouble("MobileSpacingSm", 8) };
            foreach (var item in entries)
                list.Children.Add(CreateHistoryCard(item));
            body = list;
        }

        ScrollViewer scroll = MobileUi.CreateScroll([
            MobileUi.CreateLabel(LR.S_RecentDraws),
            MobileUi.CreateTitle(LR.P_History),
            body,
            MobileUi.CreateSecondaryButton(LR.C_ClearTemporaryRecords, ClearTemporaryRecords)
        ]);
        Content = scroll;

        // 清记录后的重建不重复播放进入过渡。
        if (_firstRender)
        {
            _firstRender = false;
            MobileAnimations.PlayPageEnter(scroll);
        }
    }

    private void ClearTemporaryRecords()
    {
        _temporaryRecordService.ClearStudentList(_profileService.StudentListConfig?.Name ?? MobileDefaults.ProfileName);
        // 抽奖关闭时不触碰奖池临时记录，与能力投影口径一致。
        if (MobileCapabilities.IsLotteryEnabled)
            _temporaryRecordService.ClearPrizeList(_profileService.PrizeListConfig?.Name ?? MobileDefaults.ProfileName);
        Render();
    }

    private static Control CreateHistoryCard(HistoryQueryItem item)
    {
        var icon = MobileUi.CreateIcon(
            item.IsPrize ? FluentIcons.GiftFilled : FluentIcons.PersonFilled,
            18,
            HorizontalAlignment.Center);
        MobileTheme.BindBrush(icon, TextBlock.ForegroundProperty, MobileTheme.Keys.Primary);
        var iconBadge = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = MobileTheme.FindCornerRadius("MobileCornerRadiusSmall", new CornerRadius(10)),
            Child = icon
        };
        MobileTheme.BindBrush(iconBadge, Border.BackgroundProperty,
            item.IsPrize ? MobileTheme.Keys.WarmWash : MobileTheme.Keys.PrimaryWash);

        var name = new TextBlock
        {
            Text = item.DisplayName,
            FontSize = MobileTheme.FindDouble("MobileFontSizeBody", 16),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        MobileTheme.BindBrush(name, TextBlock.ForegroundProperty, MobileTheme.Keys.Text);
        var time = new TextBlock
        {
            Text = FormatRelativeTime(item.DrawTime),
            FontSize = MobileTheme.FindDouble("MobileFontSizeCaption", 12),
            TextWrapping = TextWrapping.Wrap
        };
        MobileTheme.BindBrush(time, TextBlock.ForegroundProperty, MobileTheme.Keys.MutedText);

        var texts = new StackPanel
        {
            Spacing = MobileTheme.FindDouble("MobileSpacingXs", 4),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { name, time }
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = MobileTheme.FindDouble("MobileSpacingMd", 14),
            Children = { iconBadge, texts }
        };
        Grid.SetColumn(texts, 1);
        return new MobileCard { Content = grid };
    }

    private static string FormatRelativeTime(DateTime drawTime)
    {
        TimeSpan elapsed = DateTime.Now - drawTime;
        if (elapsed < TimeSpan.FromMinutes(1))
            return LR.M_TimeJustNow;
        if (elapsed < TimeSpan.FromHours(1))
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_TimeMinutesAgo, (int)elapsed.TotalMinutes);
        if (elapsed < TimeSpan.FromDays(1))
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_TimeHoursAgo, (int)elapsed.TotalHours);
        if (elapsed < TimeSpan.FromDays(7))
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_TimeDaysAgo, (int)elapsed.TotalDays);
        return drawTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
    }
}
