using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SecRandom.Core.Models.SubConfigs;

namespace SecRandom.Core.Controls;

/// <summary>
///     位置下拉框：收起时是一行当前位置的名称（和原来的下拉框文案完全一致），
///     点开之后展开的是 <see cref="AnchorGridPicker" /> 的 3×3 圆点九宫格。
/// </summary>
/// <remarks>
///     <para>
///         收起的部分看起来就是一个下拉框：左边是 <see cref="Labels" /> 里与当前值同序的文本，
///         右边是下拉箭头，点击（或键盘回车/空格）由 <see cref="Button" /> 自己的 Flyout 展开。
///         展开后点中一格即写回 <see cref="SelectedValue" /> 并收起弹层；用方向键浏览九宫格时弹层保持展开，
///         所以“看位置”和“选位置”两步不会被键盘打断。
///     </para>
///     <para>
///         控件只负责把值、文案、九宫格串起来，格子外观与位置语义分别在
///         <c>Controls/AnchorGridPicker.axaml</c> 和 <see cref="NotificationWindowAnchorLayout" /> 里，
///         下拉框自身的外观在 <c>Controls/AnchorGridDropDown.axaml</c> 里。
///     </para>
/// </remarks>
public sealed class AnchorGridDropDown : Button
{
    /// <summary>当前选中的持久化位置值（九宫格外的值会被收敛成中心）。</summary>
    public static readonly StyledProperty<int> SelectedValueProperty =
        AvaloniaProperty.Register<AnchorGridDropDown, int>(
            nameof(SelectedValue),
            (int)NotificationWindowPosition.Center,
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    ///     九格各自的文本，顺序与 <see cref="NotificationWindowAnchorLayout.GridOrder" /> 一致：
    ///     收起状态显示当前值对应的那一条，九宫格里的每一格拿它当提示与无障碍名称。
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> LabelsProperty =
        AvaloniaProperty.Register<AnchorGridDropDown, IReadOnlyList<string>?>(nameof(Labels));

    private readonly TextBlock _valueText = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private readonly AnchorGridPicker _picker = new();
    private readonly Flyout _flyout;

    static AnchorGridDropDown()
    {
        SelectedValueProperty.Changed.AddClassHandler<AnchorGridDropDown>(
            static (dropdown, _) => dropdown.SyncSelection());
        LabelsProperty.Changed.AddClassHandler<AnchorGridDropDown>(
            static (dropdown, _) => dropdown.SyncSelection());
    }

    public AnchorGridDropDown()
    {
        Content = _valueText;
        _picker.SelectionCommitted += OnPickerSelectionCommitted;

        _flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = new Border
            {
                Padding = new Thickness(12),
                Child = _picker
            }
        };
        // 弹层展开后把焦点放到当前格子上，键盘用户从“现在在哪”开始移动，
        // 而不是从左上角重新走一遍。Post 到 Input 优先级是为了赢过 Flyout 自己的首次聚焦。
        _flyout.Opened += (_, _) =>
            Dispatcher.UIThread.Post(_picker.FocusSelectedCell, DispatcherPriority.Input);
        Flyout = _flyout;

        SyncSelection();
    }

    public int SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    public IReadOnlyList<string>? Labels
    {
        get => GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    private string LabelFor(NotificationWindowPosition value)
    {
        var labels = Labels;
        var index = NotificationWindowAnchorLayout.ToCellIndex((int)value);
        return labels is not null && index < labels.Count ? labels[index] : string.Empty;
    }

    /// <summary>把值归一化后同时落到收起文案、无障碍名称和九宫格的选中格上。</summary>
    private void SyncSelection()
    {
        var value = NotificationWindowAnchorLayout.Normalize(SelectedValue);
        var label = LabelFor(value);
        if (_valueText.Text != label)
            _valueText.Text = label;
        AutomationProperties.SetName(this, label);
        if (_picker.SelectedValue != (int)value)
            _picker.SelectedValue = (int)value;
    }

    private void OnPickerSelectionCommitted(object? sender, EventArgs e)
    {
        SelectedValue = _picker.SelectedValue;
        _flyout.Hide();
    }
}