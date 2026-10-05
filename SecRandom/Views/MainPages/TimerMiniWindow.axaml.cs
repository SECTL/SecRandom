using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using SecRandom.ViewModels.MainPages;

namespace SecRandom.Views.MainPages;

public sealed partial class TimerMiniWindow : Window
{
    private readonly Action _restore;
    private readonly TimerViewModel _viewModel;
    private bool _allowClose;

    public TimerMiniWindow(TimerViewModel viewModel, Action restore)
    {
        _restore = restore;
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        Loaded += OnLoaded;
        Closing += OnClosing;
        Opened += (_, _) => _viewModel.AttachRefresh();
        Closed += (_, _) => _viewModel.DetachRefresh();
    }

    internal void AllowClose() => _allowClose = true;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Width = 256;
        DisplayTime.FontFeatures = [FontFeature.Parse("+tnum")];
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return;

        e.Cancel = true;
        _restore();
    }

    private void Restore(object? sender, RoutedEventArgs e) => _restore();

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || IsButtonDescendant(e.Source as Visual))
            return;

        BeginMoveDrag(e);
    }

    private static bool IsButtonDescendant(Visual? visual)
    {
        while (visual is not null)
        {
            if (visual is Button)
                return true;
            visual = visual.GetVisualParent();
        }

        return false;
    }
}
