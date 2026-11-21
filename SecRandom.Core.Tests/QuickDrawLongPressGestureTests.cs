using Avalonia;
using Avalonia.Media;
using SecRandom.Core.Services.FloatingWindow;

namespace SecRandom.Core.Tests;

public class QuickDrawLongPressGestureTests
{
    [Fact]
    public void RowCounts_MirrorWithPickerSide()
    {
        IReadOnlyList<int> onLeft = QuickDrawLongPressGesture.ResolveRowCounts(pickerOnLeft: true);
        IReadOnlyList<int> onRight = QuickDrawLongPressGesture.ResolveRowCounts(pickerOnLeft: false);

        Assert.Equal(new[] { 5, 4, 3, 2, 1, 0 }, onLeft);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, onRight);
        Assert.Equal(QuickDrawLongPressGesture.OptionCount, onLeft.Count);
    }

    [Fact]
    public void PickerSitsOppositeTheWindowSide()
    {
        Assert.True(QuickDrawLongPressGesture.ResolvePickerOnLeft(windowOnLeft: false));
        Assert.False(QuickDrawLongPressGesture.ResolvePickerOnLeft(windowOnLeft: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolveCount_ClampsToColumnEnds(bool pickerOnLeft)
    {
        IReadOnlyList<int> rows = QuickDrawLongPressGesture.ResolveRowCounts(pickerOnLeft);
        const double top = 100;
        const double height = 300;

        Assert.Equal(rows[0], QuickDrawLongPressGesture.ResolveCount(top - 500, top, height, rows));
        Assert.Equal(rows[0], QuickDrawLongPressGesture.ResolveCount(top, top, height, rows));
        Assert.Equal(rows[^1], QuickDrawLongPressGesture.ResolveCount(top + height + 500, top, height, rows));
        Assert.Equal(rows[^1], QuickDrawLongPressGesture.ResolveCount(top + height - 1, top, height, rows));

        // 行心一定落在自己那一行：逐行取中心验证，行序两端与"退出"都测得准。
        for (int index = 0; index < rows.Count; index++)
        {
            double center = top + (index * (height / rows.Count)) + (height / rows.Count / 2);
            Assert.Equal(rows[index], QuickDrawLongPressGesture.ResolveCount(center, top, height, rows));
        }
    }

    [Fact]
    public void DefaultCount_LandsOnTheRowUnderTheFinger()
    {
        IReadOnlyList<int> rows = QuickDrawLongPressGesture.ResolveRowCounts(pickerOnLeft: true);
        int index = QuickDrawLongPressGesture.ResolveRowIndex(rows, QuickDrawLongPressGesture.DefaultCount);

        Assert.Equal(4, index);
        Assert.Equal(QuickDrawLongPressGesture.DefaultCount, rows[index]);
        Assert.Equal(QuickDrawLongPressGesture.DefaultCount,
            QuickDrawLongPressGesture.ResolveCount(100 + (index * 50) + 25, 100, 300, rows));
    }

    [Fact]
    public void ResolveRowIndex_FallsBackToDefaultRowThenFirst()
    {
        IReadOnlyList<int> rows = QuickDrawLongPressGesture.ResolveRowCounts(pickerOnLeft: true);

        Assert.Equal(0, QuickDrawLongPressGesture.ResolveRowIndex(rows, 5));
        Assert.Equal(4, QuickDrawLongPressGesture.ResolveRowIndex(rows, 99));
        Assert.Equal(0, QuickDrawLongPressGesture.ResolveRowIndex([], 99));
    }

    [Fact]
    public void ProgressAndRingColor_StayInsideTheirRange()
    {
        Assert.Equal(0, QuickDrawLongPressGesture.ResolveProgress(-10, 500));
        Assert.Equal(0.5, QuickDrawLongPressGesture.ResolveProgress(250, 500), 3);
        Assert.Equal(1, QuickDrawLongPressGesture.ResolveProgress(900, 500));

        Color start = QuickDrawLongPressGesture.ResolveRingColor(0);
        Color end = QuickDrawLongPressGesture.ResolveRingColor(1);

        Assert.True(start.B > start.G);
        Assert.True(end.G > end.B);
        Assert.Equal(255, start.A);
        Assert.Equal(QuickDrawLongPressGesture.ResolveRingColor(1), QuickDrawLongPressGesture.ResolveRingColor(5));
    }

    [Fact]
    public void HoldDuration_IsClamped()
    {
        Assert.Equal(QuickDrawLongPressGesture.MinHoldDurationMilliseconds,
            QuickDrawLongPressGesture.NormalizeHoldDuration(0));
        Assert.Equal(QuickDrawLongPressGesture.MaxHoldDurationMilliseconds,
            QuickDrawLongPressGesture.NormalizeHoldDuration(60_000));
        Assert.Equal(500, QuickDrawLongPressGesture.NormalizeHoldDuration(500));
    }

    [Fact]
    public void DragBeyondTheThreshold_CancelsTheHold()
    {
        Point start = new(100, 100);

        Assert.False(QuickDrawLongPressGesture.ShouldCancelForDrag(start, new Point(104, 104)));
        Assert.True(QuickDrawLongPressGesture.ShouldCancelForDrag(
            start,
            new Point(100 + QuickDrawLongPressGesture.DragCancelDistance + 1, 100)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DragTolerance_FollowsThePointerKind(bool isTouchOrPen)
    {
        Point start = new(100, 100);

        // 触控容差必须比鼠标宽松，否则手指按住时的自然漂移会被误判成拖窗。
        Assert.True(QuickDrawLongPressGesture.TouchDragCancelDistance >
                    QuickDrawLongPressGesture.DragCancelDistance);
        Assert.Equal(
            isTouchOrPen
                ? QuickDrawLongPressGesture.TouchDragCancelDistance
                : QuickDrawLongPressGesture.DragCancelDistance,
            QuickDrawLongPressGesture.ResolveDragCancelDistance(isTouchOrPen));

        // 同一段位移：鼠标已经算拖动，触控还在点按容差内；超过触控容差才取消长按。
        double mouseTolerance = QuickDrawLongPressGesture.ResolveDragCancelDistance(false);
        double touchTolerance = QuickDrawLongPressGesture.ResolveDragCancelDistance(true);
        double between = (mouseTolerance + touchTolerance) / 2;

        Assert.True(QuickDrawLongPressGesture.ShouldCancelForDrag(
            start, new Point(start.X + between, start.Y), isTouchOrPen: false));
        Assert.False(QuickDrawLongPressGesture.ShouldCancelForDrag(
            start, new Point(start.X + between, start.Y), isTouchOrPen: true));
        Assert.True(QuickDrawLongPressGesture.ShouldCancelForDrag(
            start, new Point(start.X + touchTolerance + 1, start.Y), isTouchOrPen: true));
        Assert.False(QuickDrawLongPressGesture.ShouldCancelForDrag(
            start, new Point(start.X, start.Y + 1), isTouchOrPen));
    }

    [Fact]
    public void SelectionActivation_LeavesMoreRoomForTouch()
    {
        Assert.True(QuickDrawLongPressGesture.TouchSelectionActivationDistance >
                    QuickDrawLongPressGesture.SelectionActivationDistance);
        Assert.Equal(QuickDrawLongPressGesture.SelectionActivationDistance,
            QuickDrawLongPressGesture.ResolveSelectionActivationDistance(false));
        Assert.Equal(QuickDrawLongPressGesture.TouchSelectionActivationDistance,
            QuickDrawLongPressGesture.ResolveSelectionActivationDistance(true));
    }

    [Fact]
    public void Layout_KeepsPickerAndOrbInsideThePanel()
    {
        QuickPickLayout layout = QuickDrawLongPressGesture.ResolveLayout(
            orbSize: 56,
            pickerWidth: 96,
            rowCount: QuickDrawLongPressGesture.OptionCount,
            rowHeight: 52,
            pickerOnLeft: true);

        Assert.True(layout.WindowSize.Width > 0);
        Assert.True(layout.WindowSize.Height > 0);
        Assert.True(layout.PickerRect.Right <= layout.WindowSize.Width);
        Assert.True(layout.PickerRect.Bottom <= layout.WindowSize.Height);
        Assert.True(layout.PickerOnLeft);

        QuickPickLayout mirrored = QuickDrawLongPressGesture.ResolveLayout(
            orbSize: 56,
            pickerWidth: 96,
            rowCount: QuickDrawLongPressGesture.OptionCount,
            rowHeight: 52,
            pickerOnLeft: false);

        Assert.Equal(layout.WindowSize, mirrored.WindowSize);
        Assert.True(mirrored.PickerRect.X > layout.PickerRect.X);
    }

    [Fact]
    public void OrbCenter_StaysInsideThePanelWhileFollowingThePointer()
    {
        Size window = new(200, 320);
        Point center = QuickDrawLongPressGesture.ResolveOrbCenter(new Point(-500, 10_000), window, 56);

        Assert.InRange(center.X, 28, window.Width - 28);
        Assert.InRange(center.Y, 28, window.Height - 28);

        Point middle = QuickDrawLongPressGesture.ResolveOrbCenter(new Point(100, 160), window, 56);
        Assert.Equal(new Point(100, 160), middle);
    }
}