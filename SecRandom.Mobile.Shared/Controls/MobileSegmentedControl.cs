using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Mobile.Views;
using AvaloniaButton = Avalonia.Controls.Button;

namespace SecRandom.Mobile.Controls;

/// <summary>
/// 胶囊分段选择器（替代旧 MobileUi.CreateSegmentButton 的手工拼圆角 + 外壳 Border）。
/// 选中项使用 PrimaryWash 底 + Primary 文字，未选中项透明底 + 弱化文字，均走 DynamicResource。
/// </summary>
public sealed class MobileSegmentedControl : UserControl
{
    private readonly StackPanel _panel;
    private readonly List<AvaloniaButton> _buttons = [];
    private readonly List<(string Text, object? Tag, bool Enabled)> _items = [];
    private int _selectedIndex = -1;

    public MobileSegmentedControl()
    {
        _panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2
        };
        var capsule = new Border
        {
            CornerRadius = MobileTheme.FindCornerRadius("MobileCornerRadiusLarge", new CornerRadius(18)),
            Padding = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _panel
        };
        MobileTheme.BindBrush(capsule, Border.BackgroundProperty, MobileTheme.Keys.SurfaceMuted);
        Content = capsule;
    }

    public event EventHandler? SelectionChanged;

    public int SelectedIndex => _selectedIndex;

    public object? SelectedTag => _selectedIndex >= 0 && _selectedIndex < _items.Count
        ? _items[_selectedIndex].Tag
        : null;

    public void SetItems(IEnumerable<(string Text, object? Tag, bool Enabled)> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = _items.Count > 0 ? 0 : -1;
        RebuildButtons();
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _items.Count || index == _selectedIndex)
            return;
        if (!_items[index].Enabled)
            return;
        _selectedIndex = index;
        ApplyVisuals();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildButtons()
    {
        _panel.Children.Clear();
        _buttons.Clear();
        for (var i = 0; i < _items.Count; i++)
        {
            var index = i;
            var button = new AvaloniaButton
            {
                Content = _items[i].Text,
                Padding = new Thickness(18, 7),
                IsEnabled = _items[i].Enabled,
                CornerRadius = SegmentCornerRadius(i, _items.Count)
            };
            button.Click += (_, _) => Select(index);
            _buttons.Add(button);
            _panel.Children.Add(button);
        }
        ApplyVisuals();
    }

    private void ApplyVisuals()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            var selected = i == _selectedIndex;
            _buttons[i].FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
            MobileTheme.BindBrush(_buttons[i], AvaloniaButton.ForegroundProperty,
                selected ? MobileTheme.Keys.Primary : MobileTheme.Keys.MutedText);
            if (selected)
                MobileTheme.BindBrush(_buttons[i], AvaloniaButton.BackgroundProperty, MobileTheme.Keys.PrimaryWash);
            else
                _buttons[i].Background = Brushes.Transparent;
        }
    }

    private static CornerRadius SegmentCornerRadius(int index, int count)
    {
        if (count <= 1)
            return new CornerRadius(15);
        if (index == 0)
            return new CornerRadius(15, 4, 4, 15);
        return index == count - 1 ? new CornerRadius(4, 15, 15, 4) : new CornerRadius(4);
    }
}
