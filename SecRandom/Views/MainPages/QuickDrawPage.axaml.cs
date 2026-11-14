using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Behaviors;
using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Presentation;
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

    /// <summary>本次自动关闭是否走四周描边；描边创建失败时回退到原有的文字倒计时。</summary>
    private bool _usesEdgeCountdown;

    public QuickDrawPage()
    {
        ViewModel = IAppHost.GetService<QuickDrawPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        _resultPresenter = this.FindControl<ItemsControl>("ResultPresenter");
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        ViewModel.Config.EffectiveFloatingWindowSettings.PropertyChanged += FloatingWindowSettings_OnPropertyChanged;
        UpdateFloatingWindowOpacity();
        ApplyNotificationTheme(ViewModel.NotificationTheme);
        Unloaded += OnUnloaded;
    }

    public QuickDrawPageViewModel ViewModel { get; }

    /// <summary>
    /// 结果区完成一次排版（预览/揭晓/通知）后触发，宿主窗口据此按最新尺寸重新居中。
    /// 事件本身不含尺寸：窗口尺寸由宿主读取，避免页面耦合窗口实现。
    /// </summary>
    public event EventHandler? ResultLayoutChanged;

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
        ViewModel.Config.EffectiveFloatingWindowSettings.PropertyChanged -= FloatingWindowSettings_OnPropertyChanged;
    }

    private void FloatingWindowSettings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FloatingWindowSettingsConfig.FloatingWindowOpacity))
            UpdateFloatingWindowOpacity();
    }

    private void UpdateFloatingWindowOpacity()
    {
        RootBorder.Opacity = System.Math.Clamp(
            ViewModel.NotificationOpacity ?? ViewModel.Config.EffectiveFloatingWindowSettings.FloatingWindowOpacity,
            20,
            100) / 100.0;
    }

    /// <summary>
    ///     把通知窗口的 Fluent 画笔替换成按桌面亮度算出的自适应配色；null 表示恢复应用主题。
    /// </summary>
    /// <remarks>
    ///     写入的是 <see cref="RootBorder" /> 自己的资源字典。DynamicResource 从控件自身开始向上查找，
    ///     所以卡片里所有模板（结果项、头像占位、状态文字）会一起换色，而页面上层
    ///     <c>QuickDrawAvatarTemplate</c> 之类的模板资源不受影响；传 null 时清空该字典即可落回主题。
    /// </remarks>
    public void ApplyNotificationTheme(AdaptiveTheme? theme)
    {
        if (RootBorder is null)
            return;

        if (theme is not { } adaptive)
        {
            RootBorder.Resources = new ResourceDictionary();
            return;
        }

        RootBorder.Resources = new ResourceDictionary
        {
            ["SolidBackgroundFillColorBaseBrush"] = new SolidColorBrush(adaptive.Surface),
            ["ControlElevationBorderBrush"] = new SolidColorBrush(adaptive.Stroke),
            ["TextFillColorPrimaryBrush"] = new SolidColorBrush(adaptive.Foreground),
            ["TextFillColorSecondaryBrush"] = new SolidColorBrush(adaptive.SecondaryForeground),
            ["TextFillColorTertiaryBrush"] = new SolidColorBrush(adaptive.SecondaryForeground),
            ["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(adaptive.CardSurface),
            ["ControlFillColorSecondaryBrush"] = new SolidColorBrush(adaptive.AvatarSurface)
        };
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

        if (e.PropertyName == nameof(QuickDrawPageViewModel.NotificationTheme))
            ApplyNotificationTheme(ViewModel.NotificationTheme);

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
                ResultPresentationBridge.RestoreHostResult(_resultPresenter);
                if (await TryPresentAsync(DrawPresentationPhase.Preview))
                    return;
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
                if (await TryPresentAsync(DrawPresentationPhase.Reveal))
                {
                    await CloseAfterDelayAsync(autoCloseRevision);
                    return;
                }

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
                if (await TryPresentAsync(DrawPresentationPhase.Reveal))
                {
                    await CloseAfterDelayAsync(autoCloseRevision);
                    return;
                }
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
        finally
        {
            RaiseResultLayoutChanged();
        }
    }

    private void RaiseResultLayoutChanged()
    {
        ResultLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task<bool> TryPresentAsync(DrawPresentationPhase phase)
    {
        var students = ViewModel.LastResultStudents;
        return ResultPresentationBridge.TryPresentAsync(
            ResultPresentationBridge.BuildRequest(DrawPresentationChannel.QuickDraw, phase, students,
                listName: ViewModel.SelectedStudentListName, requestedCount: students.Count,
                pluginAnimationId: ViewModel.PluginAnimationId,
                displayTitles: ViewModel.LastResultDisplayTitles),
            _resultPresenter);
    }

    private static async Task WaitForResultPresenterLayoutAsync()
    {
        for (var i = 0; i < 2; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render).GetTask();
            await Task.Delay(AnimationFramePolicy.FrameIntervalMs);
        }
    }

    private async Task CloseAfterDelayAsync(int autoCloseRevision)
    {
        var seconds = ViewModel.ResultAutoCloseTime;
        _remainingAutoCloseSeconds = seconds;
        _usesEdgeCountdown = seconds > 0 && TryApplyEdgeCountdownAppearance();
        UpdateAutoCloseHint();
        if (seconds == 0)
            return;

        var cts = new CancellationTokenSource();
        _autoCloseCts = cts;
        var total = TimeSpan.FromSeconds(seconds);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (_usesEdgeCountdown)
            {
                // 描边按真实剩余时间逐帧收缩，而不是每秒跳一格：窗口缩小时也不会突然加快。
                while (true)
                {
                    var remaining = total - stopwatch.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                        break;

                    CountdownRing.Progress = remaining / total;
                    await Task.Delay(AnimationFramePolicy.FrameIntervalMs, cts.Token);
                }

                _remainingAutoCloseSeconds = 0;
            }
            else
            {
                while (_remainingAutoCloseSeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
                    _remainingAutoCloseSeconds--;
                    UpdateAutoCloseHint();
                }
            }

            if (!_isUnloaded && autoCloseRevision == _autoCloseRevision && ReferenceEquals(_autoCloseCts, cts))
                (TopLevel.GetTopLevel(this) as Window)?.Close();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            // 只有仍然是「当前这次自动关闭」时才收掉描边：更晚的一次已经接管时不能把它的描边藏掉。
            if (ReferenceEquals(_autoCloseCts, cts))
            {
                _autoCloseCts = null;
                HideCountdownRing();
            }

            cts.Dispose();
        }
    }

    private void CancelAutoClose()
    {
        _autoCloseCts?.Cancel();
        _autoCloseCts = null;
        _remainingAutoCloseSeconds = 0;
        _usesEdgeCountdown = false;
        HideCountdownRing();
        UpdateAutoCloseHint();
    }

    /// <summary>按配置决定这次自动关闭画不画四周描边；任何失败都退回文字倒计时。</summary>
    private bool TryApplyEdgeCountdownAppearance()
    {
        try
        {
            var settings = ViewModel.Config.NotificationSettings.Default;
            if (!settings.UsesEdgeCountdown)
                return false;

            CountdownRing.SetPalette(
                settings.CountdownColorMode == (int)NotificationCountdownColorMode.Gradient,
                settings.ResolvedCountdownColors);
            CountdownRing.Progress = 1;
            CountdownRing.IsVisible = true;
            return true;
        }
        catch
        {
            HideCountdownRing();
            return false;
        }
    }

    private void HideCountdownRing()
    {
        CountdownRing.IsVisible = false;
        CountdownRing.Progress = 1;
    }

    private void UpdateAutoCloseHint()
    {
        // 描边已经在表达剩余时间，这一行只留「三连击关闭」的静态说明，避免两个倒计时同时读秒。
        if (_usesEdgeCountdown && _remainingAutoCloseSeconds > 0)
        {
            AutoCloseHintTextBlock.Text =
                CommonResources.ResourceManager.GetString("C_QuickDrawManualCloseHint", CommonResources.Culture)
                ?? throw new InvalidOperationException("Quick draw manual-close localization is missing.");
            return;
        }

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
