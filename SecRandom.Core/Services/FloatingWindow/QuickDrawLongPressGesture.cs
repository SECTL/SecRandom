using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace SecRandom.Core.Services.FloatingWindow;

/// <summary>
///     悬浮窗「闪抽」长按快选的全部纯计算：按住时长 → 环进度、环上的位置 → 环颜色、
///     指针纵向位置 → 抽取人数、球与整块快选面板的落位与夹紧。
/// </summary>
/// <remarks>
///     这里刻意不碰窗口、指针与合成器，只做数值决策，因此可以在 xUnit 里逐条钉住边界：
///     人数上限、拖动取消距离、时长夹紧、环颜色端点、6 行滑轨到人数（含"退出"）的映射、
///     球与面板不出屏。窗口层只负责把结果贴到 <c>FloatingWindow</c> 上。
/// </remarks>
public static class QuickDrawLongPressGesture
{
    /// <summary>长按最多一次抽多少人。</summary>
    public const int MaxCount = 5;

    /// <summary>快选滑轨的行数：5、4、3、2、1 与"退出"。</summary>
    public const int OptionCount = MaxCount + 1;

    /// <summary>
    ///     刚展开、还没滑动时默认指向的人数。手指按住期间给的暗示是"我就要 1 个人"，
    ///     真要多抽才需要往下/往上找到对应行。
    /// </summary>
    public const int DefaultCount = 1;

    /// <summary>按住后指针偏离超过这个距离（DIP）就不算长按，改判为拖动窗口。</summary>
    public const double DragCancelDistance = 14;

    /// <summary>
    ///     触控/手写笔按住时的容差（DIP）。手指按住会自然漂移十几 DIP，沿用鼠标的
    ///     <see cref="DragCancelDistance" /> 会把"点一下"误判成拖窗，长按也更容易被打断。
    /// </summary>
    public const double TouchDragCancelDistance = 24;

    /// <summary>滑轨上真正把选中行切到下一行所需的纵向位移（DIP，鼠标）。</summary>
    public const double SelectionActivationDistance = 8;

    /// <summary>触控时切换选中行的纵向位移：比鼠标多留一点，避免按住时的轻微滑动改掉人数。</summary>
    public const double TouchSelectionActivationDistance = 12;

    /// <summary>长按时长的下界，避免 0 或负数把环一按到底。</summary>
    public const int MinHoldDurationMilliseconds = 40;

    /// <summary>长按时长的上界，避免配置里写了个天文数字后再也等不到完成。</summary>
    public const int MaxHoldDurationMilliseconds = 3000;

    /// <summary>配置缺字段或非法时的长按时长回退值。</summary>
    public const int DefaultHoldDurationMilliseconds = 500;

    /// <summary>鼠标右键按下时用的长按时长（只做一次确认，不再等满整个配置时长）。</summary>
    /// <remarks>
    ///     Windows 会把触控"长按"先攒在自己的按住时间里，攒够之后才以一次<em>鼠标右键按下</em>的形式
    ///     重新投递（松手时补一次右键抬起）。也就是说用户真正的按住时长已经用掉了，这里再等一整个
    ///     <see cref="DefaultHoldDurationMilliseconds" /> 等于让人多按半秒；但也不能一按下就弹面板，
    ///     否则一次快速的右键点击也会把人送进快选。取一个只够区分"按下即松"和"继续按住"的时长。
    /// </remarks>
    public const int SecondaryButtonHoldDurationMilliseconds = 160;

    /// <summary>球的最小直径（DIP），再小就看不清、也点不准。</summary>
    public const double MinOrbSize = 44;

    /// <summary>球的最大直径（DIP），再大就盖住了滑轨。</summary>
    public const double MaxOrbSize = 96;

    /// <summary>快选面板四周留白，同时给球的光晕留出绘制空间。</summary>
    public const double SlabPadding = 8;

    /// <summary>滑轨与球之间的间距。</summary>
    public const double PickerGap = 14;

    /// <summary>滑轨行间距。</summary>
    public const double RowSpacing = 4;

    /// <summary>长按环的默认起点色（宿主未指定主题色时使用）。</summary>
    public static readonly Color RingStartColor = Color.Parse("#2E8BFF");

    /// <summary>长按环的默认完成色（宿主未指定主题色时使用）。</summary>
    public static readonly Color RingEndColor = Color.Parse("#22C55E");

    /// <summary>把设置里的长按时长夹到可执行区间。</summary>
    public static int NormalizeHoldDuration(int milliseconds)
    {
        return Math.Clamp(milliseconds, MinHoldDurationMilliseconds, MaxHoldDurationMilliseconds);
    }

    /// <summary>这次按住要走多长的进度：右键（触控长按在 Windows 上的替身）只走一段确认时长。</summary>
    public static int ResolveHoldDuration(int holdDurationMilliseconds, bool isSecondaryButton)
    {
        var duration = NormalizeHoldDuration(holdDurationMilliseconds);
        return isSecondaryButton
            ? Math.Min(duration, SecondaryButtonHoldDurationMilliseconds)
            : duration;
    }

    /// <summary>按住已经过了多少比例：0 刚到 1 完成，超出时长后保持 1。</summary>
    public static double ResolveProgress(long elapsedMilliseconds, int holdDurationMilliseconds)
    {
        var duration = NormalizeHoldDuration(holdDurationMilliseconds);
        if (elapsedMilliseconds <= 0)
            return 0;
        if (elapsedMilliseconds >= duration)
            return 1;

        return elapsedMilliseconds / (double)duration;
    }

    /// <summary>指针相对按下的位置是否已经超出长按容差（超出即取消长按、转为拖动窗口）。</summary>
    public static bool ShouldCancelForDrag(Point start, Point current, double threshold = DragCancelDistance)
    {
        var deltaX = current.X - start.X;
        var deltaY = current.Y - start.Y;
        return (deltaX * deltaX) + (deltaY * deltaY) > threshold * threshold;
    }

    /// <summary>按指针类型取长按容差：触控/手写笔用更大的容差，鼠标沿用精确的 14 DIP。</summary>
    public static double ResolveDragCancelDistance(bool isTouchOrPen)
    {
        return isTouchOrPen ? TouchDragCancelDistance : DragCancelDistance;
    }

    /// <summary>按指针类型取滑轨切换选中行所需的纵向位移。</summary>
    public static double ResolveSelectionActivationDistance(bool isTouchOrPen)
    {
        return isTouchOrPen ? TouchSelectionActivationDistance : SelectionActivationDistance;
    }

    /// <summary>按指针类型判断这次位移是否已经超出长按容差。</summary>
    public static bool ShouldCancelForDrag(Point start, Point current, bool isTouchOrPen)
    {
        return ShouldCancelForDrag(start, current, ResolveDragCancelDistance(isTouchOrPen));
    }

    /// <summary>
    ///     环上某一点的颜色：<paramref name="fraction" /> 是"从起点算起占已完成部分的进度"，
    ///     0 是蓝色起点、1 是绿色头部。头部始终是绿色，整圈填满时才呈现完整的蓝→绿渐变。
    /// </summary>
    public static Color ResolveRingColor(double fraction)
    {
        return LerpColor(RingStartColor, RingEndColor, fraction);
    }

    /// <summary>
    ///     在两个颜色之间按比例插值；宿主把主题强调色传给进度环时用它生成"起点色 → 头部色"。
    /// </summary>
    public static Color LerpColor(Color start, Color end, double fraction)
    {
        var t = Math.Clamp(fraction, 0, 1);
        return Color.FromArgb(
            255,
            Lerp(start.R, end.R, t),
            Lerp(start.G, end.G, t),
            Lerp(start.B, end.B, t));
    }

    /// <summary>把颜色朝 <paramref name="target" /> 方向提亮 <paramref name="amount" />（0 原样、1 全白）。</summary>
    public static Color Lighten(Color color, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            255,
            Lerp(color.R, 255, t),
            Lerp(color.G, 255, t),
            Lerp(color.B, 255, t));
    }

    /// <summary>球直径：跟随收纳球尺寸，但夹在可读可点的区间里。</summary>
    public static double ResolveOrbSize(int dockedWindowSize)
    {
        return Math.Clamp(dockedWindowSize, MinOrbSize, MaxOrbSize);
    }

    /// <summary>
    ///     滑轨的自上而下行序：滑轨在球的左侧时整列是 5、4、3、2、1、"退出"，
    ///     在球的右侧时整列镜像成"退出"、1、2、3、4、5——退出永远贴屏幕边缘那一端。
    /// </summary>
    public static IReadOnlyList<int> ResolveRowCounts(bool pickerOnLeft)
    {
        var rows = new int[OptionCount];
        for (var index = 0; index < OptionCount; index++)
            rows[index] = pickerOnLeft ? MaxCount - index : index;

        return rows;
    }

    /// <summary>滑轨永远放在浮窗的对侧：浮窗在左就从右边弹出，浮窗在右就从左边弹出。</summary>
    public static bool ResolvePickerOnLeft(bool windowOnLeft)
    {
        return !windowOnLeft;
    }

    /// <summary>某一人数在行序里的下标；找不到时回到默认人数的行，再找不到就回到首行。</summary>
    public static int ResolveRowIndex(IReadOnlyList<int> rowCounts, int count)
    {
        for (var index = 0; index < rowCounts.Count; index++)
        {
            if (rowCounts[index] == count)
                return index;
        }

        if (count != DefaultCount)
            return ResolveRowIndex(rowCounts, DefaultCount);

        return 0;
    }

    /// <summary>
    ///     指针落在滑轨的哪一行，并按行序换回人数（0 表示"退出"）。
    ///     超出滑轨范围时夹到首/末行，所以"一直滑到底"一定是行序两端的那一项。
    /// </summary>
    public static int ResolveCount(
        double pointerY,
        double pickerTop,
        double pickerHeight,
        IReadOnlyList<int> rowCounts)
    {
        if (pickerHeight <= 0 || rowCounts.Count == 0)
            return DefaultCount;

        // 行高与行间距相加正好是 (pickerHeight + RowSpacing) / 行数，行边界因此对得上真实布局。
        var step = (pickerHeight + RowSpacing) / rowCounts.Count;
        var index = (int)Math.Floor((pointerY - pickerTop) / step);
        index = Math.Clamp(index, 0, rowCounts.Count - 1);
        return rowCounts[index];
    }

    /// <summary>某一行的中心相对滑轨顶部的偏移，用于把默认人数那一行对准按下点。</summary>
    public static double ResolveRowCenterOffset(int rowIndex, double rowHeight)
    {
        var rows = Math.Max(0, rowIndex);
        return (rows * (rowHeight + RowSpacing)) + (rowHeight / 2);
    }

    /// <summary>整块快选面板（滑轨 + 球）的尺寸与滑轨在面板内的矩形。</summary>
    public static QuickPickLayout ResolveLayout(
        double orbSize,
        double pickerWidth,
        int rowCount,
        double rowHeight,
        bool pickerOnLeft)
    {
        var rows = Math.Max(1, rowCount);
        var pickerHeight = (rows * rowHeight) + ((rows - 1) * RowSpacing);
        var slabHeight = Math.Max(orbSize, pickerHeight) + (SlabPadding * 2);
        var slabWidth = (SlabPadding * 2) + pickerWidth + PickerGap + orbSize;
        var pickerX = pickerOnLeft
            ? SlabPadding
            : SlabPadding + orbSize + PickerGap;
        var pickerY = (slabHeight - pickerHeight) / 2;

        return new QuickPickLayout(
            new Size(slabWidth, slabHeight),
            new Rect(pickerX, pickerY, pickerWidth, pickerHeight),
            pickerOnLeft);
    }

    /// <summary>球心在面板内的位置：跟随指针，但始终留出边距，不贴到面板边缘外。</summary>
    public static Point ResolveOrbCenter(Point pointerInWindow, Size windowSize, double orbSize)
    {
        var radius = orbSize / 2;
        var inset = radius + SlabPadding;

        return new Point(
            Math.Clamp(pointerInWindow.X, inset, Math.Max(inset, windowSize.Width - inset)),
            Math.Clamp(pointerInWindow.Y, inset, Math.Max(inset, windowSize.Height - inset)));
    }

    private static byte Lerp(byte from, byte to, double t)
    {
        return (byte)Math.Round(from + ((to - from) * t));
    }
}

/// <summary>快选面板的布局结果：面板尺寸、滑轨矩形、滑轨是否在球的左侧。</summary>
public readonly record struct QuickPickLayout(Size WindowSize, Rect PickerRect, bool PickerOnLeft);