using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Views;
using SecRandom.ViewModels.MainPages;
using SecRandom.Services.ViewEngine;

namespace SecRandom.Views.MainPages;

public sealed partial class TimerView : ViewBase
{
    public TimerView()
    {
        Header = ViewModel.PageTitle;
        DataContext = ViewModel;
        InitializeComponent();
        ExitFullScreenButton.AddHandler(PointerPressedEvent, ExitFullScreen_OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        ExitFullScreenButton.AddHandler(PointerMovedEvent, ExitFullScreen_OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        ExitFullScreenButton.AddHandler(PointerReleasedEvent, ExitFullScreen_OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        ExitFullScreenButton.AddHandler(PointerCaptureLostEvent, ExitFullScreen_OnPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);

        // 只在本视图可见期间驱动 33ms 刷新，页面关闭后计时器不再空转
        AttachedToVisualTree += (_, _) =>
        {
            ViewModel.AttachRefresh();
            if (TopLevel.GetTopLevel(this) is Window window)
                IAppHost.GetService<TimerViewService>().AttachFullViewWindow(window);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            CancelExitHold();
            ViewModel.DetachRefresh();
        };

        // 指针在窗口里动一下、按一下都算"有人在用这个页面"：否则鼠标正停在窗口上时也会被缩下去。
        // 用隧道 + handledEventsToo，按钮/滑块处理过的事件同样算数。
        AddHandler(PointerPressedEvent, OnPointerActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public TimerViewModel ViewModel { get; } = IAppHost.GetService<TimerViewModel>();
    private int _ticks = 0;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        ViewModel.NotifyUserActivity();
        ViewModel.HandleKey(e.Key);
        e.Handled = e.Key is Key.Space or Key.R or Key.Up or Key.Down;
    }

    private void OnPointerActivity(object? sender, PointerEventArgs e) => ViewModel.NotifyUserActivity();

    private void OpenMiniWindow(object? sender, RoutedEventArgs e)
    {
        IAppHost.GetService<TimerViewService>().ShowMiniWindow();
    }

    private void ToggleFullScreen(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
            IAppHost.GetService<TimerViewService>().ToggleFullScreen(window);
    }

    private static readonly TimeSpan ExitHoldDuration = TimeSpan.FromSeconds(2);
    private readonly Stopwatch _exitHoldClock = new();
    private readonly DispatcherTimer _exitHoldTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private IPointer? _exitPointer;

    private void ExitFullScreen_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ViewModel.IsFullScreen || _exitPointer is not null || !e.GetCurrentPoint(ExitFullScreenButton).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;
        _exitPointer = e.Pointer;
        e.Pointer.Capture(ExitFullScreenButton);
        ExitHoldProgress.Value = 0;
        ExitHoldProgress.Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        ExitHoldProgress.IsVisible = true;
        _exitHoldClock.Restart();
        _exitHoldTimer.Tick += ExitHoldTimerOnTick;
        _exitHoldTimer.Start();
    }

    private void ExitFullScreen_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer == _exitPointer && !new Rect(ExitFullScreenButton.Bounds.Size).Contains(e.GetPosition(ExitFullScreenButton)))
            CancelExitHold();
    }

    private void ExitFullScreen_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer == _exitPointer)
            CancelExitHold();
    }

    private void ExitFullScreen_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (e.Pointer == _exitPointer)
            CancelExitHold();
    }

    private void ExitHoldTimerOnTick(object? sender, EventArgs e)
    {
        if (!ViewModel.IsFullScreen)
        {
            CancelExitHold();
            return;
        }

        ViewModel.NotifyUserActivity();
        var progress = Math.Clamp(_exitHoldClock.Elapsed.TotalMilliseconds / ExitHoldDuration.TotalMilliseconds, 0, 1);
        ExitHoldProgress.Value = progress;
        ExitHoldProgress.Foreground = new SolidColorBrush(Color.FromRgb(
            (byte)(215 * progress), (byte)(120 * (1 - progress)), (byte)(215 * (1 - progress))));
        if (progress < 1)
            return;

        CancelExitHold();
        if (ViewModel.IsFullScreen && TopLevel.GetTopLevel(this) is Window window)
            IAppHost.GetService<TimerViewService>().ToggleFullScreen(window);
    }

    private void CancelExitHold()
    {
        _exitHoldTimer.Stop();
        _exitHoldTimer.Tick -= ExitHoldTimerOnTick;
        _exitHoldClock.Reset();
        ExitHoldProgress.IsVisible = false;
        ExitHoldProgress.Value = 0;
        var pointer = _exitPointer;
        _exitPointer = null;
        if (pointer?.Captured == ExitFullScreenButton)
            pointer.Capture(null);
    }

    private void DisplayTime_OnClick(object? sender, PointerPressedEventArgs e)
    {
        if (!ViewModel.IsClockMode)
            return;
        
        _ticks += 1;
        if (_ticks == 10)
        {
            ToolTip.SetTip(ClockHintTextBlock, Langs.MainPages.Timer.Resources.C_Tips);
            ToolTip.SetShowDelay(ClockHintTextBlock, 10000);
        }
    }
}
