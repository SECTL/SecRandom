using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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

        // 只在本视图可见期间驱动 33ms 刷新，页面关闭后计时器不再空转
        AttachedToVisualTree += (_, _) => ViewModel.AttachRefresh();
        DetachedFromVisualTree += (_, _) => ViewModel.DetachRefresh();

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
