using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
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
    }

    public TimerViewModel ViewModel { get; } = IAppHost.GetService<TimerViewModel>();
    private int _ticks = 0;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        ViewModel.HandleKey(e.Key);
        e.Handled = e.Key is Key.Space or Key.R or Key.Up or Key.Down;
    }

    private void OpenMiniWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
