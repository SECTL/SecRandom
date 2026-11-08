using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Behaviors;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Notification;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Voice;
using SecRandom.Views;

namespace SecRandom.Services.Notification;

/// <summary>
///     全屏结果通知的会话与生命周期管理：开窗、播动画、倒计时、语音、关闭。
/// </summary>
/// <remarks>
///     <para>
///         调用方（<see cref="NotificationService" />）可以在任意线程调用 <see cref="Begin" /> /
///         <see cref="Complete" /> / <see cref="Cancel" />，本类内部切到 UI 线程；开窗失败只会记日志
///         并静默返回，绝不影响抽取流程。
///     </para>
///     <para>
///         窗口单实例：每次 <see cref="Begin" /> 先取消旧会话，再打开新窗口。窗口只在用户关闭、三连击、
///         ESC 或倒计时归零时关闭，<see cref="Complete" /> 播完动画后不会主动关窗。
///     </para>
///     <para>
///         动画时长按通道压缩：闪抽（QuickDraw）要尽快回到抽取，所以三种动画都更短；点名与抽奖用完整
///         2500ms。<see cref="IntroAnimationPolicy.IsEnabled" /> 关闭或低配模式下强制 Direct，且该判定在开窗前
///         完成，避免出现"已开窗才决定不做动画"的空窗。
///     </para>
/// </remarks>
public sealed class FullscreenNotificationPresenter
{
    private const int FullDurationMilliseconds = 2500;
    private const int QuickTypewriterMilliseconds = 600;
    private const int QuickGridMilliseconds = 1200;

    private readonly MainConfigHandler _configHandler;
    private readonly ILogger<FullscreenNotificationPresenter> _logger;
    private readonly object _gate = new();

    private FullscreenNotificationWindow? _window;
    private CancellationTokenSource? _sessionCts;
    private NotificationSettingsType _kind;
    private volatile bool _hasActiveSession;

    /// <summary>
    ///     用户主动关掉了待揭晓窗口：此时后续的 <see cref="Complete" /> 不再补开窗口，
    ///     否则教师关掉遮罩后会在抽取结束时被再弹一次。
    /// </summary>
    private bool _sessionAbandoned;

    public FullscreenNotificationPresenter(
        MainConfigHandler config,
        ILogger<FullscreenNotificationPresenter> logger)
    {
        _configHandler = config;
        _logger = logger;
    }

    /// <summary>当前是否有正在展示的全屏结果会话。</summary>
    public bool HasActiveSession => _hasActiveSession;

    /// <summary>
    ///     开启一次全屏展示会话：立即开窗并显示"待揭晓/滚动"的空闲态。可在任意线程调用。
    /// </summary>
    public void Begin(NotificationSettingsType kind, FullscreenPresentationSeed seed)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Begin(kind, seed));
            return;
        }

        BeginCore(kind, seed);
    }

    /// <summary>
    ///     用提交后的快照渲染结果并播放动画；动画落定后开始自动关闭倒计时。可在任意线程调用。
    /// </summary>
    /// <param name="kind">抽取类型。</param>
    /// <param name="snapshot">提交后的结果快照。</param>
    /// <param name="autoCloseSecondsOverride">
    ///     仅供预览使用：覆盖倒计时秒数。正式链路传 <c>null</c>，倒计时取自通知渠道的"通知显示时长"，
    ///     全屏渠道不再拥有自己的时长设置。
    /// </param>
    public void Complete(
        NotificationSettingsType kind,
        FullscreenResultSnapshot snapshot,
        int? autoCloseSecondsOverride = null)
    {
        if (snapshot is null)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Complete(kind, snapshot, autoCloseSecondsOverride));
            return;
        }

        try
        {
            if (_window is not null && _kind != kind)
            {
                _logger.LogDebug("全屏通知：忽略 {Kind} 的结果，当前会话属于 {ActiveKind}。", kind, _kind);
                return;
            }

            if (_window is null && _sessionAbandoned)
            {
                _logger.LogDebug("全屏通知：用户已关闭 {Kind} 的待揭晓窗口，不再补开。", kind);
                return;
            }

            var cts = _sessionCts ??= new CancellationTokenSource();
            if (cts.IsCancellationRequested)
                return;

            _ = CompleteCoreAsync(kind, snapshot, autoCloseSecondsOverride, cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知：提交结果失败。");
        }
    }

    /// <summary>取消当前会话并关窗；没有会话时是 no-op。可在任意线程调用。</summary>
    public void Cancel(NotificationSettingsType kind)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Cancel(kind));
            return;
        }

        if (_window is null || _kind != kind)
            return;

        try
        {
            ResetSession(closeWindow: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知：取消会话失败。");
        }
    }

    private void BeginCore(NotificationSettingsType kind, FullscreenPresentationSeed seed)
    {
        try
        {
            ResetSession(closeWindow: true);
            _sessionAbandoned = false;

            var window = CreateWindow(kind);
            _window = window;
            _kind = kind;
            _sessionCts = new CancellationTokenSource();

            window.BeginIdle(seed);
            window.Show();
            _hasActiveSession = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知：打开全屏结果窗口失败。");
            ResetSession(closeWindow: true);
        }
    }

    private async Task CompleteCoreAsync(
        NotificationSettingsType kind,
        FullscreenResultSnapshot snapshot,
        int? autoCloseSecondsOverride,
        CancellationToken cancellationToken)
    {
        try
        {
            var window = _window;
            if (window is null || window.IsClosed)
            {
                // 用户已经关掉待揭晓窗口就不要再弹一次；只有 Begin 缺席时才补开。
                if (_sessionAbandoned)
                    return;

                window = TryOpenWindowForSnapshot(kind, snapshot);
            }

            if (window is null || window.IsClosed)
                return;

            var settings = GetFullscreenSettings();
            var style = ResolveStyle(settings);
            var duration = ResolveDuration(style, kind);
            var voiceCue = BuildVoiceCue(settings, snapshot);

            await window.PlayAsync(snapshot, style, duration, cancellationToken, voiceCue);

            if (cancellationToken.IsCancellationRequested || window.IsClosed)
                return;

            // 动画落定之后才开始倒计时。秒数不再来自全屏自己的设置，而是通知渠道那份
            // "通知显示时长"（与浮窗、ClassIsland 共用同一个值），0 = 不自动关闭。
            window.StartCountdown(autoCloseSecondsOverride ?? ResolveChannelDisplaySeconds(kind));
        }
        catch (OperationCanceledException)
        {
            // 会话被取消（用户关闭/ESC/新一次抽取）：画面保持现状。
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知：播放结果失败。");
        }
    }

    /// <summary>Begin 缺席（例如调用方直接 Complete）时的兜底开窗；失败返回 null。</summary>
    private FullscreenNotificationWindow? TryOpenWindowForSnapshot(
        NotificationSettingsType kind,
        FullscreenResultSnapshot snapshot)
    {
        try
        {
            var window = CreateWindow(kind);
            window.BeginIdle(new FullscreenPresentationSeed(
                kind,
                snapshot.FrozenPool ?? Array.Empty<FullscreenResultItem>(),
                snapshot.TotalCount,
                snapshot.RemainingCount,
                snapshot.ScopeName));

            _window = window;
            _kind = kind;
            _sessionCts ??= new CancellationTokenSource();

            window.Show();
            _hasActiveSession = true;
            return window;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知：补开全屏结果窗口失败。");
            return null;
        }
    }

    private FullscreenNotificationWindow CreateWindow(NotificationSettingsType kind)
    {
        var window = new FullscreenNotificationWindow(_configHandler, kind);
        window.Closed += OnWindowClosed;
        return window;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is FullscreenNotificationWindow window)
            window.Closed -= OnWindowClosed;

        // Closed 由窗口自身触发；若关闭发生在 ResetSession 内部，_window 已被置空，这里自然 no-op。
        if (ReferenceEquals(_window, sender))
        {
            _sessionAbandoned = true;
            ClearSessionState();
        }
    }

    private void ClearSessionState()
    {
        _window = null;
        _hasActiveSession = false;

        var cts = _sessionCts;
        _sessionCts = null;
        if (cts is null)
            return;

        try
        {
            cts.Cancel();
        }
        catch
        {
            // 取消令牌本身失败无需上报。
        }

        cts.Dispose();
    }

    /// <summary>取消令牌、关掉窗口并清空会话状态。</summary>
    private void ResetSession(bool closeWindow)
    {
        var window = _window;
        _window = null;
        _hasActiveSession = false;

        var cts = _sessionCts;
        _sessionCts = null;
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
                // 忽略取消令牌异常。
            }

            cts.Dispose();
        }

        if (window is null || !closeWindow)
            return;

        try
        {
            window.Cancel();
            if (!window.IsClosed)
                window.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知：关闭全屏结果窗口失败。");
        }
    }

    private FullscreenNotificationSettings GetFullscreenSettings()
    {
        try
        {
            return _configHandler.Data.NotificationSettings.Fullscreen
                   ?? new FullscreenNotificationSettings();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知：读取全屏通知设置失败，使用默认值。");
            return new FullscreenNotificationSettings();
        }
    }

    /// <summary>
    ///     倒计时秒数取自通知渠道自己的"通知显示时长"（<see cref="NotificationChannelSettings.DisplayDuration" />），
    ///     和浮窗、ClassIsland 用的是同一个值；全屏渠道刻意不再拥有独立时长设置，避免两处时长互相打架。
    /// </summary>
    private int ResolveChannelDisplaySeconds(NotificationSettingsType kind)
    {
        try
        {
            var channel = _configHandler.Data.GetOverrideNotificationSettings(
                kind,
                OverridableNotificationSettingsType.Service);
            return Math.Clamp(channel.DisplayDuration, 0, 60);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "全屏通知：读取通知显示时长失败，回落 3 秒。");
            return 3;
        }
    }

    /// <summary>低配模式或全局关闭入场动画时强制 Direct（必须在开窗/播放前判定）。</summary>
    private FullscreenAnimationStyle ResolveStyle(FullscreenNotificationSettings settings)
    {
        if (!IntroAnimationPolicy.IsEnabled || IsLowSpecMode())
            return FullscreenAnimationStyle.Direct;

        return settings.AnimationStyle;
    }

    private static int ResolveDuration(FullscreenAnimationStyle style, NotificationSettingsType kind)
    {
        var quick = kind == NotificationSettingsType.QuickDraw;
        return style switch
        {
            FullscreenAnimationStyle.Direct => 0,
            FullscreenAnimationStyle.Typewriter => quick ? QuickTypewriterMilliseconds : FullDurationMilliseconds,
            FullscreenAnimationStyle.Grid => quick ? QuickGridMilliseconds : FullDurationMilliseconds,
            _ => 0
        };
    }

    /// <summary>
    ///     语音播报回调：交给窗口与动画器在各自的时间点触发，窗口侧保证只真的调用一次。
    ///     <see cref="VoiceAnnouncementService" /> 惰性解析，移动端 DI 缺失时静默跳过。
    /// </summary>
    private Action? BuildVoiceCue(FullscreenNotificationSettings settings, FullscreenResultSnapshot snapshot)
    {
        if (!settings.ShowVoiceAnnouncement)
            return null;

        var text = snapshot.VoiceText;
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return () =>
        {
            try
            {
                var service = IAppHost.TryGetService<VoiceAnnouncementService>();
                if (service is null)
                    return;

                _ = service.SpeakAsync(text, waitForCompletion: false, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "全屏通知：语音播报失败。");
            }
        };
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
}
