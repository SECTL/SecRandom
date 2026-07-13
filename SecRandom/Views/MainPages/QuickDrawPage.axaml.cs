using System;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Helpers;
using SecRandom.ViewModels.MainPages;

namespace SecRandom.Views.MainPages;

public partial class QuickDrawPage : UserControl
{
    private bool _isUnloaded;
    private int _autoCloseRevision;
    private CancellationTokenSource? _autoCloseCts;
    private readonly ItemsControl? _resultPresenter;

    public QuickDrawPage()
    {
        ViewModel = IAppHost.GetService<QuickDrawPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        _resultPresenter = this.FindControl<ItemsControl>("ResultPresenter");
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        ViewModel.Config.FloatingWindowSettings.PropertyChanged += FloatingWindowSettings_OnPropertyChanged;
        UpdateFloatingWindowOpacity();
        Unloaded += OnUnloaded;
    }

    public QuickDrawPageViewModel ViewModel { get; }

    public void StartDraw()
    {
        if (ViewModel.StartDrawCommand.CanExecute(null))
            ViewModel.StartDrawCommand.Execute(null);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        CancelAutoClose();
        ViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        ViewModel.Config.FloatingWindowSettings.PropertyChanged -= FloatingWindowSettings_OnPropertyChanged;
    }

    private void FloatingWindowSettings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FloatingWindowSettingsConfig.FloatingWindowOpacity))
            UpdateFloatingWindowOpacity();
    }

    private void UpdateFloatingWindowOpacity()
    {
        RootBorder.Opacity = System.Math.Clamp(
            ViewModel.Config.FloatingWindowSettings.FloatingWindowOpacity,
            20,
            100) / 100.0;
    }

    private void DragHandle_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !IsChildOfButton(e.Source as Control))
            (TopLevel.GetTopLevel(this) as Window)?.BeginMoveDrag(e);
    }

    private static bool IsChildOfButton(Control? control)
    {
        for (var current = control; current is not null; current = current.GetVisualParent() as Control)
        {
            if (current is Button)
                return true;
        }

        return false;
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e)
    {
        (TopLevel.GetTopLevel(this) as Window)?.Close();
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUnloaded)
            return;

        Dispatcher.UIThread.Post(async () => await RunResultAnimationAsync(e.PropertyName), DispatcherPriority.Render);
    }

    private async Task RunResultAnimationAsync(string? propertyName)
    {
        if (_isUnloaded)
            return;

        try
        {
            if (propertyName == nameof(QuickDrawPageViewModel.PreviewAnimationRevision))
            {
                CancelAutoClose();
                _autoCloseRevision++;
                await WaitForResultPresenterLayoutAsync();
                await DrawAnimationHelper.PreviewAsync(
                    _resultPresenter,
                    ViewModel.AnimationStyle,
                    ViewModel.PreviewAnimationDuration);
            }
            else if (propertyName == nameof(QuickDrawPageViewModel.ResultAnimationRevision))
            {
                var autoCloseRevision = ++_autoCloseRevision;
                CancelAutoClose();
                await WaitForResultPresenterLayoutAsync();
                if (_isUnloaded || autoCloseRevision != _autoCloseRevision)
                    return;
                await DrawAnimationHelper.RevealAsync(
                    _resultPresenter,
                    ViewModel.AnimationEnabled,
                    ViewModel.AnimationStyle,
                    ViewModel.AnimationDuration);
                if (_isUnloaded || autoCloseRevision != _autoCloseRevision)
                    return;
                await CloseAfterDelayAsync(autoCloseRevision);
            }
        }
        catch
        {
        }
    }

    private static async Task WaitForResultPresenterLayoutAsync()
    {
        for (var i = 0; i < 2; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render).GetTask();
            await Task.Delay(16);
        }
    }

    private async Task CloseAfterDelayAsync(int autoCloseRevision)
    {
        var seconds = System.Math.Clamp(ViewModel.Config.QuickDrawSettings.AutoCloseTime, 0, 60);
        if (seconds == 0)
            return;

        var cts = new CancellationTokenSource();
        _autoCloseCts = cts;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token);
            if (!_isUnloaded && autoCloseRevision == _autoCloseRevision && ReferenceEquals(_autoCloseCts, cts))
                (TopLevel.GetTopLevel(this) as Window)?.Close();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_autoCloseCts, cts))
                _autoCloseCts = null;
            cts.Dispose();
        }
    }

    private void CancelAutoClose()
    {
        _autoCloseCts?.Cancel();
        _autoCloseCts = null;
    }
}
