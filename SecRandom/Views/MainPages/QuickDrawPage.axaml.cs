using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Behaviors;
using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Presentation;
using SecRandom.Helpers;
using SecRandom.Shared;
using SecRandom.ViewModels.MainPages;
using CommonResources = SecRandom.Langs.Common.Resources;

namespace SecRandom.Views.MainPages;

public partial class QuickDrawPage : UserControl
{
    private bool _isUnloaded;
    private int _autoCloseRevision;
    private CancellationTokenSource? _autoCloseCts;
    private readonly ItemsControl? _resultPresenter;
    private CancellationTokenSource? _clickSequenceCts;
    private int _clickCount;
    private int _remainingAutoCloseSeconds;
    private Bitmap? _notificationBackgroundBitmap;
    private string? _loadedBackgroundImage;
    private Window? _notificationWindow;

    /// <summary>本次自动关闭是否走四周描边；描边创建失败时回退到原有的文字倒计时。</summary>
    private bool _usesEdgeCountdown;

    public QuickDrawPage()
    {
        ViewModel = IAppHost.GetService<QuickDrawPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        DrawBlockedNoticePopup.PlacementTarget = RootBorder;
        _resultPresenter = this.FindControl<ItemsControl>("ResultPresenter");
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        ViewModel.Config.EffectiveFloatingWindowSettings.PropertyChanged += FloatingWindowSettings_OnPropertyChanged;
        ViewModel.Config.NotificationSettings.Default.PropertyChanged += NotificationSettings_OnPropertyChanged;
        UpdateFloatingWindowOpacity();
        ApplyNotificationTheme(ViewModel.NotificationTheme);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public QuickDrawPageViewModel ViewModel { get; }

    /// <summary>
    /// 结果区完成一次排版（预览/揭晓/通知）后触发，宿主窗口据此按最新尺寸重新居中。
    /// 事件本身不含尺寸：窗口尺寸由宿主读取，避免页面耦合窗口实现。
    /// </summary>
    public event EventHandler? ResultLayoutChanged;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window
            || ReferenceEquals(_notificationWindow, window))
            return;

        _notificationWindow = window;
        ApplyNotificationTheme(ViewModel.NotificationTheme);
    }

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
        ViewModel.Config.NotificationSettings.Default.PropertyChanged -= NotificationSettings_OnPropertyChanged;
        if (_notificationWindow is not null)
        {
            _notificationWindow = null;
        }
        NotificationBackgroundBorder.Background = null;
        _notificationBackgroundBitmap?.Dispose();
        _notificationBackgroundBitmap = null;
    }

    private void FloatingWindowSettings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FloatingWindowSettingsConfig.FloatingWindowOpacity))
            UpdateFloatingWindowOpacity();
    }

    private void UpdateFloatingWindowOpacity()
    {
        var opacity = System.Math.Clamp(
            ViewModel.NotificationOpacity ?? ViewModel.Config.EffectiveFloatingWindowSettings.FloatingWindowOpacity,
            20,
            100) / 100.0;
        RootBorder.Opacity = opacity;
        NotificationBackgroundBorder.Opacity = opacity * Math.Clamp(
            ViewModel.Config.NotificationSettings.Default.BackgroundOpacity, 0, 100) / 100.0;
    }

    private void NotificationSettings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyNotificationTheme(ViewModel.NotificationTheme);
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

        var settings = ViewModel.Config.NotificationSettings.Default;
        var hasImage = TryLoadNotificationBackgroundImage(settings.BackgroundStyle == WindowBackgroundStyle.Image);
        var appPalette = AdaptiveBackdropResolver.FromAppTheme(
            Application.Current?.ActualThemeVariant == ThemeVariant.Dark);
        var palette = settings.BackgroundStyle == WindowBackgroundStyle.Theme ? appPalette : theme ?? appPalette;
        var brush = NotificationBackgroundBrushes.Create(settings, NotificationBackgroundBrushes.ResolveThemeSurfaceColor(ActualThemeVariant));
        if (settings.BackgroundStyle is WindowBackgroundStyle.Solid or WindowBackgroundStyle.Gradient)
        {
            static double Luminance(Color color) => (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
            var luminance = settings.BackgroundStyle == WindowBackgroundStyle.Solid
                ? Luminance(settings.BackgroundColor)
                : (Luminance(settings.BackgroundGradientStart) + Luminance(settings.BackgroundGradientEnd)) / 2;
            palette = AdaptiveBackdropResolver.FromAppTheme(luminance < 0.55);
        }

        RootBorder.Margin = new Thickness(6);
        CountdownRing.Margin = new Thickness(6);
        RootBorder.Background = hasImage ? CreateImageOverlayBrush()
            : settings.BackgroundStyle == WindowBackgroundStyle.Image ? new SolidColorBrush(palette.Surface) : brush;
        RootBorder.BorderBrush = new SolidColorBrush(palette.Stroke);
        RootBorder.Resources = hasImage ? CreateNotificationImageResources() : new ResourceDictionary
        {
            ["SolidBackgroundFillColorBaseBrush"] = brush,
            ["ControlElevationBorderBrush"] = new SolidColorBrush(palette.Stroke),
            ["TextFillColorPrimaryBrush"] = new SolidColorBrush(palette.Foreground),
            ["TextFillColorSecondaryBrush"] = new SolidColorBrush(palette.SecondaryForeground),
            ["TextFillColorTertiaryBrush"] = new SolidColorBrush(palette.SecondaryForeground),
            ["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(palette.CardSurface),
            ["ControlFillColorSecondaryBrush"] = new SolidColorBrush(palette.AvatarSurface)
        };

        SetNotificationTextBrushes(hasImage ? Colors.White : palette.Foreground,
            hasImage ? Color.FromArgb(0xCC, 255, 255, 255) : palette.SecondaryForeground);
        ApplyCardInk(palette, hasImage);
        UpdateFloatingWindowOpacity();
    }

    private static SolidColorBrush CreateImageOverlayBrush()
        => new(Color.FromArgb(0x99, 0, 0, 0));

    private static ResourceDictionary CreateNotificationImageResources()
    {
        var white = new SolidColorBrush(Colors.White);
        var secondary = new SolidColorBrush(Color.FromArgb(0xCC, 255, 255, 255));
        var darkCard = new SolidColorBrush(Color.FromArgb(0xAA, 0, 0, 0));
        var darkAvatar = new SolidColorBrush(Color.FromArgb(0xBB, 0, 0, 0));
        return new ResourceDictionary
        {
            ["SolidBackgroundFillColorBaseBrush"] = darkCard,
            ["ControlElevationBorderBrush"] = secondary,
            ["TextFillColorPrimaryBrush"] = white,
            ["TextFillColorSecondaryBrush"] = secondary,
            ["TextFillColorTertiaryBrush"] = secondary,
            ["CardBackgroundFillColorDefaultBrush"] = darkCard,
            ["ControlFillColorSecondaryBrush"] = darkAvatar
        };
    }

    private void SetNotificationTextBrushes(Color foreground, Color secondary)
    {
        StatusTextBlock.Foreground = new SolidColorBrush(foreground);
        AutoCloseHintTextBlock.Foreground = new SolidColorBrush(secondary);
        HistoryGroupHintTextBlock.Foreground = new SolidColorBrush(secondary);
        HistoryGroupMissingHintTextBlock.Foreground = new SolidColorBrush(secondary);
    }

    private bool TryLoadNotificationBackgroundImage(bool allowBackgroundImage)
    {
        var fileName = allowBackgroundImage
            ? ViewModel.Config.NotificationSettings.Default.BackgroundImage
            : string.Empty;
        if (_loadedBackgroundImage == fileName
            && (_notificationBackgroundBitmap is not null || string.IsNullOrWhiteSpace(fileName)))
            return _notificationBackgroundBitmap is not null;
        _loadedBackgroundImage = fileName;
        Bitmap? bitmap = null;
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            try
            {
                var path = ResolveNotificationBackgroundPath(fileName);
                if (path is not null && File.Exists(path))
                {
                    using var stream = SharedFileReader.OpenRead(path);
                    bitmap = new Bitmap(stream);
                }
            }
            catch
            {
                bitmap = null;
            }
        }

        var previous = _notificationBackgroundBitmap;
        _notificationBackgroundBitmap = bitmap;
        NotificationBackgroundBorder.Background = bitmap is null ? null
            : new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        NotificationBackgroundBorder.IsVisible = bitmap is not null;
        UpdateFloatingWindowOpacity();
        previous?.Dispose();
        return bitmap is not null;
    }

    private static string? ResolveNotificationBackgroundPath(string fileName)
    {
        var directory = Path.GetFullPath(Path.Combine(Utils.DataRoot, "images", "notification"));
        var path = Path.GetFullPath(Path.Combine(directory, fileName.Trim()));
        return path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? path
            : null;
    }

    /// <summary>
    ///     把"通知卡片现在这层底有多亮"换算成黑/白文字色，并推给结果项。
    /// </summary>
    /// <remarks>
    ///     <paramref name="adaptive" /> 非 null 时直接用它的前景色，两者本来就是同一个判据。
    ///     为 null（自适应关闭、低配回退、采样失败）时卡片回到应用主题底色，此时按应用主题的
    ///     明暗取黑/白——绝不能再让结果项沿用强调色当文字色，那正是浅色桌面上"看不清"的来源。
    /// </remarks>
    private void ApplyCardInk(AdaptiveTheme? adaptive, bool customImage)
    {
        var isDarkTheme = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        var ink = customImage
                  ? Colors.White
                  : adaptive?.Foreground
                  ?? AdaptiveBackdropResolver.FromAppTheme(isDarkTheme).Foreground;
        ViewModel.ApplyNotificationInk(new SolidColorBrush(ink));
    }

    private PointerPressedEventArgs? _quickDrawPressEvent;
    private Point _quickDrawDragStartPoint;
    private bool _quickDrawIsDragging;

    private void RootBorder_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || TopLevel.GetTopLevel(this) is not Window window)
            return;

        _quickDrawIsDragging = false;
        _quickDrawDragStartPoint = e.GetPosition(this);
        _quickDrawPressEvent = e;
    }

    private void RootBorder_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_quickDrawIsDragging)
            return;
        if (_quickDrawPressEvent is null)
            return;
        if (TopLevel.GetTopLevel(this) is not Window window)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var delta = e.GetPosition(this) - _quickDrawDragStartPoint;
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4)
            return;

        _quickDrawIsDragging = true;
        window.BeginMoveDrag(_quickDrawPressEvent);
    }

    private void RootBorder_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;

        if (!_quickDrawIsDragging)
            RegisterClick();

        _quickDrawIsDragging = false;
        _quickDrawPressEvent = null;
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUnloaded)
            return;

        if (e.PropertyName == nameof(QuickDrawPageViewModel.NotificationOpacity))
            UpdateFloatingWindowOpacity();

        if (e.PropertyName == nameof(QuickDrawPageViewModel.NotificationTheme))
            ApplyNotificationTheme(ViewModel.NotificationTheme);

        if (e.PropertyName == nameof(QuickDrawPageViewModel.IsDrawBlockedNoticeVisible)
            && !ViewModel.IsDrawBlockedNoticeVisible
            && !ViewModel.IsResultVisible
            && _notificationWindow is { IsVisible: true })
            SecRandom.App.HideQuickDrawNotificationWindow();

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
                settings.ResolvedCountdownStart,
                settings.ResolvedCountdownEnd);
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
