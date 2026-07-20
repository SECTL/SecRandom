using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Core.Icons;
using SecRandom.Mobile.Views;

namespace SecRandom.Mobile.Controls;

/// <summary>
/// 段落页眉：主色小图标 + 加粗标题（替代旧 MobileUi.CreateLabel 的 IconText 用法）。
/// </summary>
public sealed class MobileSectionHeader : UserControl
{
    public MobileSectionHeader(string text, string? glyph = null)
    {
        var icon = MobileUi.CreateIcon(glyph ?? FluentIcons.AppsListFilled, 16);
        MobileTheme.BindBrush(icon, TextBlock.ForegroundProperty, MobileTheme.Keys.Primary);

        var title = new TextBlock
        {
            Text = text,
            FontSize = MobileTheme.FindDouble("MobileFontSizeBody", 16),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        MobileTheme.BindBrush(title, TextBlock.ForegroundProperty, MobileTheme.Keys.Text);

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = MobileTheme.FindDouble("MobileSpacingSm", 8),
            Margin = new Thickness(0, 6, 0, 0),
            Children = { icon, title }
        };
    }
}
