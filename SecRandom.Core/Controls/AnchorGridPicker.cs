using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Core.Models.SubConfigs;

namespace SecRandom.Core.Controls;

/// <summary>
///     点圆点选位置：3×3 九宫格锚点选择器，点哪一格就选中哪一格对应的浮窗位置。
/// </summary>
/// <remarks>
///     <para>
///         九宫格的排列与持久化数值的对应关系由 <see cref="NotificationWindowAnchorLayout.GridOrder" /> 给出，
///         和 <c>App</c> 的窗口定位共用同一份映射：左上 → 上 → 右上 → 左 → 中心 → 右 → 左下 → 下 → 右下。
///         旧配置里 0–4 的取值（中心 / 顶 / 底 / 左 / 右）逐字节不变，四角是追加值，
///         所以换成圆点选择器不会让任何人已经保存的位置跳到别的格子上。
///     </para>
///     <para>
///         每一格都是真正的 <see cref="Button" />：Tab 能进来、方向键在九宫格里移动、回车/空格确认，
///         圆点按 <see cref="Labels" /> 里相同顺序的文本显示提示并作为无障碍名称。外观（圆点大小、
///         悬停与选中配色、键盘焦点环）全部在 <c>Controls/AnchorGridPicker.axaml</c> 里，
///         控件本身只负责格子的构造与选中状态。
///     </para>
/// </remarks>
public sealed class AnchorGridPicker : UniformGrid
{
    /// <summary>当前选中的持久化位置值（九宫格外的值会被收敛成中心）。</summary>
    public static readonly StyledProperty<int> SelectedValueProperty =
        AvaloniaProperty.Register<AnchorGridPicker, int>(
            nameof(SelectedValue),
            (int)NotificationWindowPosition.Center,
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    ///     九格各自的提示文本，顺序与 <see cref="NotificationWindowAnchorLayout.GridOrder" /> 一致；可以留空。
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> LabelsProperty =
        AvaloniaProperty.Register<AnchorGridPicker, IReadOnlyList<string>?>(nameof(Labels));

    /// <summary>圆点直径（DIP）。</summary>
    public static readonly StyledProperty<double> DotSizeProperty =
        AvaloniaProperty.Register<AnchorGridPicker, double>(nameof(DotSize), 22d);

    /// <summary>单格点击区域边长（DIP），必须不小于 <see cref="DotSize" />。</summary>
    public static readonly StyledProperty<double> CellSizeProperty =
        AvaloniaProperty.Register<AnchorGridPicker, double>(nameof(CellSize), 44d);

    /// <summary>
    ///     用户真正点中（或回车/空格确认）了一格时触发。方向键移动选中不触发，
    ///     所以外层下拉框可以据此收起弹出层，而键盘浏览九宫格时弹层保持展开。
    /// </summary>
    public event EventHandler? SelectionCommitted;

    private const int ColumnCount = 3;
    private const string CellClass = "anchor-cell";
    private const string DotClass = "anchor-dot";
    private const string SelectedClass = "selected";
    private const double CellSpacing = 4;

    private readonly List<AnchorGridCell> _cells = [];

    static AnchorGridPicker()
    {
        SelectedValueProperty.Changed.AddClassHandler<AnchorGridPicker>(static (picker, args) =>
        {
            // 绑定写进来的越界值不会真的显示到界面上：先收敛再重算选中格。
            if (args.NewValue is int value && !NotificationWindowAnchorLayout.IsDefined(value))
                picker.SelectedValue = (int)NotificationWindowAnchorLayout.Normalize(value);

            picker.RefreshSelection();
        });
        LabelsProperty.Changed.AddClassHandler<AnchorGridPicker>(static (picker, _) => picker.RefreshLabels());
        DotSizeProperty.Changed.AddClassHandler<AnchorGridPicker>(static (picker, _) => picker.RefreshMetrics());
        CellSizeProperty.Changed.AddClassHandler<AnchorGridPicker>(static (picker, _) => picker.RefreshMetrics());
    }

    public AnchorGridPicker()
    {
        Columns = ColumnCount;
        Rows = ColumnCount;
        ColumnSpacing = CellSpacing;
        RowSpacing = CellSpacing;
        BuildCells();
        AddHandler(KeyDownEvent, OnPickerKeyDown, RoutingStrategies.Bubble);
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

    public double DotSize
    {
        get => GetValue(DotSizeProperty);
        set => SetValue(DotSizeProperty, value);
    }

    public double CellSize
    {
        get => GetValue(CellSizeProperty);
        set => SetValue(CellSizeProperty, value);
    }

    private void BuildCells()
    {
        Children.Clear();
        _cells.Clear();

        foreach (var value in NotificationWindowAnchorLayout.GridOrder)
        {
            var dot = new Border();
            dot.Classes.Add(DotClass);

            var cell = new Button
            {
                Content = dot,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                Padding = default,
                MinWidth = 0,
                MinHeight = 0,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            cell.Classes.Add(CellClass);
            cell.Click += (_, _) =>
            {
                SelectedValue = (int)value;
                SelectionCommitted?.Invoke(this, EventArgs.Empty);
            };

            _cells.Add(new AnchorGridCell(cell, dot, value));
            Children.Add(cell);
        }

        RefreshMetrics();
        RefreshLabels();
        RefreshSelection();
    }

    private void RefreshMetrics()
    {
        var dotSize = Math.Max(6, DotSize);
        var cellSize = Math.Max(dotSize, CellSize);
        var radius = new CornerRadius(dotSize / 2);

        foreach (var cell in _cells)
        {
            cell.Button.Width = cellSize;
            cell.Button.Height = cellSize;
            cell.Dot.Width = dotSize;
            cell.Dot.Height = dotSize;
            cell.Dot.CornerRadius = radius;
        }
    }

    private void RefreshLabels()
    {
        var labels = Labels;
        for (var index = 0; index < _cells.Count; index++)
        {
            var label = labels is not null && index < labels.Count ? labels[index] : string.Empty;
            var button = _cells[index].Button;
            ToolTip.SetTip(button, string.IsNullOrWhiteSpace(label) ? null : label);
            AutomationProperties.SetName(button, label);
        }
    }

    private void RefreshSelection()
    {
        var selected = NotificationWindowAnchorLayout.Normalize(SelectedValue);
        foreach (var cell in _cells)
            cell.Button.Classes.Set(SelectedClass, cell.Value == selected);
    }

    /// <summary>方向键在九宫格里移动并顺带选中；回车/空格由每格自己的按钮处理。</summary>
    private void OnPickerKeyDown(object? sender, KeyEventArgs e)
    {
        var index = _cells.FindIndex(cell => cell.Button.IsFocused);
        if (index < 0)
            return;

        var (rowDelta, columnDelta) = e.Key switch
        {
            Key.Left => (0, -1),
            Key.Right => (0, 1),
            Key.Up => (-1, 0),
            Key.Down => (1, 0),
            _ => (0, 0)
        };
        if (rowDelta == 0 && columnDelta == 0)
            return;

        var row = Math.Clamp((index / ColumnCount) + rowDelta, 0, ColumnCount - 1);
        var column = Math.Clamp((index % ColumnCount) + columnDelta, 0, ColumnCount - 1);
        var target = _cells[(row * ColumnCount) + column];
        SelectedValue = (int)target.Value;
        target.Button.Focus();
        e.Handled = true;
    }

    /// <summary>
    ///     把键盘焦点放到当前选中的格子上，供弹出层展开时继承选择：
    ///     键盘用户打开下拉框后直接落在“现在的位置”上，方向键从这里继续移动。
    /// </summary>
    public void FocusSelectedCell()
    {
        var index = NotificationWindowAnchorLayout.ToCellIndex(SelectedValue);
        if (index >= 0 && index < _cells.Count)
            _cells[index].Button.Focus();
    }

    private sealed record AnchorGridCell(Button Button, Border Dot, NotificationWindowPosition Value);
}