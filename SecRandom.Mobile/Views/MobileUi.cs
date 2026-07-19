using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Core.Controls;
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
        Padding = new Thickness(2, 4),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        FontSize = 12,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    internal static AvaloniaButton CreateSegmentButton(string text, bool selected, bool left) => new()
    {
        Content = text,
        Padding = new Thickness(18, 7),
        Background = selected ? MobileTheme.Surface : Brushes.Transparent,
        Foreground = selected ? MobileTheme.Primary : MobileTheme.MutedText,
        BorderThickness = new Thickness(0),
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
            CreateLabel(title),
            CreateTitle(title),
            new TextBlock { Text = description, Foreground = MobileTheme.MutedText, TextWrapping = TextWrapping.Wrap }
        };
        content.AddRange(items);
        return CreateScroll(content);
    }

    internal static Control CreateNavigationRow(string title, string description, Action open)
    {
        var row = new AvaloniaButton
        {
            Background = MobileTheme.Surface,
            BorderBrush = MobileTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*"),
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock { Text = title, FontWeight = FontWeight.Medium, Foreground = MobileTheme.Text, TextWrapping = TextWrapping.Wrap },
                            new TextBlock { Text = description, FontSize = 12, Foreground = MobileTheme.MutedText, TextWrapping = TextWrapping.Wrap }
                        }
                    }
                }
            }
        };
        row.Click += (_, _) => open();
        return row;
    }

    internal static Control CreateChoiceRow(string text, bool selected, Action select)
    {
        var button = new AvaloniaButton
        {
            Content = text,
            Background = selected ? MobileTheme.PrimaryWash : MobileTheme.Surface,
            Foreground = selected ? MobileTheme.Primary : MobileTheme.Text,
            BorderBrush = selected ? MobileTheme.Primary : MobileTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal
        };
        button.Click += (_, _) => select();
        return button;
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
        return CreateRow(label, value ? LR.M_Enabled : LR.M_Disabled, toggle);
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
        return CreateRow(label, string.Empty, editor);
    }

    internal static TextBlock CreateLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        Foreground = MobileTheme.Primary
    };

    internal static TextBlock CreateTitle(string text) => new()
    {
        Text = text,
        FontSize = 24,
        FontWeight = FontWeight.SemiBold,
        Foreground = MobileTheme.Text,
        TextWrapping = TextWrapping.Wrap
    };

    internal static Border CreateResultPanel(Control result, Control detail, IBrush color) => new()
    {
        Background = color,
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(24, 38),
        Child = new StackPanel { Spacing = 12, Children = { result, detail } }
    };

    internal static AvaloniaButton CreatePrimaryButton(string text, bool enabled, Action? onClick = null)
    {
        var button = new AvaloniaButton
        {
            Content = text,
            IsEnabled = enabled,
            MinHeight = 48,
            Background = MobileTheme.Primary,
            Foreground = MobileTheme.Surface,
            BorderThickness = new Thickness(0),
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
            Background = Brushes.Transparent,
            Foreground = MobileTheme.Primary,
            BorderBrush = MobileTheme.Primary,
            BorderThickness = new Thickness(1),
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

    internal static Border CreateRow(string title, string detail, Control? trailing = null, Action? remove = null)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Medium, Foreground = MobileTheme.Text, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(detail))
            text.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = MobileTheme.MutedText, TextWrapping = TextWrapping.Wrap });

        var actions = new StackPanel { Orientation = AvaloniaOrientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (trailing is not null)
            actions.Children.Add(trailing);
        if (remove is not null)
        {
            var removeButton = new AvaloniaButton
            {
                Content = LR.C_Remove,
                Background = Brushes.Transparent,
                Foreground = MobileTheme.Danger,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4)
            };
            removeButton.Click += (_, _) => remove();
            actions.Children.Add(removeButton);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(text);
        grid.Children.Add(actions);
        Grid.SetColumn(actions, 1);
        return new Border
        {
            Background = MobileTheme.Surface,
            BorderBrush = MobileTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12),
            Child = grid
        };
    }

    internal static string Format(string id, string name) => string.IsNullOrWhiteSpace(id)
        ? name
        : string.IsNullOrWhiteSpace(name) ? id : $"{id}  {name}";
}
