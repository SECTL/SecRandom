using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Controls;
using SecRandom.Core.Icons;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;
using AvaloniaButton = Avalonia.Controls.Button;
using AvaloniaOrientation = Avalonia.Layout.Orientation;

namespace SecRandom.Mobile.Views;

internal static class MobileUi
{
    internal static AvaloniaButton CreateNavigationButton(string text, string icon) => new()
    {
        Content = new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new FluentIcon(icon, 20) { HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center }
            }
        },
        Padding = new Thickness(8, 6),
        FontSize = 12,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    internal static AvaloniaButton CreateSegmentButton(string text, bool selected, bool left) => new()
    {
        Content = text,
        Padding = new Thickness(18, 7),
        Classes = { selected ? "accent" : "compact" },
        Foreground = selected ? MobileTheme.Primary : MobileTheme.MutedText,
        CornerRadius = left ? new CornerRadius(15, 4, 4, 15) : new CornerRadius(4, 15, 15, 4),
        FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal
    };

    internal static ScrollViewer CreateScroll(IEnumerable<Control> items)
    {
        var panel = new StackPanel { Spacing = 14, MaxWidth = 720 };
        foreach (var item in items)
            panel.Children.Add(item);
        return new ScrollViewer
        {
            Content = panel,
            Padding = new Thickness(20, 20, 20, 28),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    internal static Control CreateSettingsScroll(string title, string description, Action goBack, IEnumerable<Control> items)
    {
        var content = new List<Control>
        {
            CreateSecondaryButton(LR.C_Back, goBack),
            new FAInfoBar
            {
                Title = title,
                Message = description,
                Severity = FAInfoBarSeverity.Informational,
                IsOpen = true,
                IsClosable = false,
                IconSource = new FluentIconSource(FluentIcons.InfoFilled)
            }
        };
        content.AddRange(items);
        return CreateScroll(content);
    }

    internal static Control CreateNavigationRow(string title, string description, Action open)
    {
        var row = new FASettingsExpander
        {
            Header = title,
            Description = description,
            IsClickEnabled = true,
            ActionIconSource = new FluentIconSource(FluentIcons.OpenFilled),
            IconSource = new FluentIconSource(FluentIcons.AppsFilled)
        };
        row.Click += (_, _) => open();
        return row;
    }

    internal static Control CreateChoiceRow(string text, bool selected, Action select)
    {
        var option = new FASettingsExpanderItem
        {
            Content = text,
            IsClickEnabled = true,
            ActionIconSource = new FluentIconSource(FluentIcons.CheckmarkFilled),
            Footer = new RadioButton { IsChecked = selected, GroupName = "mobile-choice" }
        };
        option.Click += (_, _) => select();
        ((RadioButton)option.Footer).Click += (_, _) => select();
        return option;
    }

    internal static Control CreateToggleRow(string label, bool value, Action<bool> setValue)
    {
        var toggle = new ToggleSwitch
        {
            IsChecked = value,
            OnContent = string.Empty,
            OffContent = string.Empty,
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.IsCheckedChanged += (_, _) => setValue(toggle.IsChecked == true);
        return new FASettingsExpanderItem
        {
            Content = label,
            Description = value ? LR.M_Enabled : LR.M_Disabled,
            Footer = toggle
        };
    }

    internal static Control CreateIntegerRow(string label, int value, int minimum, Action<int> setValue)
    {
        var editor = new TextBox
        {
            Text = value.ToString(System.Globalization.CultureInfo.CurrentCulture),
            Width = 84,
            MinHeight = 40,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        editor.LostFocus += (_, _) =>
        {
            if (!int.TryParse(editor.Text, out var parsed))
                parsed = value;
            parsed = Math.Max(minimum, parsed);
            editor.Text = parsed.ToString(System.Globalization.CultureInfo.CurrentCulture);
            setValue(parsed);
        };
        return new FASettingsExpanderItem
        {
            Content = label,
            Footer = editor
        };
    }

    internal static Control CreateLabel(string text) => new IconText
    {
        Text = text,
        Glyph = FluentIcons.AppsListFilled,
        Spacing = 6,
        Margin = new Thickness(0, 6, 0, 0)
    };

    internal static TextBlock CreateTitle(string text) => new()
    {
        Text = text,
        FontSize = 24,
        FontWeight = FontWeight.SemiBold,
        Foreground = MobileTheme.Text,
        TextWrapping = TextWrapping.Wrap
    };

    internal static Control CreateResultPanel(Control result, Control detail, IBrush color) => new FASettingsExpanderItem
    {
        Content = new Border
        {
            Background = color,
            Padding = new Thickness(24, 28),
            Child = result
        },
        Description = LR.M_DrawCompleted,
        IsClickEnabled = false,
        Footer = detail
    };

    internal static AvaloniaButton CreatePrimaryButton(string text, bool enabled, Action? onClick = null)
    {
        var button = new AvaloniaButton
        {
            Content = text,
            IsEnabled = enabled,
            MinHeight = 48,
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
            MinHeight = 44,
            FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    internal static Border CreateMetric(string label, string value, IBrush color)
    {
        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 28,
            FontWeight = FontWeight.SemiBold,
            Foreground = MobileTheme.Text,
            VerticalAlignment = VerticalAlignment.Center
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock { Text = label, Foreground = MobileTheme.MutedText, VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(valueText);
        Grid.SetColumn(valueText, 1);
        return new Border { Background = color, CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Child = grid };
    }

    internal static Control CreateRow(string title, string detail, Control? trailing = null, Action? remove = null)
    {
        var actions = new StackPanel { Orientation = AvaloniaOrientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (trailing is not null)
            actions.Children.Add(trailing);
        if (remove is not null)
        {
            var removeButton = new AvaloniaButton
            {
                Content = new FluentIcon(FluentIcons.DeleteFilled, 18),
                [ToolTip.TipProperty] = LR.C_Remove,
                Classes = { "compact" }
            };
            removeButton.Click += (_, _) => remove();
            actions.Children.Add(removeButton);
        }

        return new FASettingsExpanderItem
        {
            Content = title,
            Description = detail,
            Footer = actions
        };
    }

    internal static string Format(string id, string name) => string.IsNullOrWhiteSpace(id)
        ? name
        : string.IsNullOrWhiteSpace(name) ? id : $"{id}  {name}";
}
