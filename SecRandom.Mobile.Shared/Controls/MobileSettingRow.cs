using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Core.Icons;
using SecRandom.Mobile.Views;
using AvaloniaButton = Avalonia.Controls.Button;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Controls;

/// <summary>
/// 设置行三段式控件：标题 / 描述 / Footer。收敛旧 MobileUi 的
/// CreateToggleRow / CreateIntegerRow / CreateChoiceRow / CreateNavigationRow / CreateRow。
/// </summary>
public class MobileSettingRow : UserControl
{
    private readonly Border _root;
    private readonly TextBlock _title;
    private readonly TextBlock _description;
    private readonly Border _footerHost;
    private bool _isClickEnabled;

    public MobileSettingRow()
    {
        _title = new TextBlock
        {
            FontSize = MobileTheme.FindDouble("MobileFontSizeBody", 16),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        MobileTheme.BindBrush(_title, TextBlock.ForegroundProperty, MobileTheme.Keys.Text);

        _description = new TextBlock
        {
            FontSize = MobileTheme.FindDouble("MobileFontSizeCaption", 12),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        MobileTheme.BindBrush(_description, TextBlock.ForegroundProperty, MobileTheme.Keys.MutedText);

        _footerHost = new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            IsVisible = false
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Children = { _title, _description, _footerHost }
        };
        Grid.SetRow(_description, 1);
        Grid.SetColumn(_footerHost, 1);
        Grid.SetRowSpan(_footerHost, 2);

        _root = new Border
        {
            CornerRadius = MobileTheme.FindCornerRadius("MobileCornerRadiusMedium", new CornerRadius(15)),
            Padding = MobileTheme.FindThickness("MobileCardPadding", new Thickness(16)),
            BorderThickness = new Thickness(1),
            Child = grid
        };
        MobileTheme.BindBrush(_root, Border.BackgroundProperty, MobileTheme.Keys.Surface);
        MobileTheme.BindBrush(_root, Border.BorderBrushProperty, MobileTheme.Keys.Border);

        Content = _root;
    }

    public string Title
    {
        get => _title.Text ?? string.Empty;
        set => _title.Text = value;
    }

    public string? Description
    {
        get => _description.Text;
        set
        {
            _description.Text = value;
            _description.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    public Control? Footer
    {
        get => _footerHost.Child;
        set
        {
            _footerHost.Child = value;
            _footerHost.IsVisible = value is not null;
        }
    }

    public bool IsClickEnabled
    {
        get => _isClickEnabled;
        set
        {
            _isClickEnabled = value;
            _root.Cursor = value ? new Cursor(StandardCursorType.Hand) : null;
        }
    }

    public event EventHandler<RoutedEventArgs>? Click;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_isClickEnabled)
            _root.Opacity = 0.72;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isClickEnabled)
            return;
        _root.Opacity = 1;
        Click?.Invoke(this, new RoutedEventArgs());
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_isClickEnabled)
            _root.Opacity = 1;
    }

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
        // 每次调用默认生成唯一组名：历史实现共用固定 GroupName，导致同屏不同设置组的
        // RadioButton 跨组互相取消勾选（视觉选中态与实际配置不一致）。
        var radio = new RadioButton
        {
            IsChecked = selected,
            GroupName = groupName ?? "mobile-choice-" + Guid.NewGuid().ToString("N"),
            // 命中测试交给整行处理，行点击即选中，避免 RadioButton 自身的独立切换与行行为分裂。
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new MobileSettingRow { Title = text, Footer = radio, IsClickEnabled = true };
        row.Click += (_, _) => select();
        return row;
    }

    internal static MobileSettingRow Navigation(string title, string? description, Action open)
    {
        var chevron = MobileUi.CreateIcon(FluentIcons.ChevronRightFilled, 16);
        MobileTheme.BindBrush(chevron, TextBlock.ForegroundProperty, MobileTheme.Keys.MutedText);
        var row = new MobileSettingRow { Title = title, Description = description, Footer = chevron, IsClickEnabled = true };
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
