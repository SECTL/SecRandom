using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Icons;
using SecRandom.Mobile.Views;
using AvaloniaButton = Avalonia.Controls.Button;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Controls;

/// <summary>
/// Mobile compatibility wrapper over FluentAvalonia's Windows-style settings item.
/// </summary>
public class MobileSettingRow : UserControl
{
    private readonly FASettingsExpanderItem _item;

    public MobileSettingRow()
    {
        _item = new FASettingsExpanderItem
        {
            MinHeight = 64,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _item.Click += (_, args) => Click?.Invoke(this, args);
        Content = _item;
    }

    public string Title
    {
        get => _item.Content?.ToString() ?? string.Empty;
        set => _item.Content = value;
    }

    public string? Description
    {
        get => _item.Description;
        set => _item.Description = value;
    }

    public Control? Footer
    {
        get => _item.Footer as Control;
        set => _item.Footer = value;
    }

    public bool IsClickEnabled
    {
        get => _item.IsClickEnabled;
        set => _item.IsClickEnabled = value;
    }

    public event EventHandler<RoutedEventArgs>? Click;

    internal static MobileSettingRow Toggle(string title, string? description, bool value, Action<bool> setValue)
    {
        var toggle = new ToggleSwitch
        {
            IsChecked = value,
            OnContent = string.Empty,
            OffContent = string.Empty,
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.IsCheckedChanged += (_, _) => setValue(toggle.IsChecked == true);
        return new MobileSettingRow { Title = title, Description = description, Footer = toggle };
    }

    internal static MobileSettingRow Integer(string title, string? description, int value, int minimum, Action<int> setValue)
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
            if (!int.TryParse(editor.Text, out int parsed))
                parsed = value;
            parsed = Math.Max(minimum, parsed);
            editor.Text = parsed.ToString(System.Globalization.CultureInfo.CurrentCulture);
            setValue(parsed);
        };
        return new MobileSettingRow { Title = title, Description = description, Footer = editor };
    }

    internal static MobileSettingRow Choice(string text, bool selected, Action select, string? groupName = null)
    {
        var radio = new RadioButton
        {
            IsChecked = selected,
            GroupName = groupName ?? "mobile-choice-" + Guid.NewGuid().ToString("N"),
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new MobileSettingRow { Title = text, Footer = radio, IsClickEnabled = true };
        row.Click += (_, _) => select();
        return row;
    }

    internal static MobileSettingRow Navigation(string title, string? description, Action open)
    {
        var row = new MobileSettingRow { Title = title, Description = description, IsClickEnabled = true };
        row._item.ActionIconSource = MobileUi.CreateIconSource(FluentIcons.ChevronRightFilled);
        row.Click += (_, _) => open();
        return row;
    }

    internal static MobileSettingRow Simple(string title, string? detail, Control? trailing = null, Action? remove = null)
    {
        Control? footer = trailing;
        if (remove is not null)
        {
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = MobileTheme.FindDouble("MobileSpacingXs", 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            if (trailing is not null)
                actions.Children.Add(trailing);
            var removeButton = new AvaloniaButton
            {
                Content = MobileUi.CreateIcon(FluentIcons.DeleteFilled, 18, HorizontalAlignment.Center),
                [ToolTip.TipProperty] = LR.C_Remove,
                Classes = { "compact" }
            };
            removeButton.Click += (_, _) => remove();
            actions.Children.Add(removeButton);
            footer = actions;
        }
        return new MobileSettingRow { Title = title, Description = detail, Footer = footer };
    }
}
