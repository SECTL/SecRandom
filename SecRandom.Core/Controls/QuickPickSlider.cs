using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Core.Services.FloatingWindow;

namespace SecRandom.Core.Controls;

/// <summary>
///     闪抽长按后的竖向快选滑轨：行序由宿主按浮窗所在侧决定，一侧是 5、4、3、2、1、"退出"，
///     另一侧整列镜像（"退出"贴屏幕边缘那一端）。
/// </summary>
/// <remarks>
///     这个控件不处理任何指针事件（宿主把它整层设成不参与命中测试）：手指按在球上横向、纵向拖动时，
///     窗口层用 <see cref="QuickDrawLongPressGesture.ResolveCount" /> 把指针高度换算成人数，再回头调
///     <see cref="SelectedCount" /> 点亮对应行。这样做的好处是拖动永远不会被某一行的命中测试打断，
///     也不会出现"手指滑到行缝里就丢掉选择"的情况。
///     <para>
///         选中高光由宿主通过 <see cref="RowBrush" />、<see cref="ActiveRowBrush" />、
///         <see cref="ActiveRowBorderBrush" /> 和 <see cref="ActiveRowShadow" /> 传入当前主题色：
///         整列在宿主那里共用一块圆角底衬，行本身保持透明，只有当前指向的那一行亮成一块强调色芯片。
///     </para>
/// </remarks>
public sealed class QuickPickSlider : StackPanel
{
    /// <summary>当前指向的人数，<c>0</c> 表示"退出"，1..5 表示抽取人数。</summary>
    public static readonly StyledProperty<int> SelectedCountProperty =
        AvaloniaProperty.Register<QuickPickSlider, int>(
            nameof(SelectedCount),
            QuickDrawLongPressGesture.DefaultCount);

    /// <summary>自上而下的行序（人数，0 表示"退出"）。</summary>
    public static readonly StyledProperty<IReadOnlyList<int>> RowCountsProperty =
        AvaloniaProperty.Register<QuickPickSlider, IReadOnlyList<int>>(
            nameof(RowCounts),
            QuickDrawLongPressGesture.ResolveRowCounts(pickerOnLeft: true));

    /// <summary>"退出"那一行的文字，由宿主按当前语言传入。</summary>
    public static readonly StyledProperty<string> ExitTextProperty =
        AvaloniaProperty.Register<QuickPickSlider, string>(nameof(ExitText), "Exit");

    public static readonly StyledProperty<double> RowHeightProperty =
        AvaloniaProperty.Register<QuickPickSlider, double>(nameof(RowHeight), 44d);

    public static readonly StyledProperty<double> LabelFontSizeProperty =
        AvaloniaProperty.Register<QuickPickSlider, double>(nameof(LabelFontSize), 18d);

    /// <summary>未选中行自己的底色；宿主按主题传入，滑轨因此不需要整块底衬。</summary>
    public static readonly StyledProperty<IBrush?> RowBrushProperty =
        AvaloniaProperty.Register<QuickPickSlider, IBrush?>(nameof(RowBrush));

    /// <summary>选中行的填充色，通常由主题强调色生成。</summary>
    public static readonly StyledProperty<IBrush?> ActiveRowBrushProperty =
        AvaloniaProperty.Register<QuickPickSlider, IBrush?>(nameof(ActiveRowBrush));

    /// <summary>选中行的描边色。</summary>
    public static readonly StyledProperty<IBrush?> ActiveRowBorderBrushProperty =
        AvaloniaProperty.Register<QuickPickSlider, IBrush?>(nameof(ActiveRowBorderBrush));

    /// <summary>选中行的外发光，深色主题里靠它把高光从背景里提出来。</summary>
    public static readonly StyledProperty<BoxShadows> ActiveRowShadowProperty =
        AvaloniaProperty.Register<QuickPickSlider, BoxShadows>(nameof(ActiveRowShadow));

    private const string RowClass = "quick-pick-row";
    private const string LabelClass = "quick-pick-label";
    private const string ActiveClass = "active";

    private readonly List<(Border Row, TextBlock Label, int Count)> _rows = [];

    static QuickPickSlider()
    {
        SelectedCountProperty.Changed.AddClassHandler<QuickPickSlider>(
            static (slider, _) => slider.RefreshSelection());
        RowCountsProperty.Changed.AddClassHandler<QuickPickSlider>(
            static (slider, _) => slider.RebuildRows());
        RowHeightProperty.Changed.AddClassHandler<QuickPickSlider>(
            static (slider, _) => slider.RefreshMetrics());
        LabelFontSizeProperty.Changed.AddClassHandler<QuickPickSlider>(
            static (slider, _) => slider.RefreshMetrics());
        ExitTextProperty.Changed.AddClassHandler<QuickPickSlider>(
            static (slider, _) => slider.RefreshText());
        RowBrushProperty.Changed.AddClassHandler<QuickPickSlider>(
            static (slider, _) => slider.RefreshSelection());
        ActiveRowBrushProperty.Changed.AddClassHandler<QuickPickSlider>(
            static (slider, _) => slider.RefreshSelection());
        ActiveRowBorderBrushProperty.Changed.AddClassHandler<QuickPickSlider>(
            static (slider, _) => slider.RefreshSelection());
        ActiveRowShadowProperty.Changed.AddClassHandler<QuickPickSlider>(
            static (slider, _) => slider.RefreshSelection());
    }

    public QuickPickSlider()
    {
        Orientation = Orientation.Vertical;
        Spacing = QuickDrawLongPressGesture.RowSpacing;
        RebuildRows();
    }

    public int SelectedCount
    {
        get => GetValue(SelectedCountProperty);
        set => SetValue(SelectedCountProperty, value);
    }

    public IReadOnlyList<int> RowCounts
    {
        get => GetValue(RowCountsProperty);
        set => SetValue(RowCountsProperty, value);
    }

    public string ExitText
    {
        get => GetValue(ExitTextProperty);
        set => SetValue(ExitTextProperty, value);
    }

    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public double LabelFontSize
    {
        get => GetValue(LabelFontSizeProperty);
        set => SetValue(LabelFontSizeProperty, value);
    }

    public IBrush? RowBrush
    {
        get => GetValue(RowBrushProperty);
        set => SetValue(RowBrushProperty, value);
    }

    public IBrush? ActiveRowBrush
    {
        get => GetValue(ActiveRowBrushProperty);
        set => SetValue(ActiveRowBrushProperty, value);
    }

    public IBrush? ActiveRowBorderBrush
    {
        get => GetValue(ActiveRowBorderBrushProperty);
        set => SetValue(ActiveRowBorderBrushProperty, value);
    }

    public BoxShadows ActiveRowShadow
    {
        get => GetValue(ActiveRowShadowProperty);
        set => SetValue(ActiveRowShadowProperty, value);
    }

    private void RebuildRows()
    {
        Children.Clear();
        _rows.Clear();

        var counts = RowCounts;
        if (counts is null || counts.Count == 0)
            counts = QuickDrawLongPressGesture.ResolveRowCounts(pickerOnLeft: true);

        foreach (var count in counts)
        {
            var label = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            label.Classes.Add(LabelClass);

            var row = new Border
            {
                CornerRadius = new CornerRadius(12),
                Child = label
            };
            row.Classes.Add(RowClass);

            _rows.Add((row, label, count));
            Children.Add(row);
        }

        RefreshMetrics();
        RefreshText();
        RefreshSelection();
    }

    private void RefreshMetrics()
    {
        var height = Math.Max(24, RowHeight);
        var fontSize = Math.Max(10, LabelFontSize);
        foreach (var (row, label, _) in _rows)
        {
            row.Height = height;
            label.FontSize = fontSize;
        }
    }

    private void RefreshText()
    {
        var exitText = string.IsNullOrWhiteSpace(ExitText) ? "Exit" : ExitText;
        foreach (var (_, label, count) in _rows)
            label.Text = count <= 0 ? exitText : count.ToString();
    }

    private void RefreshSelection()
    {
        var selected = SelectedCount;
        foreach (var (row, _, count) in _rows)
        {
            var active = count == selected;
            row.Classes.Set(ActiveClass, active);
            // 整列由宿主在背后铺一块圆角底衬，所以未选中的行保持"透明、无描边、无阴影"，
            // 只有当前指向的那一行才浮起来成一块强调色芯片。
            row.Background = active ? ActiveRowBrush : RowBrush;
            row.BorderBrush = active ? ActiveRowBorderBrush : null;
            row.BorderThickness = active ? new Thickness(1.5) : default;
            row.BoxShadow = active ? ActiveRowShadow : default;
        }
    }
}