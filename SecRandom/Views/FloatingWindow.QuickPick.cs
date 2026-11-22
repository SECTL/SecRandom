using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Behaviors;
using Avalonia.Media;
using Avalonia.Threading;
using SecRandom.Core.Controls;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.FloatingWindow;
using SecRandom.Helpers;

namespace SecRandom.Views;

/// <summary>
///     悬浮窗「闪抽」长按快选：按住闪抽按钮 → 四周走一圈主题色进度环 → 浮窗收起成一个球 →
///     球旁边滑出 1–5 人滑轨 → 松手按选中人数开抽。
/// </summary>
/// <remarks>
///     <para>
///         点击语义只有一条路，而且完全由这里收口：短按不抢指针捕获，松手时按位移自己判"这是点击"，
///         直接 <see cref="App.ShowQuickDrawWindow(int)" /> 抽 1 人；因此按钮的 <c>Click</c> 不再参与开抽，
///         也不会因为长按抢过捕获而失灵。按住满时长才 <c>Pointer.Capture(this)</c>，之后的移动与松手
///         全由这里接管，按钮收不到这一次松手。
///     </para>
///     <para>
///         抢捕获会让按钮抛出 <c>PointerCaptureLost</c>，这是预期交接而不是中断：只要指针的捕获仍在
///         本窗手上，这次"丢捕获"就一律忽略。反过来，捕获真的丢了（触控被系统收走、窗口去激活）时
///         也不取消，而是把指针当前指向的人数直接抽掉——用户已经指到哪一行，就抽哪一行。
///     </para>
///     <para>
///         快选态用的是同一个窗口：浮窗自己从按钮面板缩成"球 + 滑轨"的小面板，不新开窗口，
///         因此不会在任务栏或 Alt+Tab 里多出条目，也不会抢走前台焦点（窗口始终是 NoActivate）。
///         按住后指针一旦偏移超过 <see cref="QuickDrawLongPressGesture.DragCancelDistance" /> 就取消长按、
///         退回原来的拖窗逻辑，这样"想拖窗口却误触闪抽键"不会变成一次意外的抽取。
///     </para>
///     <para>
///         配色全部取主题强调色（<see cref="ThemeAccentColor" />）：滑轨整列在背后铺一块圆角底衬，
///         行本身保持透明，只有当前指向的那一行亮成强调色芯片；球同样由强调色生成，不铺满窗口的面板。
///     </para>
///     <para>
///         所有数值决策（人数上限、进度、环颜色、行→人数、面板尺寸、球与面板的夹紧）都在
///         <see cref="QuickDrawLongPressGesture" /> 里，这个文件只负责把结果贴到控件上。
///     </para>
/// </remarks>
public partial class FloatingWindow
{
    /// <summary>闪抽按钮的稳定名字，用来判断按下的是不是它（按钮全部由代码构建）。</summary>
    private const string QuickDrawButtonName = "QuickDrawButton";

    private static string QuickPickExitText => Langs.Common.Resources.C_QuickPickExit;

    private const double RingInset = 3.5;

    /// <summary>当前主题的强调色：进度环、球、选中行全部由它派生，不再写死蓝绿。</summary>
    private static Color QuickPickAccent => ThemeAccentColor.Resolve();

    /// <summary>由强调色生成的深浅渐变笔刷（球体与选中行共用一套）。</summary>
    private static IBrush CreateAccentBrush()
    {
        var accent = QuickPickAccent;
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.1, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.9, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(QuickDrawLongPressGesture.Lighten(accent, 0.3), 0),
                new GradientStop(accent, 1)
            }
        };
    }

    /// <summary>主题面板色（带自定义不透明度）：滑轨的每一行都是自己的小芯片，靠它保持可读。</summary>
    private IBrush CreateRowBrush(byte alpha)
    {
        if (Application.Current?.TryFindResource(
                "SolidBackgroundFillColorQuarternaryBrush",
                ActualThemeVariant,
                out var value) == true
            && value is ISolidColorBrush surface)
        {
            return new SolidColorBrush(Color.FromArgb(alpha, surface.Color.R, surface.Color.G, surface.Color.B));
        }

        return new SolidColorBrush(Color.FromArgb(alpha, 32, 32, 32));
    }

    private enum QuickPickState
    {
        Idle,
        Holding,
        Picking
    }

    private QuickPickState _quickPickState;
    // Keep layout and persistence callbacks blocked until the expanded window has settled.
    private bool _quickPickRestoring;
    private IPointer? _quickPickPointer;
    private bool _quickPickCaptureHandlerAttached;
    private bool _quickPickTapSuppressed;
    private bool _quickPickIsTouch;
    private bool _quickPickIsSecondaryButton;
    private bool _quickPickSwallowSecondaryRelease;
    private Point _quickPickPressPoint;
    private PixelPoint _quickPickPressScreenPoint;
    private PixelPoint _quickPickOriginPosition;
    private int _quickPickOriginAnchorCenterY;
    private PixelRect? _quickPickOriginDockWorkingArea;
    private bool _quickPickOriginWasDocked;
    private bool _quickPickOriginWasDockedOnLeft;
    private int _quickPickOriginExpandedWindowWidth;
    private int _quickPickOriginExpandedWindowHeight;
    private int _quickPickOriginFloatPositionX;
    private int _quickPickOriginFloatPositionY;
    private int? _quickPickOriginSecondaryAnchorCenterY;
    private long _quickPickPressTimestamp;
    private int _quickPickRevision;
    private DispatcherTimer? _quickPickTimer;

    private readonly BorderedProgressRing _quickDrawRing = new();

    private Size _quickPickWindowSize;
    private Rect _quickPickPickerRect;
    private double _quickPickOrbSize;
    private double _quickPickOrbCenterX;
    private double _quickPickAnchorOffset;
    private double _quickPickRowHeight;
    private IReadOnlyList<int> _quickPickRowCounts = [];
    private int _quickPickCount = QuickDrawLongPressGesture.DefaultCount;

    /// <summary>按下的是闪抽按钮时接管这次按下；返回 <c>true</c> 表示不要再进入拖窗逻辑。</summary>
    /// <remarks>
    ///     两种按下都算：主指针（鼠标左键、触控、手写笔）和鼠标<em>右键</em>。后者不是附带支持，
    ///     而是触控长按在 Windows 上真正的到达形式——系统先把按住攒够，再把这次长按当成一次
    ///     鼠标右键按下重新投递，原来的触控指针同时被收走（<c>TouchCancel</c>）。
    ///     只认主指针时，那一下右键会被当成"不相干的按下"丢掉，用户看到的就是"触控长按没反应"。
    /// </remarks>
    /// <summary>这次按下是不是鼠标右键：Windows 把触控"长按"转成的就是它。</summary>
    private bool IsSecondaryButtonPress(PointerPressedEventArgs e)
    {
        return e.Pointer.Type == PointerType.Mouse
               && e.GetCurrentPoint(this).Properties.IsRightButtonPressed;
    }

    private bool TryBeginQuickDrawLongPress(PointerPressedEventArgs e, Visual? source)
    {
        if (_isDocked || _isDockTransitioning)
            return false;
        if (!EffectiveFloatingWindowSettings.EnableQuickDrawLongPress)
            return false;

        var button = FindQuickDrawButton(source);
        if (button is null)
            return false;

        var isSecondaryButton = IsSecondaryButtonPress(e);

        // Windows promotes a touch long-press to a mouse right-button press after the
        // original touch capture is cancelled. Transfer the pointer to this same gesture;
        // restarting the timer and origin here makes the panel jump or flash.
        if (_quickPickState == QuickPickState.Holding
            && _quickPickIsTouch
            && isSecondaryButton)
        {
            _quickPickPointer = e.Pointer;
            _quickPickIsSecondaryButton = true;
            return true;
        }

        // 已经在按住态时又来一次按下：只有"右键重新投递"需要重开计时。触控取消与右键到达的
        // 先后顺序没有保证，先到右键时若不接管，这次长按就会随触控取消一起消失。
        if (_quickPickState != QuickPickState.Idle)
        {
            if (!isSecondaryButton || _quickPickIsSecondaryButton)
                return false;

            CancelQuickDrawRing();
        }

        AttachQuickPickCaptureHandler();

        var point = e.GetPosition(this);
        _quickPickState = QuickPickState.Holding;
        _quickPickPointer = e.Pointer;
        _quickPickIsTouch = e.Pointer.Type is PointerType.Touch or PointerType.Pen;
        _quickPickIsSecondaryButton = isSecondaryButton;
        _quickPickPressPoint = point;
        _quickPickPressScreenPoint = Position + ToPixelPoint(point);
        // 展开态的原位就在按下的这一刻定下来。收起展开态、改窗口尺寸都可能挪动窗口，
        // 等那时再读 Position 记原位，结束快选时窗口会被放回一个已经偏掉的位置。
        _quickPickOriginPosition = Position;
        _quickPickOriginAnchorCenterY = AnchorCenterY;
        _quickPickOriginDockWorkingArea = _dockWorkingArea;
        _quickPickOriginWasDocked = _isDocked;
        _quickPickOriginWasDockedOnLeft = _isDockedOnLeft;
        _quickPickOriginExpandedWindowWidth = _expandedWindowWidth;
        _quickPickOriginExpandedWindowHeight = _expandedWindowHeight;
        _quickPickOriginFloatPositionX = ViewModel.Config.FloatPosition.X;
        _quickPickOriginFloatPositionY = ViewModel.Config.FloatPosition.Y;
        _quickPickOriginSecondaryAnchorCenterY = EffectiveFloatingWindowSettings.SecondaryAnchorCenterY;
        _quickPickPressTimestamp = Environment.TickCount64;
        _quickPickTapSuppressed = false;
        ++_quickPickRevision;

        ShowQuickDrawRing(button);
        StartQuickPickTimer();

        // 刻意不设置 e.Handled：按钮仍要收到这次按下，短按松手后它的 Click 才会正常触发。
        return true;
    }

    private bool TryHandleQuickPickPointerMoved(PointerEventArgs e)
    {
        if (_quickPickState == QuickPickState.Holding)
        {
            if (!QuickDrawLongPressGesture.ShouldCancelForDrag(
                    _quickPickPressPoint,
                    e.GetPosition(this),
                    _quickPickIsTouch))
            {
                return true;
            }

            // 按住却拖远了：这次按下改判为拖窗，长按环立刻消失（按钮的 Click 也不会再触发）。
            CancelQuickDrawRing();
            _quickPickTapSuppressed = true;

            // 不允许拖窗时这次按下就只是一次失败的尝试：移动继续吞掉，松手由下面的收尾逻辑压住，
            // 不能让按钮把"按住后滑开"再当成一次点击抽奖。
            if (!EffectiveFloatingWindowSettings.Draggable)
                return true;

            _isPendingWindowDrag = true;
            _isMovingWindow = false;
            _windowDragStartedByMouse = !_quickPickIsTouch;
            _windowDragStartScreenPoint = _quickPickPressScreenPoint;
            _windowDragStartPosition = Position;
            _windowDragScreen = GetScreenForWindow(Position);
            return false;
        }

        if (_quickPickState != QuickPickState.Picking)
            return false;

        FollowQuickPickPointer(e.GetPosition(this));
        return true;
    }

    /// <summary>快选面板开着时又按下：按当前高亮的人数收尾；返回 <c>true</c> 表示这次按下已被接管。</summary>
    private bool TryHandleQuickPickPointerPressed(PointerPressedEventArgs e)
    {
        if (_quickPickState != QuickPickState.Picking)
            return false;

        // 触控长按可能先被我们自己的环判成"已满时长"，系统随后仍会把同一次按住补成一次右键。
        // 那不是"再点一下确认"：把它的按下和配对的抬起一起吞掉，否则面板刚出现就立刻抽走一个人。
        if (IsSecondaryButtonPress(e))
        {
            if (_quickPickIsTouch)
            {
                // The promoted right button belongs to the original touch gesture. Let its
                // release finish the already-open picker instead of swallowing that release.
                _quickPickPointer = e.Pointer;
                _quickPickIsSecondaryButton = true;
                return true;
            }

            _quickPickSwallowSecondaryRelease = true;
            return true;
        }

        // 不接管的话，这次按下会掉进下面的拖窗分支，面板跟着指针跑、松手还把窗口留在半路。
        var count = _quickPickCount;
        EndQuickPick(draw: count >= 1, count);
        return true;
    }

    private bool TryHandleQuickPickPointerReleased(PointerReleasedEventArgs e)
    {
        if (_quickPickSwallowSecondaryRelease)
        {
            // 配对被吞掉的那次右键按下：这次抬起也不能拿来收尾，否则等于又确认了一遍。
            _quickPickSwallowSecondaryRelease = false;
            return true;
        }

        if (_quickPickState == QuickPickState.Holding)
        {
            // 短按也在这里收口，不再依赖按钮自己的 Click：满时长那一刻本窗会抢走按钮的指针捕获，
            // 两条路分开处理才不会出现"点一下没反应"。手指基本没动才算一次点按。
            var isTap = !QuickDrawLongPressGesture.ShouldCancelForDrag(
                _quickPickPressPoint,
                e.GetPosition(this),
                _quickPickIsTouch);
            var wasSecondaryButton = _quickPickIsSecondaryButton;
            CancelQuickDrawRing();
            if (!isTap)
            {
                // 位移已经超出容差却还停在按住态（一次移动事件都没收到）：这次按下已经是拖窗，
                // 收尾拖动并压掉按钮的 Click，免得松手时又抽一次。
                CompleteWindowDrag(e.Pointer);
                return true;
            }

            // 按钮在按下时已经拿走了捕获，而这里的松手不会再到它手上：先交还捕获，按钮才会把
            // "按下中"的视觉状态收回去，否则下一次看到它还是按下去的样子。
            e.Pointer.Capture(null);

            // 右键只是触控长按的替身：按一下就走完整个长按，没等满确认时长说明用户并不想快选，
            // 这时不能像左键那样"点一下抽 1 人"，否则一次普通右键点击会凭空抽一次。
            if (wasSecondaryButton)
                return true;

            // 和按钮原来的 Click 完全同一个语义：短按抽 1 人。触控点一下也走这里。
            App.NotifyFloatingWindowInteraction(this);
            App.ShowQuickDrawWindow(noticeAnchor: this);
            return true;
        }

        if (_quickPickState != QuickPickState.Picking)
        {
            // 长按被拖窗取消后按钮可能仍会收到这次松手：吞掉它（返回 true 会置 Handled，
            // 按钮不再触发 Click），但拖窗状态必须同时收尾——只吞事件不清理的话
            // _isPendingWindowDrag/_isMovingWindow 会一直挂着，松手后窗口还跟着指针走。
            if (!_quickPickTapSuppressed)
                return false;

            _quickPickTapSuppressed = false;
            CompleteWindowDrag(e.Pointer);
            return true;
        }

        var count = _quickPickCount;
        EndQuickPick(draw: count >= 1, count);
        return true;
    }

    private void OnQuickPickCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_quickPickState == QuickPickState.Picking)
        {
            // 满时长时本窗从按钮手里接过捕获，按钮丢捕获同样会冒泡到这儿：那是预期的交接，不是取消。
            // 只要指针的捕获仍然在本窗手上，就说明这次"丢捕获"来自按钮，忽略即可。
            if (_quickPickPointer is { Captured: not null } pointer
                && ReferenceEquals(pointer.Captured, this))
            {
                return;
            }

            // 真正的打断（触控被系统收走、窗口去激活等）：用户已经指到哪一行就抽哪一行，
            // 中途消失不该变成"白按一场"。
            var count = _quickPickCount;
            EndQuickPick(draw: count >= 1, count);
            return;
        }

        if (_quickPickState == QuickPickState.Holding)
        {
            // 触控/手写笔按住中途"丢捕获"通常不是用户取消，而是系统把手势接管走了
            // （Windows 的"长按转右键"就是先收走触控、再补一次右键）。这时环不能灭：
            // 计时继续跑，右键若随后到达会直接重开这次长按；真没有任何后续事件时，
            // BeginQuickPickAsync 会发现捕获没拿到并原样收回去。
            if (_quickPickIsTouch)
                return;

            CancelQuickDrawRing();
        }
    }

    private void AttachQuickPickCaptureHandler()
    {
        if (_quickPickCaptureHandlerAttached)
            return;

        _quickPickCaptureHandlerAttached = true;
        AddHandler(PointerCaptureLostEvent, OnQuickPickCaptureLost, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>本窗是否仍握着这次快选的指针（触控被系统收走时会变成 <c>false</c>）。</summary>
    private bool IsQuickPickPointerCaptured =>
        _quickPickPointer is { Captured: not null } pointer && ReferenceEquals(pointer.Captured, this);

    /// <summary>把指针抢到本窗；指针已经失效（系统收走）时放弃，由调用方走"没有指针"的兜底。</summary>
    private void TryCaptureQuickPickPointer()
    {
        try
        {
            _quickPickPointer?.Capture(this);
        }
        catch (Exception)
        {
            // 对已经结束/被系统接管的指针取捕获会抛异常。这里只影响这一个兜底分支，
            // 绝不能让它从计时器回调里逃出去（那会把整个快选卡在 Picking）。
        }
    }

    private static Button? FindQuickDrawButton(Visual? visual)
    {
        while (visual != null)
        {
            if (visual is Button { Name: QuickDrawButtonName } button)
                return button;
            visual = visual.GetVisualParent();
        }

        return null;
    }

    private void ShowQuickDrawRing(Button button)
    {
        var origin = button.TranslatePoint(new Point(0, 0), this);
        if (origin is null)
        {
            CancelQuickDrawRing();
            return;
        }

        // 环的两个端点色取当前主题强调色：浅→满，轨迹是同色低透明度，深色/浅色主题都跟着走。
        var accent = QuickPickAccent;
        _quickDrawRing.StrokeThickness = 3;
        _quickDrawRing.StartColor = accent;
        _quickDrawRing.EndColor = QuickDrawLongPressGesture.Lighten(accent, 0.45);
        _quickDrawRing.TrackBrush = new SolidColorBrush(Color.FromArgb(56, accent.R, accent.G, accent.B));
        if (_quickDrawRing.Parent is null)
            QuickDrawRingLayer.Children.Add(_quickDrawRing);

        var bounds = button.Bounds;
        _quickDrawRing.CornerRadius = 9;
        _quickDrawRing.Width = bounds.Width + (RingInset * 2);
        _quickDrawRing.Height = bounds.Height + (RingInset * 2);
        _quickDrawRing.Progress = 0;
        _quickDrawRing.Opacity = 1;
        _quickDrawRing.IsVisible = true;
        Canvas.SetLeft(_quickDrawRing, origin.Value.X - RingInset);
        Canvas.SetTop(_quickDrawRing, origin.Value.Y - RingInset);
    }

    private void CancelQuickDrawRing()
    {
        _quickPickState = QuickPickState.Idle;
        _quickPickRestoring = false;
        _quickPickPointer = null;
        _quickPickIsSecondaryButton = false;
        _quickPickSwallowSecondaryRelease = false;
        ++_quickPickRevision;
        StopQuickPickTimer();
        _quickDrawRing.IsVisible = false;
        _quickDrawRing.Progress = 0;
        _quickPickCount = -1;
    }

    private void StartQuickPickTimer()
    {
        StopQuickPickTimer();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AnimationFramePolicy.FrameIntervalMs) };
        timer.Tick += OnQuickPickTick;
        _quickPickTimer = timer;
        timer.Start();
    }

    private void StopQuickPickTimer()
    {
        if (_quickPickTimer is not { } timer)
            return;

        timer.Stop();
        timer.Tick -= OnQuickPickTick;
        _quickPickTimer = null;
    }

    private void OnQuickPickTick(object? sender, EventArgs e)
    {
        if (_quickPickState != QuickPickState.Holding)
        {
            StopQuickPickTimer();
            return;
        }

        var duration = QuickDrawLongPressGesture.ResolveHoldDuration(
            EffectiveFloatingWindowSettings.LongPressDuration,
            _quickPickIsSecondaryButton);
        var progress = QuickDrawLongPressGesture.ResolveProgress(
            Environment.TickCount64 - _quickPickPressTimestamp,
            duration);
        _quickDrawRing.Progress = progress;

        if (progress >= 1)
            _ = BeginQuickPickAsync();
    }

    private async Task BeginQuickPickAsync()
    {
        if (_quickPickState != QuickPickState.Holding)
            return;

        _quickPickState = QuickPickState.Picking;
        // A previous interrupted gesture must not turn the next quick-pick release into a
        // drag completion. The panel owns the pointer for its entire lifetime.
        _isPendingWindowDrag = false;
        _isMovingWindow = false;
        _isNativeWindowDrag = false;
        _isMovingDockHandle = false;
        StopQuickPickTimer();
        _quickDrawRing.Progress = 1;
        var revision = ++_quickPickRevision;

        // 宿主始终透明，快选只显示自身的球和滑轨，不额外铺设窗口背景。

        // 让已经排上的自动收纳失效：否则按住不放的这几秒里窗口可能自己收纳，松手后就变成
        // "展开态和收纳球同时在屏上"的怪样子。
        ++_dockRevision;
        ++_dockTransitionRevision;
        // A pending edge-snap from the previous interaction must not write the temporary
        // quick-pick position while this gesture is active.
        ++_snapAnimationRevision;

        // 从这一刻起由本窗接管指针：按钮收不到这次松手，因此不会再触发一次"抽 1 人"。
        // 抢捕获会让按钮抛 PointerCaptureLost，那是交接；OnQuickPickCaptureLost 靠
        // "捕获是否仍在本窗手上"区分交接与真正的打断。
        TryCaptureQuickPickPointer();

        // 收起展开态：淡出 + 轻微缩放，和收纳动画同一组参数，手感一致。
        await AnimateControlAsync(
            ExpandedContent,
            ExpandedContent.Opacity,
            0,
            1,
            0.92,
            GetDockTransformOrigin(),
            revision);
        if (revision != _quickPickRevision || _quickPickState != QuickPickState.Picking)
            return;

        _quickDrawRing.IsVisible = false;
        WindowBorder.IsVisible = false;
        ExpandedContent.IsVisible = false;
        // 收纳球也不能跟着露出来：快选态只该有球和滑轨，不能多出一个"抽"方块。
        DockButton.IsVisible = false;
        DockButton.Opacity = 0;

        // 另一侧悬浮窗（通常是收成球的那一扇）也先让位：快选是"整块浮窗收起来、只剩指针旁一颗球"，
        // 桌面上还飘着另一颗球就不是这个语义了。
        App.NotifyQuickPickStarted(this);

        var settings = EffectiveFloatingWindowSettings;
        // 触控/手写笔按行距更大的滑轨布局：手指点不准 44 DIP 的行，也点不准 78 DIP 宽的滑轨。
        var isTouch = _quickPickIsTouch;
        var rowHeight = isTouch ? 52d : 44d;
        var pickerWidth = isTouch ? 96d : 78d;
        var orbSize = QuickDrawLongPressGesture.ResolveOrbSize(settings.DockedWindowSize);

        // 滑轨永远在浮窗对侧：浮窗贴右就从左边弹出，浮窗贴左就从右边弹出。
        var pickerOnLeft = QuickDrawLongPressGesture.ResolvePickerOnLeft(IsOnLeftSide);
        var rowCounts = QuickDrawLongPressGesture.ResolveRowCounts(pickerOnLeft);
        var layout = QuickDrawLongPressGesture.ResolveLayout(
            orbSize,
            pickerWidth,
            rowCounts.Count,
            rowHeight,
            pickerOnLeft);

        _quickPickWindowSize = layout.WindowSize;
        _quickPickPickerRect = layout.PickerRect;
        _quickPickOrbSize = orbSize;
        _quickPickRowHeight = rowHeight;
        _quickPickRowCounts = rowCounts;

        // 刚弹出就指向"1 人"：按住只是想要一次普通的闪抽，要多抽才需要滑到对应行。
        _quickPickCount = QuickDrawLongPressGesture.DefaultCount;
        _quickPickOrbCenterX = pickerOnLeft
            ? layout.WindowSize.Width - QuickDrawLongPressGesture.SlabPadding - (orbSize / 2)
            : QuickDrawLongPressGesture.SlabPadding + (orbSize / 2);

        SizeToContent = SizeToContent.Manual;
        Width = layout.WindowSize.Width;
        Height = layout.WindowSize.Height;

        PrepareQuickPickVisuals(orbSize, rowHeight, layout);
        QuickPickLayer.IsVisible = true;
        QuickPickLayer.Opacity = 0;
        PositionQuickPickWindowAtPressPoint();

        // 面板是"把 1 人那一行对准按下点"摆出来的，窗口一挪，按下点在这个新坐标系里就不再是
        // 原来那个点。不跟着换，之后每一次指针移动都会被判成"早就离开按下点了"：球会被夹到
        // 错误的纵向位置、选中行在按下点附近来回跳，用户看到的就是浮窗上下抽搐。
        _quickPickPressPoint = new Point(_quickPickOrbCenterX, _quickPickAnchorOffset);

        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render).GetTask();
        if (revision != _quickPickRevision || _quickPickState != QuickPickState.Picking)
            return;

        // 捕获没拿到（触控指针已经被系统为"长按转右键"收走，且右键没跟上来）：
        // 这一轮快选没有可用指针，原样收回去，绝不能停在一个松手也关不掉的面板上。
        if (!IsQuickPickPointerCaptured)
        {
            EndQuickPick(draw: false, count: 0);
            return;
        }

        QuickPickLayer.Opacity = 1;
        await AnimateControlAsync(QuickPickOrb, 0, 1, 0.55, 1, RelativePoint.Center, revision);
        await AnimateControlAsync(QuickPickCountPicker, 0, 1, 0.92, 1, new RelativePoint(0, 0.5, RelativeUnit.Relative), revision);
    }

    private void PrepareQuickPickVisuals(double orbSize, double rowHeight, QuickPickLayout layout)
    {
        var pickerRect = layout.PickerRect;

        QuickPickOrb.Width = orbSize;
        QuickPickOrb.Height = orbSize;
        QuickPickOrb.CornerRadius = new CornerRadius(orbSize / 2);
        Canvas.SetLeft(QuickPickOrb, _quickPickOrbCenterX - (orbSize / 2));
        Canvas.SetTop(QuickPickOrb, (layout.WindowSize.Height / 2) - (orbSize / 2));
        QuickPickOrb.Opacity = 1;

        // 球里的图标每次都按当前球径重建：收纳球尺寸是可配置的，字号必须跟着走。
        QuickPickOrbIconHost.Children.Clear();
        QuickPickOrbIconHost.Children.Add(new FluentIcon(FluentIcons.FlashFilled, orbSize * 0.46));
        // 刚弹出就指向"1 人"：徽标立刻显示，滑动后跟着换。
        QuickPickBadge.IsVisible = true;
        QuickPickBadgeText.Text = QuickDrawLongPressGesture.DefaultCount.ToString();

        QuickPickCountPicker.Width = pickerRect.Width;
        QuickPickCountPicker.RowHeight = rowHeight;
        QuickPickCountPicker.LabelFontSize = Math.Max(12, rowHeight * 0.4);
        QuickPickCountPicker.ExitText = QuickPickExitText;
        QuickPickCountPicker.RowCounts = _quickPickRowCounts;
        QuickPickCountPicker.SelectedCount = QuickDrawLongPressGesture.DefaultCount;
        Canvas.SetLeft(QuickPickCountPicker, pickerRect.X);
        Canvas.SetTop(QuickPickCountPicker, pickerRect.Y);

        // 滑轨这一列共用一块圆角底衬（只包 6 行、不含球）：行本身透明，只有当前指向的那一行亮成芯片。
        var accent = QuickPickAccent;
        const double pickerSurfaceInset = 6;
        QuickPickPickerSurface.Width = pickerRect.Width + (pickerSurfaceInset * 2);
        QuickPickPickerSurface.Height = pickerRect.Height + (pickerSurfaceInset * 2);
        QuickPickPickerSurface.CornerRadius = new CornerRadius(18);
        QuickPickPickerSurface.Background = CreateRowBrush(214);
        QuickPickPickerSurface.BorderBrush =
            new SolidColorBrush(Color.FromArgb(46, accent.R, accent.G, accent.B));
        Canvas.SetLeft(QuickPickPickerSurface, pickerRect.X - pickerSurfaceInset);
        Canvas.SetTop(QuickPickPickerSurface, pickerRect.Y - pickerSurfaceInset);

        // 颜色全部跟着主题强调色走：底衬是半透明面板色，选中行是强调色高光。
        QuickPickCountPicker.RowBrush = Brushes.Transparent;
        QuickPickCountPicker.ActiveRowBrush = CreateAccentBrush();
        QuickPickCountPicker.ActiveRowBorderBrush =
            new SolidColorBrush(QuickDrawLongPressGesture.Lighten(accent, 0.35));
        QuickPickCountPicker.ActiveRowShadow = BoxShadows.Parse(
            $"0 0 18 2 #{accent.R:X2}{accent.G:X2}{accent.B:X2}");
        QuickPickOrb.Background = CreateAccentBrush();
    }

    private void PositionQuickPickWindowAtPressPoint()
    {
        // 让"1 人"那一行正对按下的高度、球心正对按下的横向位置，再把整块面板夹进工作区：
        // 球与滑轨都不出屏，滑轨被压向哪一端，"退出"就落在那一端。
        var rowIndex = QuickDrawLongPressGesture.ResolveRowIndex(
            _quickPickRowCounts,
            QuickDrawLongPressGesture.DefaultCount);
        var anchorOffset = _quickPickPickerRect.Y
            + QuickDrawLongPressGesture.ResolveRowCenterOffset(rowIndex, _quickPickRowHeight);
        // 先把锚点记下来：即使下面找不到屏幕（面板位置保持不动），按下点也仍要按这个锚点换算。
        _quickPickAnchorOffset = anchorOffset;

        var scaling = RenderScaling;
        var width = Math.Max(1, (int)Math.Ceiling(_quickPickWindowSize.Width * scaling));
        var height = Math.Max(1, (int)Math.Ceiling(_quickPickWindowSize.Height * scaling));
        var workingArea = GetScreenForWindow(_quickPickPressScreenPoint, width, height)?.WorkingArea
            ?? GetScreenForWindow(Position)?.WorkingArea;
        if (workingArea is not { } area)
            return;

        var desired = new PixelPoint(
            _quickPickPressScreenPoint.X - (int)Math.Round(_quickPickOrbCenterX * scaling),
            _quickPickPressScreenPoint.Y - (int)Math.Round(anchorOffset * scaling));
        Position = ClampToWorkingArea(desired, width, height, area);
    }

    private void FollowQuickPickPointer(Point pointerInWindow)
    {
        // 球沿滑轨所在的纵向通道跟随指针；横向固定在通道里，避免球压到滑轨上。
        var orbSize = _quickPickOrbSize;
        var center = QuickDrawLongPressGesture.ResolveOrbCenter(
            new Point(_quickPickOrbCenterX, pointerInWindow.Y),
            _quickPickWindowSize,
            orbSize);
        Canvas.SetLeft(QuickPickOrb, center.X - (orbSize / 2));
        Canvas.SetTop(QuickPickOrb, center.Y - (orbSize / 2));

        // 刚弹出时指针正落在"1 人"那一行上：还没动就保持 1，动起来才跟着换。
        if (!QuickDrawLongPressGesture.ShouldCancelForDrag(
                _quickPickPressPoint,
                pointerInWindow,
                QuickDrawLongPressGesture.ResolveSelectionActivationDistance(_quickPickIsTouch)))
        {
            _quickPickCount = QuickDrawLongPressGesture.DefaultCount;
            QuickPickCountPicker.SelectedCount = QuickDrawLongPressGesture.DefaultCount;
            QuickPickBadge.IsVisible = true;
            QuickPickBadgeText.Text = QuickDrawLongPressGesture.DefaultCount.ToString();
            return;
        }

        var count = QuickDrawLongPressGesture.ResolveCount(
            pointerInWindow.Y,
            _quickPickPickerRect.Y,
            _quickPickPickerRect.Height,
            _quickPickRowCounts);
        if (count == _quickPickCount)
            return;

        _quickPickCount = count;
        QuickPickCountPicker.SelectedCount = count;
        QuickPickBadge.IsVisible = true;
        QuickPickBadgeText.Text = count <= 0
            ? QuickPickExitText
            : count.ToString();
    }

    /// <summary>
    ///     结束快选：恢复展开态，可选地按 <paramref name="count" /> 真正开抽。
    /// </summary>
    /// <remarks>
    ///     整段收尾刻意**同步**做完再去开抽：以前先 await 两段动画，动画只要抛一次异常（或动画中途
    ///     窗口被别的路径改状态）就会把 <c>App.ShowQuickDrawWindow</c> 整个跳过，用户看到的就是
    ///     "选好人却没抽"。恢复展开态本身没有必须等待的视觉步骤，所以直接落到终值，开抽之后再补动画。
    /// </remarks>
    private void EndQuickPick(bool draw, int count)
    {
        if (_quickPickState != QuickPickState.Picking)
            return;

        _quickPickRestoring = true;
        _quickPickState = QuickPickState.Idle;
        ++_quickPickRevision;

        // 先把让位的那一侧还原，再恢复本窗：两扇窗都回位后才是用户想要的"跟按下前一模一样"。
        App.NotifyQuickPickFinished(this);
        _quickPickPointer?.Capture(null);
        _quickPickPointer = null;
        _quickPickCount = -1;
        _quickPickSwallowSecondaryRelease = false;

        QuickPickLayer.IsVisible = false;
        QuickPickLayer.Opacity = 1;
        QuickPickOrb.Opacity = 1;
        QuickPickCountPicker.Opacity = 1;

        // 交回 SizeToContent 重新量一遍，再挪回按下前的位置。
        SizeToContent = SizeToContent.WidthAndHeight;
        Width = double.NaN;
        Height = double.NaN;

        // 窗口级材质从头到尾都不动，保持用户设置的样子。

        // 快选只能从展开态开始。即使某个后台布局回调在收尾期间把停靠标志改了，
        // 也要按开始时的展开态恢复，不能把停靠动画的中间坐标带回配置。
        _isDocked = false;
        WindowBorder.IsVisible = true;
        ExpandedContent.IsVisible = true;
        // 收起那一下留的缩放/透明度缓动先清掉，再由动画从 0.96 淡入到 1；
        // 低配模式里 AnimateControlAsync 会直接落终值，不播动画。
        ExpandedContent.RenderTransform = null;
        ExpandedContent.Opacity = 0;
        DockButton.IsVisible = false;
        DockButton.Opacity = 1;
        DockButton.RenderTransform = null;
        Position = _quickPickOriginPosition;
        ReassertNativeTopmost();

        // 交回 SizeToContent 之后，展开尺寸要等布局跑完才成立，窗口可能在那一下被重新摆放。
        // 等这一帧的布局落地再把原位和持久化坐标一起钉回去。
        var settleRevision = _quickPickRevision;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_quickPickRevision != settleRevision)
                {
                    _quickPickRestoring = false;
                    return;
                }

                RestoreQuickPickOrigin();
                _quickPickRestoring = false;
            },
            DispatcherPriority.Render);

        // 恢复展开态要有动画，不然长按结束后那一下就"跳"回来了。
        _ = AnimateControlAsync(
            ExpandedContent,
            0,
            1,
            0.96,
            1,
            GetDockTransformOrigin(),
            _quickPickRevision);

        // 刚把 SizeToContent 交回去时 Bounds 还停在快选那块小面板的尺寸上，这时判断"贴边"
        // 会算出错误的距离，自动收纳就再也不排了；等一帧把展开尺寸量回来再判断。
        Dispatcher.UIThread.Post(ScheduleDockIfAtEdge, DispatcherPriority.Render);

        if (draw)
            App.ShowQuickDrawWindow(count, noticeAnchor: this);
    }

    /// <summary>
    ///     快选面板使用临时尺寸和位置；布局完成后把窗口内部状态及已经可能被后台回调写入的
    ///     配置一并恢复到按下前的展开态。
    /// </summary>
    private void RestoreQuickPickOrigin()
    {
        _isPendingWindowDrag = false;
        _isMovingWindow = false;
        _isNativeWindowDrag = false;
        _isMovingDockHandle = false;
        _isDocked = _quickPickOriginWasDocked;
        _isDockedOnLeft = _quickPickOriginWasDockedOnLeft;
        _dockWorkingArea = _quickPickOriginDockWorkingArea;
        _dockAnchorCenterY = _quickPickOriginAnchorCenterY;
        _expandedWindowWidth = _quickPickOriginExpandedWindowWidth;
        _expandedWindowHeight = _quickPickOriginExpandedWindowHeight;
        Position = _quickPickOriginPosition;

        var config = ViewModel.Config;
        var changed = config.FloatPosition.X != _quickPickOriginFloatPositionX
                      || config.FloatPosition.Y != _quickPickOriginFloatPositionY;
        if (changed)
        {
            config.FloatPosition = new FloatPositionConfig
            {
                X = _quickPickOriginFloatPositionX,
                Y = _quickPickOriginFloatPositionY
            };
        }

        var settings = EffectiveFloatingWindowSettings;
        if (settings.SecondaryAnchorCenterY != _quickPickOriginSecondaryAnchorCenterY)
        {
            settings.SecondaryAnchorCenterY = _quickPickOriginSecondaryAnchorCenterY;
            changed = true;
        }

        if (changed && !IsSimpleMode)
            IAppHost.TryGetService<MainConfigHandler>()?.Save();
    }
}
