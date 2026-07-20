using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Core.Controls;
using SecRandom.Mobile.Views;
using AvaloniaButton = Avalonia.Controls.Button;

namespace SecRandom.Mobile.Controls;

/// <summary>
/// 空态占位：图标 + 标题 + 可选描述 + 可选引导按钮。引导按钮应跳转到对应管理界面，
/// 而不是解释平台内部细节。
/// </summary>
public sealed class MobileEmptyState : UserControl
{
    public MobileEmptyState(string glyph, string title, string? description = null, string? actionText = null, Action? action = null)
    {
        var icon = new FluentIcon(glyph, 40)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.72
        };
        MobileTheme.BindBrush(icon, TextElement.ForegroundProperty, MobileTheme.Keys.MutedText);

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = MobileTheme.FindDouble("MobileFontSizeBody", 16),
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        MobileTheme.BindBrush(titleBlock, TextBlock.ForegroundProperty, MobileTheme.Keys.Text);

        var stack = new StackPanel
        {
            Spacing = MobileTheme.FindDouble("MobileSpacingMd", 14),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 48, 0, 24),
            Children = { icon, titleBlock }
        };

        if (!string.IsNullOrEmpty(description))
        {
            var descriptionBlock = new TextBlock
            {
                Text = description,
                FontSize = MobileTheme.FindDouble("MobileFontSizeCaption", 12),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            MobileTheme.BindBrush(descriptionBlock, TextBlock.ForegroundProperty, MobileTheme.Keys.MutedText);
            stack.Children.Add(descriptionBlock);
        }

        if (!string.IsNullOrEmpty(actionText) && action is not null)
        {
            var button = new AvaloniaButton
            {
                Content = actionText,
                MinHeight = MobileTheme.FindDouble("MobileMinHeightPrimary", 48),
                Classes = { "accent" },
                FontWeight = FontWeight.SemiBold,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            button.Click += (_, _) => action();
            stack.Children.Add(button);
        }

        Content = stack;
    }
}
