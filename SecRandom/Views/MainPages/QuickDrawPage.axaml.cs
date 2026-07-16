using System;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Helpers;
using SecRandom.ViewModels.MainPages;
using CommonResources = SecRandom.Langs.Common.Resources;

namespace SecRandom.Views.MainPages;

public partial class QuickDrawPage : UserControl
{
    private bool _isUnloaded;
    private int _autoCloseRevision;
    private CancellationTokenSource? _autoCloseCts;
    private readonly ItemsControl? _resultPresenter;
    private IPointer? _dragPointer;
    private PixelPoint _dragStartScreenPoint;
    private PixelPoint _dragStartWindowPosition;
    private CancellationTokenSource? _clickSequenceCts;
    private int _clickCount;
    private int _remainingAutoCloseSeconds;

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
        CancelClickSequence();
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
            ViewModel.NotificationOpacity ?? ViewModel.Config.FloatingWindowSettings.FloatingWindowOpacity,
            20,
            100) / 100.0;
    }

    private void RootBorder_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!App.SupportsProgrammaticWindowPositioning)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || TopLevel.GetTopLevel(this) is not Window window)
            return;

        _dragPointer = e.Pointer;
        _dragStartScreenPoint = window.Position + ToPixelPoint(e.GetPosition(window), window.RenderScaling);
        _dragStartWindowPosition = window.Position;
        e.Pointer.Capture(RootBorder);
    }

    private void RootBorder_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!App.SupportsProgrammaticWindowPositioning)
            return;
        if (!ReferenceEquals(e.Pointer, _dragPointer)
            || TopLevel.GetTopLevel(this) is not Window window)
            return;

        var screenPoint = window.Position + ToPixelPoint(e.GetPosition(window), window.RenderScaling);
        window.Position = new PixelPoint(
            _dragStartWindowPosition.X + screenPoint.X - _dragStartScreenPoint.X,
            _dragStartWindowPosition.Y + screenPoint.Y - _dragStartScreenPoint.Y);
    }

    private void RootBorder_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;

        if (ReferenceEquals(e.Pointer, _dragPointer))
        {
            _dragPointer = null;
            e.Pointer.Capture(null);
        }

        RegisterClick();
    }

    private static PixelPoint ToPixelPoint(Point point, double renderScaling)
    {
        return new PixelPoint(
            (int)Math.Round(point.X * renderScaling),
            (int)Math.Round(point.Y * renderScaling));
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUnloaded)
            return;

        if (e.PropertyName == nameof(QuickDrawPageViewModel.NotificationOpacity))
            UpdateFloatingWindowOpacity();

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
            else if (propertyName == nameof(QuickDrawPageViewModel.NotificationDisplayRevision))
            {
                var autoCloseRevision = ++_autoCloseRevision;
                CancelAutoClose();
                await WaitForResultPresenterLayoutAsync();
                if (_isUnloaded || autoCloseRevision != _autoCloseRevision)
                    return;
                if (ViewModel.NotificationAnimationEnabled)
                    await DrawAnimationHelper.RevealAsync(
                        _resultPresenter,
                        true,
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
        var seconds = ViewModel.ResultAutoCloseTime;
        _remainingAutoCloseSeconds = seconds;
        UpdateAutoCloseHint();
        if (seconds == 0)
            return;

        var cts = new CancellationTokenSource();
        _autoCloseCts = cts;
        try
        {
            while (_remainingAutoCloseSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
                _remainingAutoCloseSeconds--;
                UpdateAutoCloseHint();
            }

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
        _remainingAutoCloseSeconds = 0;
        UpdateAutoCloseHint();
    }

    private void UpdateAutoCloseHint()
    {
        AutoCloseHintTextBlock.Text = _remainingAutoCloseSeconds > 0
            ? string.Format(
                CommonResources.ResourceManager.GetString("C_QuickDrawAutoCloseHint", CommonResources.Culture)
                ?? throw new InvalidOperationException("Quick draw auto-close localization is missing."),
                _remainingAutoCloseSeconds)
            : CommonResources.ResourceManager.GetString("C_QuickDrawManualCloseHint", CommonResources.Culture)
              ?? throw new InvalidOperationException("Quick draw manual-close localization is missing.");
    }

    private void RegisterClick()
    {
        var clickCount = ++_clickCount;
        if (clickCount == 1)
        {
            RestartClickSequenceTimeout();
            return;
        }

        if (clickCount == 3)
        {
            CancelClickSequence();
            (TopLevel.GetTopLevel(this) as Window)?.Close();
        }
    }

    private void RestartClickSequenceTimeout()
    {
        _clickSequenceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _clickSequenceCts = cts;
        _ = ResetClickSequenceAfterDelayAsync(cts);
    }

    private async Task ResetClickSequenceAfterDelayAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
            if (ReferenceEquals(_clickSequenceCts, cts))
                _clickCount = 0;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_clickSequenceCts, cts))
                _clickSequenceCts = null;
            cts.Dispose();
        }
    }

    private void CancelClickSequence()
    {
        _clickSequenceCts?.Cancel();
        _clickSequenceCts = null;
        _clickCount = 0;
    }
}
