using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Icons;
using SecRandom.Mobile.Controls;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;
using AvaloniaButton = Avalonia.Controls.Button;

namespace SecRandom.Mobile.Views;

internal static class MobileUi
{
    private static readonly FontFamily IconFont =
        new("avares://SecRandom.Mobile/Assets/Fonts/#FluentSystemIcons-Resizable");

    internal static FAFontIconSource CreateIconSource(string glyph) => new()
    {
        Glyph = glyph,
        FontFamily = IconFont
    };

    internal static TextBlock CreateIcon(
        string glyph,
        double size,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left) => new()
    {
        Text = glyph,
        FontFamily = IconFont,
        FontSize = size,
        LineHeight = size,
        HorizontalAlignment = horizontalAlignment,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center
    };

    internal static AvaloniaButton CreateSegmentButton(string text, bool selected, bool left)
    {
        var button = new AvaloniaButton
        {
            Content = text,
            Padding = new Thickness(18, 7),
            Classes = { selected ? "accent" : "compact" },
            CornerRadius = left ? new CornerRadius(15, 4, 4, 15) : new CornerRadius(4, 15, 15, 4),
            FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal
        };
        MobileTheme.BindBrush(button, AvaloniaButton.ForegroundProperty,
            selected ? MobileTheme.Keys.Primary : MobileTheme.Keys.MutedText);
        return button;
    }

    internal static ScrollViewer CreateScroll(IEnumerable<Control> items)
    {
        var panel = new StackPanel
        {
            Spacing = MobileTheme.FindDouble("MobileSpacingMd", 14),
            MaxWidth = 720
        };
        foreach (var item in items)
            panel.Children.Add(item);
        return new ScrollViewer
        {
            Content = panel,
            Padding = MobileTheme.FindThickness("MobilePagePadding", new Thickness(20, 20, 20, 28)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsScrollChainingEnabled = false,
            IsScrollInertiaEnabled = true
        };
    }

    internal static Control CreateSettingsScroll(string title, string description, Action goBack, IEnumerable<Control> items)
    {
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = MobileTheme.FindDouble("MobileFontSizeSection", 20),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        MobileTheme.BindBrush(titleBlock, TextBlock.ForegroundProperty, MobileTheme.Keys.Text);
        var descriptionBlock = new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap
        };
        MobileTheme.BindBrush(descriptionBlock, TextBlock.ForegroundProperty, MobileTheme.Keys.MutedText);
        var content = new List<Control>
        {
            CreateSecondaryButton(LR.C_Back, goBack),
            new MobileCard(MobileTheme.Keys.PrimaryWash)
            {
                BorderThickness = new Thickness(0),
                Content = new StackPanel
                {
                    Spacing = MobileTheme.FindDouble("MobileSpacingXs", 4),
                    Children = { titleBlock, descriptionBlock }
                }
            }
        };
        content.AddRange(items);
        return CreateScroll(content);
    }

    internal static Control CreateNavigationRow(string title, string description, Action open) =>
        MobileSettingRow.Navigation(title, description, open);

    internal static Control CreateChoiceRow(string text, bool selected, Action select, string? groupName = null) =>
        MobileSettingRow.Choice(text, selected, select, groupName);

    internal static Control CreateToggleRow(string label, bool value, Action<bool> setValue) =>
        MobileSettingRow.Toggle(label, value ? LR.M_Enabled : LR.M_Disabled, value, setValue);

    internal static Control CreateIntegerRow(string label, int value, int minimum, Action<int> setValue) =>
        MobileSettingRow.Integer(label, null, value, minimum, setValue);

    internal static Control CreateLabel(string text) => new MobileSectionHeader(text);

    internal static TextBlock CreateTitle(string text)
    {
        var title = new TextBlock
        {
            Text = text,
            FontSize = MobileTheme.FindDouble("MobileFontSizeTitle", 24),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        MobileTheme.BindBrush(title, TextBlock.ForegroundProperty, MobileTheme.Keys.Text);
        return title;
    }

    internal static Control CreateResultPanel(Control result, Control detail, IBrush color)
    {
        var card = new MobileCard
        {
            Background = color,
            BorderThickness = new Thickness(0),
            CornerRadius = MobileTheme.FindCornerRadius("MobileCornerRadiusLarge", new CornerRadius(18)),
            Padding = new Thickness(24, 28)
        };
        card.Content = CreateResultStack(result, detail);
        return card;
    }

    // 主题感知重载：wash 底色走 DynamicResource，切主题时随资源系统刷新。
    internal static Control CreateResultPanel(Control result, Control detail, string washResourceKey, Control? media = null)
    {
        var card = new MobileCard(washResourceKey)
        {
            BorderThickness = new Thickness(0),
            CornerRadius = MobileTheme.FindCornerRadius("MobileCornerRadiusLarge", new CornerRadius(18)),
            Padding = new Thickness(24, 28)
        };
        card.Content = CreateResultStack(result, detail, media);
        return card;
    }

    private static Control CreateResultStack(Control result, Control detail, Control? media = null)
    {
        var stack = new StackPanel
        {
            Spacing = MobileTheme.FindDouble("MobileSpacingSm", 8),
            Children = { result, detail }
        };
        if (media is not null)
            stack.Children.Add(media);
        if (result is Layoutable layoutableResult)
            layoutableResult.HorizontalAlignment = HorizontalAlignment.Center;
        if (detail is Layoutable layoutableDetail)
            layoutableDetail.HorizontalAlignment = HorizontalAlignment.Center;
        return stack;
    }

    internal static AvaloniaButton CreatePrimaryButton(string text, bool enabled, Action? onClick = null)
    {
        var button = new AvaloniaButton
        {
            Content = text,
            IsEnabled = enabled,
            MinHeight = MobileTheme.FindDouble("MobileMinHeightPrimary", 48),
            Classes = { "accent" },
            FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        if (onClick is not null)
            button.Click += (_, _) => onClick();
        return button;
    }

    internal static AvaloniaButton CreateSecondaryButton(string text, Action onClick)
    {
        var button = new AvaloniaButton
        {
            Content = text,
            MinHeight = MobileTheme.FindDouble("MobileMinHeightSecondary", 44),
            FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    internal static Border CreateMetric(string label, string value, IBrush color)
    {
        return new Border
        {
            Background = color,
            CornerRadius = MobileTheme.FindCornerRadius("MobileCornerRadiusSmall", new CornerRadius(10)),
            Padding = new Thickness(16),
            Child = CreateMetricGrid(label, value)
        };
    }

    // 主题感知重载：wash 底色走 DynamicResource，切主题时随资源系统刷新。
    internal static Control CreateMetric(string label, string value, string washResourceKey)
    {
        var card = new MobileCard(washResourceKey)
        {
            BorderThickness = new Thickness(0),
            CornerRadius = MobileTheme.FindCornerRadius("MobileCornerRadiusSmall", new CornerRadius(10)),
            Padding = new Thickness(16)
        };
        card.Content = CreateMetricGrid(label, value);
        return card;
    }

    private static Grid CreateMetricGrid(string label, string value)
    {
        var valueText = new TextBlock
        {
            Text = value,
            // 指标数值沿用历史 28pt，比结果页 34pt 低一级。
            FontSize = 28,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        MobileTheme.BindBrush(valueText, TextBlock.ForegroundProperty, MobileTheme.Keys.Text);
        var labelText = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        };
        MobileTheme.BindBrush(labelText, TextBlock.ForegroundProperty, MobileTheme.Keys.MutedText);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(labelText);
        grid.Children.Add(valueText);
        Grid.SetColumn(valueText, 1);
        return grid;
    }

    internal static Control CreateRow(string title, string detail, Control? trailing = null, Action? remove = null) =>
        MobileSettingRow.Simple(title, detail, trailing, remove);

    internal static string Format(string id, string name) => string.IsNullOrWhiteSpace(id)
        ? name
        : string.IsNullOrWhiteSpace(name) ? id : $"{id}  {name}";
}
