using System;
using Avalonia;
using Avalonia.Controls;
using SecRandom.Core.Services.FloatingWindow;

namespace SecRandom.Core.Controls;

/// <summary>
///     浮窗结果卡的居中自适应网格：列数按可用宽高选择，行内等宽，末行水平居中。
/// </summary>
/// <remarks>
///     <para>
///         列数决策在 <see cref="QuickDrawResultGridLayout" /> 里，本面板只负责量测与落位：
///         先量出所有卡片的自然尺寸，再把同一行的卡片拉伸到该行最高、最宽的单元格，
///         这样短姓名和长姓名混排时行高与列宽仍然整齐；最后一行卡片少时整行居中。
///     </para>
///     <para>
///         为什么需要窗口兜底：窗口是 <c>SizeToContent</c>，<c>ScrollViewer</c> 又会用无限高度
///         量测内容，面板拿到的 <see cref="Size" /> 可能有一边是无穷。此时退回窗口的
///         <see cref="Window.MaxWidth" /> / <see cref="Window.MaxHeight" />，再退回当前屏幕工作区，
///         并预留窗口边框与底部提示占用的空间，否则"按屏幕宽高自适应"会退化成永远单列。
///     </para>
/// </remarks>
public sealed class QuickDrawResultGridPanel : Panel
{
    private const double FallbackWidth = 1280;
    private const double FallbackHeight = 720;
    private const double WindowChromeWidth = 48;
    private const double WindowChromeHeight = 96;

    private int _columns;
    private double _cellWidth;
    private double[] _rowHeights = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        _columns = 0;
        _cellWidth = 0;
        _rowHeights = [];

        var children = Children;
        if (children.Count == 0)
            return default;

        var limits = ResolveAvailableSize(availableSize);
        var maxWidth = 0.0;
        var maxHeight = 0.0;
        foreach (var child in children)
        {
            child.Measure(new Size(limits.Width, double.PositiveInfinity));
            maxWidth = Math.Max(maxWidth, child.DesiredSize.Width);
            maxHeight = Math.Max(maxHeight, child.DesiredSize.Height);
        }

        if (maxWidth <= 0 || maxHeight <= 0)
            return default;

        var columns = QuickDrawResultGridLayout.ResolveColumns(
            children.Count,
            limits.Width,
            limits.Height,
            maxWidth,
            maxHeight);
        if (columns <= 0)
            return default;

        var rows = (children.Count + columns - 1) / columns;
        var rowHeights = new double[rows];
        for (var index = 0; index < children.Count; index++)
        {
            var row = index / columns;
            rowHeights[row] = Math.Max(rowHeights[row], children[index].DesiredSize.Height);
        }

        _columns = columns;
        _cellWidth = maxWidth;
        _rowHeights = rowHeights;

        var desiredHeight = 0.0;
        foreach (var rowHeight in rowHeights)
            desiredHeight += rowHeight;

        return new Size(columns * maxWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0 || _columns <= 0 || _cellWidth <= 0)
            return finalSize;

        // 面板可能被拉伸得比内容宽（ItemsControl 是 Stretch），按面板实际宽度逐行居中；
        // 若窗口反而比网格窄，仍以网格宽度计算，避免第一列被推到负坐标上裁掉。
        var arrangeWidth = Math.Max(finalSize.Width, _columns * _cellWidth);
        var y = 0.0;
        for (var row = 0; row < _rowHeights.Length; row++)
        {
            var rowHeight = _rowHeights[row];
            var first = row * _columns;
            var count = Math.Min(_columns, Children.Count - first);
            var x = (arrangeWidth - (count * _cellWidth)) / 2;

            for (var column = 0; column < count; column++)
            {
                Children[first + column].Arrange(new Rect(x, y, _cellWidth, rowHeight));
                x += _cellWidth;
            }

            y += rowHeight;
        }

        return finalSize;
    }

    /// <summary>
    ///     把量测约束收敛成有限值；无穷/0 时依次退回窗口上限与屏幕工作区。
    /// </summary>
    private Size ResolveAvailableSize(Size availableSize)
    {
        var width = availableSize.Width;
        var height = availableSize.Height;
        var window = TopLevel.GetTopLevel(this) as Window;

        if (window is not null)
        {
            // SizeToContent 窗口的"可用宽度"就是内容宽度，拿它选列会形成反馈环：
            // 少选一列 → 窗口变窄 → 更倾向少选一列。窗口会随结果卡伸缩，
            // 所以决策依据必须是窗口上限/屏幕工作区，而不是当前内容宽度。
            if (window.SizeToContent.HasFlag(SizeToContent.Width) || !IsUsable(width))
                width = ResolveWindowLimit(window, true);
            if (window.SizeToContent.HasFlag(SizeToContent.Height) || !IsUsable(height))
                height = ResolveWindowLimit(window, false);
        }

        if (!IsUsable(width))
            width = FallbackWidth;
        if (!IsUsable(height))
            height = FallbackHeight;

        return new Size(width, height);
    }

    private static double ResolveWindowLimit(Window window, bool horizontal)
    {
        var configured = horizontal ? window.MaxWidth : window.MaxHeight;
        if (IsUsable(configured))
            return Math.Max(1, configured - (horizontal ? WindowChromeWidth : WindowChromeHeight));

        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null)
            return horizontal ? FallbackWidth : FallbackHeight;

        var scale = window.RenderScaling > 0 ? window.RenderScaling : 1;
        var workingArea = horizontal ? screen.WorkingArea.Width : screen.WorkingArea.Height;
        if (workingArea <= 0)
            return horizontal ? FallbackWidth : FallbackHeight;

        return Math.Max(1, (workingArea / scale) - (horizontal ? WindowChromeWidth : WindowChromeHeight));
    }

    private static bool IsUsable(double value)
    {
        return double.IsFinite(value) && value > 0;
    }
}