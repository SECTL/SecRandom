using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Core.Icons;
using SecRandom.Core.Views;
using SecRandom.Mobile.Views;
using AvaloniaButton = Avalonia.Controls.Button;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

/// <summary>
/// 移动设置页公共骨架：收口各设置页重复的 CloseView + CreateSettingsScroll 样板。
/// 页眉使用轻量标题/描述排版（不再用 FAInfoBar 当页眉），内容滚动与旧工厂一致，
/// 进入时播放页面进入过渡。
/// </summary>
public abstract class MobileSettingsPageBase : ViewBase
{
    protected void CloseView() => _ = CloseAsync(reason: ViewCloseReason.Back);

    // 能力投影直通属性，页面统一经由基类消费（详见 MobileCapabilities）。
    protected static bool IsLotteryEnabled => MobileCapabilities.IsLotteryEnabled;
    protected static bool IsAndroid => MobileCapabilities.IsAndroid;
    protected static bool SupportsInAppUpdate => MobileCapabilities.SupportsInAppUpdate;

    // showBackButton=false 供设置目录这类根级页面使用：它们是底部导航目的地，没有可返回的上级设置页。
    protected Control BuildPage(string title, string description, IEnumerable<Control> items, bool showBackButton = true)
    {
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = MobileTheme.FindDouble("MobileFontSizeTitle", 24),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        MobileTheme.BindBrush(titleBlock, TextBlock.ForegroundProperty, MobileTheme.Keys.Text);

        var descriptionBlock = new TextBlock
        {
            Text = description,
            FontSize = MobileTheme.FindDouble("MobileFontSizeCaption", 12),
            TextWrapping = TextWrapping.Wrap
        };
        MobileTheme.BindBrush(descriptionBlock, TextBlock.ForegroundProperty, MobileTheme.Keys.MutedText);

        var backButton = new AvaloniaButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    MobileUi.CreateIcon(FluentIcons.ArrowLeftFilled, 16),
                    new TextBlock { Text = LR.C_Back, VerticalAlignment = VerticalAlignment.Center }
                }
            },
            HorizontalAlignment = HorizontalAlignment.Left,
            MinHeight = MobileTheme.FindDouble("MobileMinHeightSecondary", 44)
        };
        backButton.Click += (_, _) => CloseView();

        var content = new List<Control>();
        if (showBackButton)
            content.Add(backButton);
        content.Add(new StackPanel
        {
            Spacing = MobileTheme.FindDouble("MobileSpacingXs", 4),
            Children = { titleBlock, descriptionBlock }
        });
        content.AddRange(items);

        ScrollViewer scroll = MobileUi.CreateScroll(content);
        MobileAnimations.PlayPageEnter(scroll);
        return scroll;
    }
}
