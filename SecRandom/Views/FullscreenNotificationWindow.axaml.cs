using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Notification;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Helpers;
using SecRandom.Platforms.Abstractions;
using SecRandom.Services.Notification;
using SecRandom.Shared;
using SecRandom.Views.Fullscreen;
using FullscreenResources = SecRandom.Langs.Fullscreen.Resources;

namespace SecRandom.Views;

/// <summary>
///     全屏结果通知窗口：直接继承 <see cref="Window" />，不走 MVE/ViewEngine 宿主。
/// </summary>
/// <remarks>
///     <para>
///         这是一个"纯展示"窗口：所有内容都由 <c>FullscreenNotificationPresenter</c> 通过
///         <see cref="BeginIdle" /> / <see cref="PlayAsync" /> / <see cref="StartCountdown" /> 驱动，
///         窗口不查询候选池、不回读任何 ViewModel。
///     </para>
///     <para>
///         关闭手势只有三种：ESC、500ms 内三连击、倒计时归零；一律只 <see cref="Window.Close" />，
///         绝不在窗口内调用 <c>Shutdown</c>。安全验证弹窗会挂在 OverlayLayer 上，因此弹窗存在时
///         ESC 让位给弹窗，避免把正在验证的 root window 关掉（方案 R1）。
///     </para>
///     <para>
///         整窗不透明度放在根 <c>RootBorder.Opacity</c>（QuickDrawPage 先例），不使用
///         <see cref="Window.Opacity" />；根 Border 的 Opacity 同时承担淡入。
///     </para>
/// </remarks>
public partial class FullscreenNotificationWindow : Window
{
    private const int ClickSequenceWindowMilliseconds = 500;
    private const int IdleRollIntervalMilliseconds = 90;
    private const int CountdownFrameMilliseconds = 16;
    private const int TopmostReassertDelayMilliseconds = 160;

    /// <summary>结果画面上最多显示几个头像；再多就只是一排缩略图，不如留给结果文字。</summary>
    private const int MaxAvatars = 8;

    /// <summary>
    ///     自定义图片背景上固定盖一层压暗，保证文字可读；其余背景模式不压暗。
    ///     遮罩浓度刻意不做成设置项——主题/纯色/渐变本来就是用户选的配色，再压一层只会让它们变脏。
    /// </summary>
    private const double ImageOverlayOpacity = 0.35;

    /// <summary>深色主题下的正文色（近白）。</summary>
    private static readonly Color DarkThemeBodyColor = Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF);

    /// <summary>浅色主题下的正文色（近黑），否则浅底白字整屏不可读。</summary>
    private static readonly Color LightThemeBodyColor = Color.FromArgb(0xF2, 0x1A, 0x1D, 0x22);

    private readonly MainConfigHandler _configHandler;
    private readonly NotificationSettingsType _kind;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly DispatcherTimer _idleTimer;
    private readonly ScaleTransform _topEdgeScale = new(1, 1);
    private readonly ScaleTransform _bottomEdgeScale = new(1, 1);
    private readonly ScaleTransform _leftEdgeScale = new(1, 1);
    private readonly ScaleTransform _rightEdgeScale = new(1, 1);

    private CancellationTokenSource? _lifetimeCts = new();
    private CancellationTokenSource? _playCts;
    private CancellationTokenSource? _countdownCts;
    private FullscreenPresentationSeed? _seed;
    private Bitmap? _backgroundBitmap;
    private Color _accentColor;
    private Color _bodyColor = DarkThemeBodyColor;
    private double _bodyFontSize = 64;

    /// <summary>当前背景是否是浅色（决定正文用深色还是浅色）。由背景模式与主题共同推出，不是"主题是否浅色"。</summary>
    private bool _lightBackground;

    /// <summary>当前背景是不是自定义图片：只有图片需要遮罩与暗角压边，纯色/渐变/主题底不压。</summary>
    private bool _backgroundUsesImage;
    private bool _isClosed;
    private bool _applyingGeometry;
    private int _clickCount;
    private long _firstClickTimestamp;
    private int _voiceCueInvoked;

    /// <summary>
    ///     创建一个全屏结果窗口；窗口内容完全由 <c>FullscreenNotificationPresenter</c> 驱动。
    /// </summary>
    public FullscreenNotificationWindow(MainConfigHandler configHandler, NotificationSettingsType kind)
    {
        _configHandler = configHandler;
        _kind = kind;
        _logger = IAppHost.TryGetService<ILogger<FullscreenNotificationWindow>>()
                  ?? NullLogger<FullscreenNotificationWindow>.Instance;

        InitializeComponent();

        // 透明无边框全屏的窗口属性必须在构造期设置（FloatingWindow 先例）。
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Manual;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        // 降级渲染质量的插值设置（低配机减少整屏位图开销）。
        UiRenderQuality.Apply(this);

        // 强调色默认跟随主题；每次播放前还会按最新设置重新解析一次。
        _accentColor = ThemeAccentColor.Resolve();

        // 窗口级静态文案在构造期取一次：语言切换会走重启/重建窗口，不需要运行时热切换。
        Title = FullscreenResources.Fullscreen_Title;
        CloseHintText.Text = FullscreenResources.Fullscreen_CloseHint;

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IdleRollIntervalMilliseconds) };
        _idleTimer.Tick += OnIdleTimerTick;

        EdgeTop.RenderTransform = _topEdgeScale;
        EdgeTop.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
        EdgeBottom.RenderTransform = _bottomEdgeScale;
        EdgeBottom.RenderTransformOrigin = new RelativePoint(1, 0.5, RelativeUnit.Relative);
        EdgeLeft.RenderTransform = _leftEdgeScale;
        EdgeLeft.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
        EdgeRight.RenderTransform = _rightEdgeScale;
        EdgeRight.RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative);

        Opened += OnWindowOpened;
        ScalingChanged += OnScalingChanged;
        Closed += OnWindowClosed;
        KeyDown += OnWindowKeyDown;
        RootBorder.PointerPressed += OnRootPointerPressed;
    }

    /// <summary>会话是否已经结束（关闭后不再接受任何播放请求）。</summary>
    public bool IsClosed
    {
        get
        {
            lock (_gate)
            {
                return _isClosed;
            }
        }
    }

    /// <summary>
    ///     展示"待揭晓/滚动"的空闲态：候选池来自会话开始时冻结的快照。
    /// </summary>
    public void BeginIdle(FullscreenPresentationSeed seed)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => BeginIdle(seed));
            return;
        }

        if (IsClosed)
            return;

        try
        {
            CancelPlayback();
            CancelCountdown();
            _seed = seed;

            var settings = GetFullscreenSettings();
            _accentColor = ResolveAccent(settings);
            _bodyFontSize = ComputeBodyFontSize(null, settings);

            ResetStage();
            ApplyAppearance(settings);
            UpdateHeader(
                seed.ScopeName,
                BuildKindLabel(seed.Kind),
                BuildRemainingText(seed.Kind, seed.RemainingCount, seed.TotalCount, settings.ShowRemainingCount));

            IdleTitle.Text = FullscreenResources.Fullscreen_IdleTitle;
            IdleHint.Text = FullscreenResources.Fullscreen_IdleHint;
            IdleRollingText.Text = FullscreenResources.Fullscreen_IdleRolling;
            IdlePanel.IsVisible = true;

            if (seed.FrozenPool is { Count: > 0 })
                _idleTimer.Start();
        }
        catch (Exception ex)
        {
            // 空闲态失败也绝不能把窗口弄崩：保持空白背景，等 Complete 再来。
            _logger.LogWarning(ex, "全屏通知窗口：展示待揭晓态失败。");
        }
    }

    /// <summary>
    ///     播放一次结果展示，返回时动画已经落定（取消时保留当前画面并立即返回）。
    /// </summary>
    public async Task PlayAsync(
        FullscreenResultSnapshot snapshot,
        FullscreenAnimationStyle style,
        int durationMs,
        CancellationToken cancellationToken,
        Action? voiceCue = null)
    {
        if (snapshot is null || IsClosed)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => PlayAsync(snapshot, style, durationMs, cancellationToken, voiceCue));
            return;
        }

        CancellationTokenSource cts;
        lock (_gate)
        {
            _playCts?.Cancel();
            _playCts?.Dispose();
            cts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCts is { IsCancellationRequested: false } lifetime ? lifetime.Token : CancellationToken.None);
            _playCts = cts;
        }

        var cue = BindVoiceCue(voiceCue);
        try
        {
            if (cts.IsCancellationRequested)
                return;

            _idleTimer.Stop();
            CancelCountdown();

            var settings = GetFullscreenSettings();
            _accentColor = ResolveAccent(settings);

            // 只有打字机需要模板的"前段/后段"；直接显示与网格都只看结果本身（用户反馈）。
            var lines = FullscreenTextAnimation.BuildLines(snapshot);
            var resultOnlyLines = FullscreenTextAnimation.BuildLines(snapshot, includeTemplate: false);
            var shownLines = style == FullscreenAnimationStyle.Typewriter ? lines : resultOnlyLines;
            _bodyFontSize = ComputeBodyFontSize(shownLines, settings);

            ResetStage();
            ApplyAppearance(settings);
            UpdateHeader(
                snapshot.ScopeName ?? snapshot.CourseName,
                BuildKindLabel(snapshot.Kind),
                BuildRemainingText(snapshot.Kind, snapshot.RemainingCount, snapshot.TotalCount, settings.ShowRemainingCount));

            switch (style)
            {
                case FullscreenAnimationStyle.Grid:
                    if (snapshot.FrozenPool is { Count: > 1 })
                    {
                        var (width, height) = GetStageSize();
                        AnimationHost.IsVisible = true;
                        await FullscreenGridAnimation.PlayAsync(
                            AnimationHost,
                            snapshot.FrozenPool,
                            snapshot.Results,
                            width * 0.9,
                            height * 0.82,
                            _accentColor,
                            _bodyColor,
                            _lightBackground,
                            durationMs,
                            cue,
                            cts.Token);
                        if (cts.IsCancellationRequested)
                            return;

                        // 网格停在中奖卡片上就是最终画面，不再切回结果文案。
                        if (AnimationHost.Children.Count == 0)
                            ShowStatic(resultOnlyLines, snapshot);

                        // 幂等 cue：网格动画正常时已在定格帧播报，这里只兜底提前结束的情况。
                        cue();
                        break;
                    }

                    ShowStatic(resultOnlyLines, snapshot);
                    cue();
                    break;

                case FullscreenAnimationStyle.Typewriter:
                    ResultHost.IsVisible = true;
                    await FullscreenTextAnimation.PlayAsync(
                        ResultHost,
                        lines,
                        _bodyFontSize,
                        _bodyColor,
                        _accentColor,
                        settings.EffectiveTypewriterCharactersPerSecond,
                        cue,
                        cts.Token,
                        BuildAvatarControls(snapshot));
                    break;

                case FullscreenAnimationStyle.Direct:
                default:
                    ShowStatic(resultOnlyLines, snapshot);
                    cue();
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // 取消时保留当前画面。
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知窗口：播放结果动画失败，回落为静态结果。");
            TryShowStaticSafe(snapshot);
            cue();
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_playCts, cts))
                    _playCts = null;
            }

            cts.Dispose();
        }
    }

    /// <summary>取消当前动画与倒计时，但保留窗口和当前画面。</summary>
    public void Cancel()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Cancel);
            return;
        }

        _idleTimer.Stop();
        CancelPlayback();
        CancelCountdown();
        HideCountdownChrome();
    }

    /// <summary>
    ///     动画落定后开始倒计时：右上角读秒 + 四边进度条随剩余时间收缩，归零时先回调再关窗。
    /// </summary>
    /// <param name="seconds">剩余秒数；0 或负数表示不自动关闭。</param>
    /// <param name="onElapsed">归零回调（可以为 null）。</param>
    public void StartCountdown(int seconds, Action? onElapsed = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => StartCountdown(seconds, onElapsed));
            return;
        }

        if (IsClosed)
            return;

        CancelCountdown();
        if (seconds <= 0)
        {
            HideCountdownChrome();
            return;
        }

        var totalMilliseconds = seconds * 1000.0;
        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            _countdownCts = cts;
        }

        CountdownBadge.IsVisible = true;
        SetEdgesVisible(true);
        UpdateCountdownVisual(totalMilliseconds, totalMilliseconds);
        _ = RunCountdownAsync(totalMilliseconds, onElapsed, cts.Token);
    }

    private async Task RunCountdownAsync(double totalMilliseconds, Action? onElapsed, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = totalMilliseconds - stopwatch.Elapsed.TotalMilliseconds;
                if (remaining <= 0)
                    break;

                UpdateCountdownVisual(remaining, totalMilliseconds);
                await Task.Delay(CountdownFrameMilliseconds, cancellationToken);
            }

            UpdateCountdownVisual(0, totalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            onElapsed?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知窗口：倒计时归零回调失败。");
        }

        Dispatcher.UIThread.Post(CloseSafely);
    }

    private void UpdateCountdownVisual(double remainingMilliseconds, double totalMilliseconds)
    {
        var seconds = (int)Math.Ceiling(Math.Max(0, remainingMilliseconds) / 1000.0);
        CountdownText.Text = string.Format(
            CultureInfo.CurrentCulture,
            FullscreenResources.Fullscreen_RemainingCountdown,
            seconds);

        var progress = totalMilliseconds <= 0
            ? 1
            : Math.Clamp(remainingMilliseconds / totalMilliseconds, 0, 1);

        _topEdgeScale.ScaleX = progress;
        _bottomEdgeScale.ScaleX = progress;
        _leftEdgeScale.ScaleY = progress;
        _rightEdgeScale.ScaleY = progress;
    }

    private void OnIdleTimerTick(object? sender, EventArgs e)
    {
        try
        {
            var pool = _seed?.FrozenPool;
            if (pool is null || pool.Count == 0)
            {
                IdleRollingText.Text = FullscreenResources.Fullscreen_IdleRolling;
                return;
            }

            var index = pool.Count == 1 ? 0 : Random.Shared.Next(pool.Count);
            IdleRollingText.Text = pool[index].DisplayText;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知窗口：空闲态滚动失败。");
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        try
        {
            // 部分桌面环境在窗口首次显示时会丢掉构造期设置的置顶标志，显示后再断言一次。
            Topmost = true;
            PositionOnTargetScreen();
            Dispatcher.UIThread.Post(() =>
            {
                PositionOnTargetScreen();
                RootBorder.Opacity = GetWindowOpacity();
                ReassertTopmost();
            }, DispatcherPriority.Render);

            // 再补一次：某些后端要等窗口真正落到目标屏、尺寸生效后置顶才会稳定生效。
            DispatcherTimer.RunOnce(() =>
            {
                try
                {
                    PositionOnTargetScreen();
                    ReassertTopmost();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "全屏通知窗口：延迟断言置顶失败。");
                }
            }, TimeSpan.FromMilliseconds(TopmostReassertDelayMilliseconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知窗口：定位到目标显示器失败。");
        }
    }

    private void OnScalingChanged(object? sender, EventArgs e)
    {
        try
        {
            Dispatcher.UIThread.Post(PositionOnTargetScreen, DispatcherPriority.Render);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知窗口：显示器缩放变化处理失败。");
        }
    }

    /// <summary>按目标显示器铺满整屏（含任务栏区域）。</summary>
    /// <remarks>
    ///     几何始终是 <c>Normal</c> + 显式整屏 bounds（任务栏区域也在内），并额外向平台请求原生置顶。
    ///     不再依赖 <see cref="WindowState.FullScreen" />：真全屏会让窗口进入独占显示状态、尺寸不再受
    ///     这里的几何控制，在部分后端还会闪黑，反而不如"整屏 + 置顶"稳定。
    /// </remarks>
    private void PositionOnTargetScreen()
    {
        if (IsClosed || _applyingGeometry)
            return;

        _applyingGeometry = true;
        try
        {
            // Linux/Wayland 不允许程序化定位，退化为最大化。
            if (!App.SupportsProgrammaticWindowPositioning)
            {
                WindowState = WindowState.Maximized;
                RequestNativeTopmost();
                return;
            }

            var screens = Screens;
            var all = screens?.All;
            if (all is null || all.Count == 0)
                return;

            var identifier = GetMonitorIdentifier();
            Screen? target = null;
            if (!string.IsNullOrWhiteSpace(identifier))
            {
                target = all.FirstOrDefault(screen =>
                    NotificationMonitorIdentifier.Matches(screen.DisplayName, screen.Bounds, identifier));
            }

            target ??= screens?.Primary ?? all[0];
            if (target is null)
                return;

            var bounds = target.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            var scale = GetRenderScale(target);

            WindowState = WindowState.Normal;
            Width = bounds.Width / scale;
            Height = bounds.Height / scale;
            Position = bounds.Position;
            Topmost = true;

            // 双保险：几何只负责"盖住任务栏所在的像素"，置顶则保证任务栏这个置顶窗口不压在画面之上。
            RequestNativeTopmost();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知窗口：计算整屏几何失败。");
        }
        finally
        {
            _applyingGeometry = false;
        }
    }

    /// <summary>
    ///     向平台窗口服务请求原生置顶（Windows 走 <c>HWND_TOPMOST</c> 的 <c>SetWindowPos</c>）。
    /// </summary>
    /// <remarks>
    ///     Avalonia 的 <see cref="Window.Topmost" /> 只写窗口管理器标志；Windows 任务栏自身也是置顶窗口，
    ///     必须再把它提到置顶组最前面才能真的盖住。平台不支持时静默忽略，绝不影响展示。
    /// </remarks>
    private void RequestNativeTopmost()
    {
        try
        {
            if (IsClosed || !IsVisible)
                return;

            var handle = TryGetPlatformHandle();
            if (handle is null || handle.Handle == nint.Zero)
                return;

            var service = IAppHost.TryGetService<IWindowFeatureService>();
            if (service is null)
                return;

            service.Apply(
                new PlatformWindowHandle(handle.Handle, handle.HandleDescriptor),
                new WindowFeatureRequest(WindowFeatures.Topmost, true));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "全屏通知窗口：请求原生置顶失败，继续使用 Avalonia 置顶。");
        }
    }

    /// <summary>
    ///     重新断言一次置顶（置顶标志置反再置回 + 原生置顶 + 激活），并避开安全验证弹窗。
    /// </summary>
    private void ReassertTopmost()
    {
        if (IsClosed || !IsVisible)
            return;

        try
        {
            Topmost = false;
            Topmost = true;
            RequestNativeTopmost();

            // 有安全验证弹窗时不能激活自己：会把正在验证的对话框挤到后面（方案 R1）。
            if (!HasOverlayDialog())
                Activate();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "全屏通知窗口：重新断言置顶失败。");
        }
    }

    private double GetRenderScale(Screen screen)
    {
        try
        {
            var scaling = screen.Scaling;
            if (double.IsFinite(scaling) && scaling > 0)
                return scaling;
        }
        catch
        {
            // 部分后端在窗口尚未落到该屏时读不到缩放，退回窗口缩放。
        }

        try
        {
            var scaling = RenderScaling;
            if (double.IsFinite(scaling) && scaling > 0)
                return scaling;
        }
        catch
        {
            // 同上。
        }

        return 1;
    }

    private string GetMonitorIdentifier()
    {
        try
        {
            var channel = _configHandler.Data.GetOverrideNotificationSettings(
                _kind,
                OverridableNotificationSettingsType.NotificationWindow);
            return channel.EnabledMonitor ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知窗口：读取监控显示器设置失败，回落到主显示器。");
            return string.Empty;
        }
    }

    private FullscreenNotificationSettings GetFullscreenSettings()
    {
        try
        {
            return _configHandler.Data.NotificationSettings.Fullscreen ?? new FullscreenNotificationSettings();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知窗口：读取全屏通知设置失败，使用默认值。");
            return new FullscreenNotificationSettings();
        }
    }

    private double GetWindowOpacity()
    {
        // 全屏结果窗是"整块屏幕"的展板：窗口本身始终不透明，透明度只作用在背景层
        // （见 ApplyAppearance 里的 BackgroundOpacity），否则文字也会一起变淡、投影时看不清。
        return 1.0;
    }

    /// <summary>
    ///     解析本次展示的强调色：跟随主题时直接取主题强调色；自定义时用设置值，空/坏值回落主题色。
    /// </summary>
    private static Color ResolveAccent(FullscreenNotificationSettings settings)
    {
        if (settings.UseThemeAccentColor)
            return ThemeAccentColor.Resolve();

        var configured = settings.AccentColor;
        if (!string.IsNullOrWhiteSpace(configured)
            && Color.TryParse(configured.Trim(), out var accent))
        {
            return accent;
        }

        return ThemeAccentColor.Resolve();
    }

    private void ResetStage()
    {
        _idleTimer.Stop();
        IdlePanel.IsVisible = false;
        AnimationHost.IsVisible = false;
        AnimationHost.Children.Clear();
        ResultHost.IsVisible = false;
        ResultHost.Children.Clear();
    }

    /// <summary>应用背景、遮罩、强调色、文字配色与四边进度条外观。任何资源缺失都静默回落。</summary>
    private void ApplyAppearance(FullscreenNotificationSettings settings)
    {
        var lowSpec = IsLowSpecMode();

        // 文字/遮罩配色跟着「背景」的明暗走，而不是跟着应用主题走：用户选了浅色纯色底时，
        // 哪怕应用是深色主题也必须换成深色文字，否则整屏白字糊在浅底上。
        var appearance = settings.ResolveBackgroundAppearance();
        _backgroundUsesImage = appearance.UsesImage;
        _lightBackground = ResolveLightBackground(appearance);
        _bodyColor = _lightBackground ? LightThemeBodyColor : DarkThemeBodyColor;

        ApplyBackground(settings, appearance, lowSpec);
        ApplyThemePalette();

        // 底色层始终不透明：不透明度只作用于背景图（图片模式卡片里的那条滑杆），
        // 否则把图片调淡会连带把窗口本身弄成半透明、露出桌面。
        FallbackBackground.Opacity = 1.0;

        // 遮罩浓度不再是设置项：主题/纯色/渐变本来就是用户选的配色，再压一层只会让它们变脏；
        // 只有自定义图片才需要一层固定压暗来保证文字可读，所以这里直接按模式推出，不对外开放。
        OverlayBorder.Opacity = appearance.UsesImage ? ImageOverlayOpacity : 0.0;

        var accentBrush = new SolidColorBrush(_accentColor);
        CountdownText.Foreground = accentBrush;
        IdleRollingText.Foreground = accentBrush;

        // 强调色可能随主题或设置变化，每次都重建（只有四个画刷）。
        ApplyEdgeBrushes();
    }

    /// <summary>
    ///     跟随主题的文字与遮罩配色。
    /// </summary>
    /// <remarks>
    ///     浅色主题下必须把正文换成近黑、把黑色遮罩换成白色遮罩，
    ///     否则"浅底白字"整屏不可读——这是跟随主题后最容易翻车的一处。
    /// </remarks>
    private void ApplyThemePalette()
    {
        var light = _lightBackground;
        var primary = new SolidColorBrush(_bodyColor);

        ScopeText.Foreground = CreateTextBrush(light, 0xCC);
        KindText.Foreground = CreateTextBrush(light, 0x99);
        RemainingText.Foreground = CreateTextBrush(light, 0xCC);
        CloseHintText.Foreground = CreateTextBrush(light, 0x66);
        IdleTitle.Foreground = primary;
        IdleHint.Foreground = CreateTextBrush(light, 0xCC);

        CountdownBadge.Background = new SolidColorBrush(light
            ? Color.FromArgb(0x1F, 0x00, 0x00, 0x00)
            : Color.FromArgb(0x66, 0x00, 0x00, 0x00));

        // 遮罩与暗角只服务自定义图片：浅色主题下的白色暗角会在上下边缘糊成两条发白的带子，
        // 主题/纯色/渐变底本来就是用户选的配色，任何压边都会让底色"不纯"，所以直接不显示。
        // 图片模式的压边永远是深色：白字压在亮照片上会糊成一片，深色遮罩才是可读的保证。
        OverlayBorder.Background = new SolidColorBrush(Colors.Black);
        VignetteBorder.Background = CreateVignetteBrush(
            Color.FromArgb(0xCC, 0x00, 0x00, 0x00),
            Color.FromArgb(0xDD, 0x00, 0x00, 0x00));
        VignetteBorder.IsVisible = _backgroundUsesImage;
    }

    private static SolidColorBrush CreateTextBrush(bool light, byte alpha)
        => light
            ? new SolidColorBrush(Color.FromArgb(alpha, 0x1A, 0x1D, 0x22))
            : new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF));

    private static LinearGradientBrush CreateVignetteBrush(Color topEdge, Color bottomEdge)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
        };
        brush.GradientStops.Add(new GradientStop(topEdge, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.28));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.72));
        brush.GradientStops.Add(new GradientStop(bottomEdge, 1));
        return brush;
    }

    private void ApplyBackground(
        FullscreenNotificationSettings settings,
        FullscreenBackgroundAppearance appearance,
        bool lowSpec)
    {
        if (_backgroundBitmap is not null)
        {
            BackgroundImage.Source = null;
            _backgroundBitmap.Dispose();
            _backgroundBitmap = null;
        }

        BackgroundImage.Effect = null;
        BackgroundImage.IsVisible = false;
        BackgroundImage.Opacity = 0;

        // 纯色 / 双色渐变各铺各的；跟随主题（以及图片模式没选到图时）铺主题渐变。
        // 图片模式永远先在底下留一层主题渐变：图丢了、读不出来时整屏仍然是正常的板子，不是一片黑。
        switch (appearance.Mode)
        {
            case FullscreenBackgroundMode.Solid:
                FallbackBackground.Background = new SolidColorBrush(
                    appearance.SolidColor ?? FullscreenNotificationSettings.DefaultBackgroundColor);
                break;

            case FullscreenBackgroundMode.Gradient:
                FallbackBackground.Background = BuildAngleGradient(
                    appearance.GradientStart ?? FullscreenNotificationSettings.DefaultBackgroundGradientStart,
                    appearance.GradientEnd ?? FullscreenNotificationSettings.DefaultBackgroundGradientEnd,
                    appearance.GradientAngle);
                break;

            default:
                ApplyThemeBackground();
                break;
        }

        if (!appearance.UsesImage)
            return;

        var path = ResolveBackgroundPath(settings.BackgroundImage);
        if (path is null)
            return;

        try
        {
            // 背景图缺失是合法配置状态（用户可能刚删掉文件），静默回落到主题渐变。
            if (!File.Exists(path))
                return;

            var bitmap = new Bitmap(path);
            _backgroundBitmap = bitmap;
            BackgroundImage.Source = bitmap;
            BackgroundImage.Opacity = settings.EffectiveBackgroundOpacity / 100.0;
            BackgroundImage.IsVisible = true;

            var radius = settings.EffectiveBackgroundBlurRadius;
            if (!lowSpec && radius > 0)
                BackgroundImage.Effect = new BlurEffect { Radius = radius };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知窗口：加载背景图失败，回落到主题渐变。");
            BackgroundImage.Source = null;
            BackgroundImage.IsVisible = false;
            _backgroundBitmap?.Dispose();
            _backgroundBitmap = null;
        }
    }

    /// <summary>
    ///     背景是不是浅色：纯色看它的亮度、渐变看两端平均亮度、图片模式不可知就按主题走。
    /// </summary>
    /// <remarks>
    ///     这段时间最容易被忽略的一处：用户把纯色底设成浅灰后，若还按"深色主题"画白字，整屏就不可读。
    /// </remarks>
    private bool ResolveLightBackground(FullscreenBackgroundAppearance appearance)
    {
        static double Luminance(Color color) => (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;

        return appearance.Mode switch
        {
            FullscreenBackgroundMode.Solid when appearance.SolidColor is { } solid =>
                Luminance(solid) > 0.55,
            FullscreenBackgroundMode.Gradient when appearance.GradientStart is { } start
                                                    && appearance.GradientEnd is { } end =>
                (Luminance(start) + Luminance(end)) / 2 > 0.55,
            // 主题模式与图片模式：主题按实际铺上去的底色判断；图片内容不可预知，但上层永远压着
            // 一层深色遮罩（见 ApplyThemePalette），所以正文固定用浅色。
            FullscreenBackgroundMode.Image => false,
            _ => Luminance(ResolveThemeSurfaceColor()) > 0.55
        };
    }

    /// <summary>
    ///     按角度拼一个线性渐变：0° 从左往右、顺时针转。
    /// </summary>
    /// <remarks>
    ///     角度换算成相对坐标的两个端点，画出来的方向才跟用户在设置里看到的一致；
    ///     直接用 90° 当"从上到下"，在宽屏上斜角会明显偏。
    /// </remarks>
    private static LinearGradientBrush BuildAngleGradient(Color start, Color end, int angle)
    {
        var radians = angle * Math.PI / 180;
        var dx = Math.Cos(radians) / 2;
        var dy = Math.Sin(radians) / 2;

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5 - dx, 0.5 - dy, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5 + dx, 0.5 + dy, RelativeUnit.Relative)
        };
        brush.GradientStops.Add(new GradientStop(start, 0));
        brush.GradientStops.Add(new GradientStop(end, 1));
        return brush;
    }

    /// <summary>
    ///     跟随主题的底色：直接用应用主题的纯色背景铺满整屏。
    /// </summary>
    /// <remarks>
    ///     这里刻意不再掺强调色、也不再画渐变：用户选「跟随主题」要的是应用本身的底色，
    ///     掺了强调色会发蓝/发紫，渐变会让整块底色看起来"不纯"，投影时尤其明显。
    ///     强调色只留给进度条、倒计时与结果强调，不参与背景。
    /// </remarks>
    private void ApplyThemeBackground()
    {
        FallbackBackground.Background = new SolidColorBrush(ResolveThemeSurfaceColor());
    }

    /// <summary>
    ///     解析应用主题的纯色背景：优先取 FluentAvalonia 的主题底色资源，取不到就按明暗主题回默认值。
    /// </summary>
    /// <remarks>
    ///     必须只接受不透明纯色：主题资源里也有半透明/画刷型条目，透出来会让整屏和桌面混色，同样是"不纯"。
    /// </remarks>
    private Color ResolveThemeSurfaceColor()
    {
        var variant = ActualThemeVariant;
        foreach (var key in ThemeSurfaceColorResourceKeys)
        {
            if (!Application.Current!.TryFindResource(key, variant, out var resource) || resource is null)
                continue;

            switch (resource)
            {
                case Color color when color.A == 0xFF:
                    return color;
                case ISolidColorBrush { Color.A: 0xFF } solid:
                    return solid.Color;
            }
        }

        return variant == ThemeVariant.Light ? DefaultLightThemeSurfaceColor : DefaultDarkThemeSurfaceColor;
    }

    private static readonly object[] ThemeSurfaceColorResourceKeys =
    [
        "SolidBackgroundFillColorBaseBrush",
        "SolidBackgroundFillColorBase",
        "ApplicationBackgroundBrush",
        "SystemControlBackgroundAltHighBrush"
    ];

    private static readonly Color DefaultDarkThemeSurfaceColor = Color.FromRgb(0x20, 0x20, 0x20);
    private static readonly Color DefaultLightThemeSurfaceColor = Color.FromRgb(0xF3, 0xF3, 0xF3);

    private string? ResolveBackgroundPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var trimmed = value.Trim();
            if (Path.IsPathRooted(trimmed))
                return trimmed;

            // 兼容只存文件名的旧配置：设置页负责导入到 data/images/fullscreen。
            return Path.Combine(Utils.DataRoot, "images", "fullscreen", trimmed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知窗口：背景图路径无效。");
            return null;
        }
    }

    /// <summary>四边渐变进度条：强调色从一端渐隐到另一端。</summary>
    private void ApplyEdgeBrushes()
    {
        var accent = _accentColor;
        var soft = Color.FromArgb(0x26, accent.R, accent.G, accent.B);

        EdgeTop.Background = CreateEdgeBrush(accent, soft, horizontal: true);
        EdgeBottom.Background = CreateEdgeBrush(soft, accent, horizontal: true);
        EdgeLeft.Background = CreateEdgeBrush(accent, soft, horizontal: false);
        EdgeRight.Background = CreateEdgeBrush(soft, accent, horizontal: false);
    }

    private static LinearGradientBrush CreateEdgeBrush(Color start, Color end, bool horizontal)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = horizontal
                ? new RelativePoint(0, 0, RelativeUnit.Relative)
                : new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = horizontal
                ? new RelativePoint(1, 0, RelativeUnit.Relative)
                : new RelativePoint(0, 1, RelativeUnit.Relative)
        };
        brush.GradientStops.Add(new GradientStop(start, 0));
        brush.GradientStops.Add(new GradientStop(end, 1));
        return brush;
    }

    private void SetEdgesVisible(bool visible)
    {
        EdgeTop.IsVisible = visible;
        EdgeBottom.IsVisible = visible;
        EdgeLeft.IsVisible = visible;
        EdgeRight.IsVisible = visible;

        if (visible)
        {
            _topEdgeScale.ScaleX = 1;
            _bottomEdgeScale.ScaleX = 1;
            _leftEdgeScale.ScaleY = 1;
            _rightEdgeScale.ScaleY = 1;
        }
    }

    private void HideCountdownChrome()
    {
        CountdownBadge.IsVisible = false;
        SetEdgesVisible(false);
    }

    private void UpdateHeader(string? scope, string kindLabel, string remainingText)
    {
        ScopeText.Text = string.IsNullOrWhiteSpace(scope) ? string.Empty : scope;
        ScopeText.IsVisible = !string.IsNullOrWhiteSpace(scope);
        KindText.Text = kindLabel;
        RemainingText.Text = remainingText;
    }

    /// <summary>
    /// 左上角只写抽取类型（点名结果 / 闪抽结果 / 抽奖结果）。
    /// 总条数与抽中时刻都不在这里重复：左下角的"剩余 x · 共 y"已经有总数，多写一遍只会互相打架。
    /// </summary>
    private static string BuildKindLabel(NotificationSettingsType kind)
    {
        return kind switch
        {
            NotificationSettingsType.RollCall => FullscreenResources.Fullscreen_KindRollCall,
            NotificationSettingsType.Lottery => FullscreenResources.Fullscreen_KindLottery,
            _ => FullscreenResources.Fullscreen_KindQuickDraw
        };
    }

    private static string BuildRemainingText(
        NotificationSettingsType kind,
        int remainingCount,
        int totalCount,
        bool showRemainingCount)
    {
        if (!showRemainingCount)
            return string.Empty;

        var remaining = Math.Max(0, remainingCount);
        if (kind == NotificationSettingsType.Lottery)
        {
            return totalCount > 0
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    FullscreenResources.Fullscreen_RemainingPrizesWithTotal,
                    remaining,
                    totalCount)
                : string.Format(
                    CultureInfo.CurrentCulture,
                    FullscreenResources.Fullscreen_RemainingPrizes,
                    remaining);
        }

        return totalCount > 0
            ? string.Format(
                CultureInfo.CurrentCulture,
                FullscreenResources.Fullscreen_RemainingPeopleWithTotal,
                remaining,
                totalCount)
            : string.Format(
                CultureInfo.CurrentCulture,
                FullscreenResources.Fullscreen_RemainingPeople,
                remaining);
    }

    private void ShowStatic(
        IReadOnlyList<FullscreenResultLine> lines,
        FullscreenResultSnapshot? snapshot = null)
    {
        ResultHost.IsVisible = true;
        FullscreenTextAnimation.FillStatic(
            ResultHost,
            lines,
            _bodyFontSize,
            _bodyColor,
            _accentColor,
            snapshot is null ? null : BuildAvatarControls(snapshot));
    }

    /// <summary>
    ///     每条结果的头像控件（用户 m03502：头像在名称左侧；m03587：整段文字连续排布，头像内联在名字前面）。
    ///     配了图片的成员显示照片，没配图片的成员显示姓名首字的圆形占位；未勾选头像时返回 null。
    /// </summary>
    private IReadOnlyList<Control?>? BuildAvatarControls(FullscreenResultSnapshot snapshot)
    {
        if (!snapshot.ShowResultAvatar || snapshot.Results.Count == 0)
            return null;

        var size = Math.Clamp(_bodyFontSize * 1.15, 48, 160);
        var controls = new List<Control?>(Math.Min(snapshot.Results.Count, MaxAvatars));
        for (var index = 0; index < snapshot.Results.Count && index < MaxAvatars; index++)
            controls.Add(CreateAvatar(snapshot.Results[index], size));

        return controls;
    }

    /// <summary>单条结果的圆形头像：有照片用照片，没照片用姓名首字。</summary>
    private Border CreateAvatar(FullscreenResultItem item, double size)
    {
        Bitmap? bitmap = null;
        if (!string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath))
        {
            try
            {
                bitmap = new Bitmap(item.ImagePath);
            }
            catch (Exception)
            {
                bitmap = null;
            }
        }

        var border = new Border
        {
            Width = size,
            Height = size,
            Margin = new Thickness(0, 0, size * 0.3, 0),
            CornerRadius = new CornerRadius(size / 2),
            BorderThickness = new Thickness(3),
            BorderBrush = new SolidColorBrush(_accentColor),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (bitmap is not null)
        {
            border.Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
            return border;
        }

        border.Background = new SolidColorBrush(Color.FromArgb(
            _lightBackground ? (byte)0x1A : (byte)0x33,
            _accentColor.R,
            _accentColor.G,
            _accentColor.B));
        border.Child = new TextBlock
        {
            Text = TakeInitial(item.DisplayText),
            FontSize = size * 0.42,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(_accentColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        return border;
    }

    /// <summary>取展示文本的首字（"01  张三" 这类带序号的文本要跳过序号）。</summary>
    private static string TakeInitial(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "?";

        var value = text;
        var separator = value.LastIndexOf("  ", StringComparison.Ordinal);
        if (separator >= 0 && separator + 2 < value.Length)
            value = value[(separator + 2)..];

        var enumerator = StringInfo.GetTextElementEnumerator(value.Trim());
        return enumerator.MoveNext() ? enumerator.GetTextElement() : "?";
    }

    /// <summary>播放异常时的兜底渲染：只显示结果本身，绝不抛异常。</summary>
    private void TryShowStaticSafe(FullscreenResultSnapshot snapshot)
    {
        try
        {
            AnimationHost.IsVisible = false;
            AnimationHost.Children.Clear();
            ShowStatic(FullscreenTextAnimation.BuildLines(snapshot, includeTemplate: false), snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知窗口：静态结果兜底渲染失败。");
        }
    }

    /// <summary>
    ///     结果正文的字号：设置里的"字体大小"是上限，多结果/长文本按可用空间向下收缩，
    ///     所以调大只会让画面更醒目，永远不会把结果撑出屏幕。
    /// </summary>
    private double ComputeBodyFontSize(
        IReadOnlyList<FullscreenResultLine>? lines,
        FullscreenNotificationSettings? settings = null)
    {
        var (width, height) = GetStageSize();
        var configured = settings?.EffectiveResultFontSize ?? FullscreenNotificationSettings.DefaultResultFontSize;
        if (lines is null || lines.Count == 0)
            return Math.Clamp(Math.Min(configured, height * 0.055), 28, FullscreenNotificationSettings.MaxResultFontSize);

        var maxUnits = lines.Max(line =>
            (line.Prefix?.Length ?? 0) + (line.Result?.Length ?? 0) + (line.Suffix?.Length ?? 0) + 2);
        var byWidth = width / (Math.Max(1, maxUnits) * 0.62);
        var byHeight = height / (Math.Max(1, lines.Count) * 2.4);
        var auto = Math.Clamp(Math.Min(byWidth, byHeight), 28, FullscreenNotificationSettings.MaxResultFontSize);
        return Math.Clamp(Math.Min(configured, auto), 28, FullscreenNotificationSettings.MaxResultFontSize);
    }

    private (double Width, double Height) GetStageSize()
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (!double.IsFinite(width) || width <= 1)
            width = double.IsFinite(Width) && Width > 1 ? Width : 1920;
        if (!double.IsFinite(height) || height <= 1)
            height = double.IsFinite(Height) && Height > 1 ? Height : 1080;
        return (width, height);
    }

    private static bool IsLowSpecMode()
    {
        try
        {
            return IAppHost.TryGetService<MainConfigHandler>()?.Data.General.PerformanceSettings.LowSpecMode == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>把"说一次语音"包成幂等回调：动画器和窗口都可以触发，但只真的说一次。</summary>
    private Action BindVoiceCue(Action? voiceCue)
    {
        Interlocked.Exchange(ref _voiceCueInvoked, 0);
        return () =>
        {
            if (Interlocked.Exchange(ref _voiceCueInvoked, 1) != 0)
                return;

            try
            {
                voiceCue?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "全屏通知窗口：语音播报回调失败。");
            }
        };
    }

    private void CancelPlayback()
    {
        lock (_gate)
        {
            _playCts?.Cancel();
            _playCts?.Dispose();
            _playCts = null;
        }
    }

    private void CancelCountdown()
    {
        lock (_gate)
        {
            _countdownCts?.Cancel();
            _countdownCts?.Dispose();
            _countdownCts = null;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        // 安全验证弹窗挂在 OverlayLayer 上时，ESC 属于弹窗，不能关掉它的 root window。
        if (HasOverlayDialog())
            return;

        e.Handled = true;
        CloseSafely();
    }

    private bool HasOverlayDialog()
    {
        try
        {
            var overlay = OverlayLayer.GetOverlayLayer(this);
            return overlay is not null && overlay.Children.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsRightButtonPressed)
                return;

            RegisterClick();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知窗口：处理关闭手势失败。");
        }
    }

    /// <summary>500ms 内三连击关闭（QuickDrawPage 先例）。</summary>
    private void RegisterClick()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedMilliseconds = _clickCount == 0
            ? double.MaxValue
            : (now - _firstClickTimestamp) * 1000.0 / Stopwatch.Frequency;

        if (_clickCount == 0 || elapsedMilliseconds > ClickSequenceWindowMilliseconds)
        {
            _clickCount = 1;
            _firstClickTimestamp = now;
            return;
        }

        _clickCount++;
        if (_clickCount < 3)
            return;

        _clickCount = 0;
        CloseSafely();
    }

    private void CloseSafely()
    {
        try
        {
            if (IsClosed)
                return;

            Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知窗口：关闭失败。");
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            _isClosed = true;
            _lifetimeCts?.Cancel();
        }

        try
        {
            _idleTimer.Stop();
            _idleTimer.Tick -= OnIdleTimerTick;
            CancelPlayback();
            CancelCountdown();

            _backgroundBitmap?.Dispose();
            _backgroundBitmap = null;
            BackgroundImage.Source = null;

            _lifetimeCts?.Dispose();
            _lifetimeCts = null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知窗口：清理资源失败。");
        }
    }
}
