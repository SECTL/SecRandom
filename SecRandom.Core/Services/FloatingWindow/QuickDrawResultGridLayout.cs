using System;

namespace SecRandom.Core.Services.FloatingWindow;

/// <summary>
///     浮窗结果卡的网格排版决策：给定人数、可用宽高和单张卡片尺寸，决定一行放几张。
/// </summary>
/// <remarks>
///     <para>
///         列数先按可用区域的宽高比推一个"愿意铺多宽"的理想值，再用两条硬约束收窄：
///         一是屏幕宽度最多能并排放下几张（长姓名会把卡片撑宽，不能让它把列数压成 1），
///         二是至少留两行（4 个人是 2×2、6 个人在正常横向屏幕上 3×2，而不是又拉成一条长龙）。
///         可用区域偏窄偏高时理想值会掉到 1，此时 4 人以上仍保底 2 列，
///         所以竖屏/窄窗口得到的是 2 列多行，而不是一路竖着排。
///     </para>
///     <para>
///         这里只做纯数值决策，不碰控件与窗口，因此 <c>SecRandom.Core.Tests</c> 可以直接钉住
///         "4 人→2 列、宽屏 6 人→3 列、窄高屏 6 人→2 列"这些用户能看见的结果。
///     </para>
/// </remarks>
public static class QuickDrawResultGridLayout
{
    /// <summary>
    ///     估算列数时使用的名义卡片宽高比。刻意不用实测宽度：长姓名会把单张卡片撑到
    ///     300–400 DIP，实测比例会把 4 个人算成 1 列，而用户要的是 2×2。
    /// </summary>
    public const double NominalCardAspectRatio = 1.5;

    /// <summary>
    ///     返回一行应放几张卡片；<paramref name="itemCount" /> 为 0 时返回 0。
    /// </summary>
    /// <param name="itemCount">结果卡总数。</param>
    /// <param name="availableWidth">结果区可用宽度（DIP）。</param>
    /// <param name="availableHeight">结果区可用高度（DIP）。</param>
    /// <param name="cardWidth">单张卡片的最大自然宽度（DIP）。</param>
    /// <param name="cardHeight">单张卡片的最大自然高度（DIP）。</param>
    public static int ResolveColumns(
        int itemCount,
        double availableWidth,
        double availableHeight,
        double cardWidth,
        double cardHeight)
    {
        if (itemCount <= 0)
            return 0;

        // 量测还没落到真实窗口上时可能拿到无穷/0；收敛成 1 让后面的夹紧逻辑仍然给出可执行结果。
        var width = IsPositiveFinite(availableWidth) ? availableWidth : 1;
        var height = IsPositiveFinite(availableHeight) ? availableHeight : 1;
        var itemWidth = IsPositiveFinite(cardWidth) ? cardWidth : 1;
        var itemHeight = IsPositiveFinite(cardHeight) ? cardHeight : 1;

        var widthFit = width / itemWidth;
        var maxByWidth = widthFit >= itemCount
            ? itemCount
            : Math.Max(1, (int)Math.Floor(widthFit));
        // 最多铺两行：6 个人不会排成 4+2，10 个人也不会先铺 9 个再挂一个。
        var maxByBalance = itemCount <= 2 ? itemCount : Math.Max(2, (int)Math.Ceiling(itemCount / 2.0));
        var maxColumns = Math.Max(1, Math.Min(itemCount, Math.Min(maxByWidth, maxByBalance)));

        var availableAspect = width / height;
        var ideal = (int)Math.Clamp(
            Math.Round(
                Math.Sqrt(itemCount * availableAspect / NominalCardAspectRatio),
                MidpointRounding.AwayFromZero),
            1d,
            itemCount);

        // 4 人以上、宽度又允许时至少两列：竖屏也保持"两个人一排"，而不是四个人竖成一列。
        var minColumns = itemCount >= 4 && maxByWidth >= 2 ? 2 : 1;
        var columns = Math.Clamp(ideal, minColumns, maxColumns);

        // 最后一行只剩 1 张时，如果多一列能同时减少行数，就补到那一列，避免孤零零一张。
        while (columns < maxColumns
               && LastRowItemCount(itemCount, columns) == 1
               && RowCount(itemCount, columns + 1) < RowCount(itemCount, columns))
        {
            columns++;
        }

        // 行高装不下时优先加列（加列会减少行数）；已经到宽度/行数上限就交给外层纵向滚动。
        while (RowCount(itemCount, columns) * itemHeight > height && columns < maxColumns)
            columns++;

        return columns;
    }

    private static int RowCount(int itemCount, int columns)
    {
        return (itemCount + columns - 1) / columns;
    }

    private static int LastRowItemCount(int itemCount, int columns)
    {
        var rows = RowCount(itemCount, columns);
        return itemCount - ((rows - 1) * columns);
    }

    private static bool IsPositiveFinite(double value)
    {
        return double.IsFinite(value) && value > 0;
    }
}