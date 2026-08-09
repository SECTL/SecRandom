using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Views;
using SecRandom.Services.CrashRecovery;
using SecRandom.Services.Feedback;
using CrashResources = SecRandom.Langs.CrashRecovery.Resources;

namespace SecRandom.Views;

/// <summary>
/// Cross-platform crash-report content. Desktop wraps it in a Window during pre-host startup;
/// an MVE host can present the same view once the application Host is available.
/// </summary>
public sealed partial class CrashRecoveryView : ViewBase, INotifyPropertyChanged
{
    private const int AutoRestartSeconds = 10;
    private readonly CrashRecoveryPromptOptions _options;
    private readonly Func<bool> _restartApp;
    private DispatcherTimer? _restartTimer;
    private int _remainingSeconds;
    private bool _isCrashFeedbackDraftLoaded;
    private bool _isFeedbackDrawerOpen;
    private bool _restartPausedByFeedback;
    private FeedbackDrawer? _feedbackDrawer;
    private string _feedbackStatus = string.Empty;

    public CrashRecoveryView(
        CrashRecoveryPromptOptions options,
        Func<bool> restartApp,
        bool canIgnore = true)
    {
        _options = options;
        _restartApp = restartApp;
        CanIgnore = canIgnore;
        CrashReport = CrashRecoveryRuntime.ReadCrashReport(_options.CrashReportPath);
        DataContext = this;
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => _restartTimer?.Stop();

        if (_options.AutoRestart)
            StartAutoRestartCountdown();
    }

    public CrashRecoveryView(CrashRecoveryViewState state)
        : this(state.Options, state.RestartApp, state.CanIgnore)
    {
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public string CrashReport { get; }
    public bool CanIgnore { get; }
    public bool CanSubmitInAppFeedback => IAppHost.TryGetService<IUserFeedbackService>() is not null;
    public FeedbackDrawer? FeedbackDrawer
    {
        get => _feedbackDrawer;
        private set
        {
            if (_feedbackDrawer == value)
                return;

            _feedbackDrawer = value;
            OnPropertyChanged();
        }
    }

    public bool IsFeedbackDrawerOpen
    {
        get => _isFeedbackDrawerOpen;
        set
        {
            if (_isFeedbackDrawerOpen == value)
                return;

            _isFeedbackDrawerOpen = value;
            OnPropertyChanged();

            // 反馈抽屉打开时暂停自动重启倒计时，关闭后从剩余秒数继续。
            if (value)
            {
                if (_restartTimer?.IsEnabled == true)
                {
                    _restartTimer.Stop();
                    _restartPausedByFeedback = true;
                }
            }
            else if (_restartPausedByFeedback)
            {
                _restartPausedByFeedback = false;
                _restartTimer?.Start();
            }
        }
    }

    public bool WasIgnored { get; private set; }
    public event EventHandler? Dismissed;

    public string FeedbackStatus
    {
        get => _feedbackStatus;
        private set
        {
            if (_feedbackStatus == value)
                return;

            _feedbackStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasFeedbackStatus));
        }
    }

    public bool HasFeedbackStatus => !string.IsNullOrWhiteSpace(FeedbackStatus);

    public string RestartButtonText => _remainingSeconds > 0
        ? string.Format(CrashResources.C_RestartCountdown, _remainingSeconds)
        : CrashResources.C_Restart;

    private async void CopyReport_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(CrashReport) ?? Task.CompletedTask)
                .ConfigureAwait(true);
            if (sender is Button button)
                button.Content = CrashResources.C_CopyDone;
        }
        catch
        {
            if (sender is Button button)
                button.Content = CrashResources.C_CopyFailed;
        }
    }

    private void ReportInApp_OnClick(object? sender, RoutedEventArgs e)
    {
        FeedbackStatus = string.Empty;
        FeedbackDrawer? drawer = FeedbackDrawer ?? IAppHost.TryGetService<FeedbackDrawer>();
        if (drawer is null)
        {
            FeedbackStatus = CrashResources.M_ReportFailed;
            return;
        }

        if (FeedbackDrawer is null)
        {
            FeedbackDrawer = drawer;
            drawer.Configure(CloseFeedbackDrawer);
            drawer.DraftCleared += (_, _) => _isCrashFeedbackDraftLoaded = false;
            drawer.SubmissionSucceeded += (_, _) => FeedbackStatus = CrashResources.M_ReportSuccess;
        }

        if (!_isCrashFeedbackDraftLoaded)
        {
            drawer.LoadDraft(new UserFeedbackSubmission(
                UserFeedbackCategory.Bug,
                CrashResources.FeedbackTitle,
                CrashResources.FeedbackExpected,
                CrashResources.FeedbackActual,
                CrashResources.FeedbackReproduction,
                CrashReport: CrashReport));
            _isCrashFeedbackDraftLoaded = true;
        }

        IsFeedbackDrawerOpen = true;
    }

    private void CloseFeedbackDrawer()
    {
        IsFeedbackDrawerOpen = false;
    }

    private void ReportGitHub_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            CrashRecoveryRuntime.OpenFeedbackIssue(CrashReport);
        }
        catch
        {
            FeedbackStatus = CrashResources.C_ReportFailed;
        }
    }

    private void Restart_OnClick(object? sender, RoutedEventArgs e) => Restart();

    private async void Ignore_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanIgnore)
            return;

        _restartTimer?.Stop();
        await Task.Delay(100).ConfigureAwait(true);
        WasIgnored = true;
        await CloseAsync(CrashRecoveryResult.Ignored, ViewCloseReason.User).ConfigureAwait(true);
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private async void Close_OnClick(object? sender, RoutedEventArgs e)
    {
        _restartTimer?.Stop();
        await CloseAsync(CrashRecoveryResult.Closed, ViewCloseReason.User).ConfigureAwait(true);
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void StartAutoRestartCountdown()
    {
        _remainingSeconds = AutoRestartSeconds;
        OnPropertyChanged(nameof(RestartButtonText));
        _restartTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _restartTimer.Tick += RestartTimer_OnTick;
        _restartTimer.Start();
    }

    private void RestartTimer_OnTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;
        OnPropertyChanged(nameof(RestartButtonText));
        if (_remainingSeconds <= 0)
            Restart();
    }

    private void Restart()
    {
        _restartTimer?.Stop();
        if (_options.AutoRestart && !CrashRecoveryRuntime.TryBeginAutomaticRestart())
            return;

        if (_restartApp())
        {
            _ = CloseAsync(CrashRecoveryResult.RestartRequested, ViewCloseReason.Programmatic);
            Dismissed?.Invoke(this, EventArgs.Empty);
        }
        else
            OnPropertyChanged(nameof(RestartButtonText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

public enum CrashRecoveryResult
{
    Closed,
    Ignored,
    RestartRequested
}
