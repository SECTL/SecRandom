using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SecRandom.Services.CrashRecovery;
using CrashResources = SecRandom.Langs.CrashRecovery.Resources;

namespace SecRandom.Views;

public partial class CrashRecoveryWindow : Window, INotifyPropertyChanged
{
    private const int AutoRestartSeconds = 10;
    private readonly CrashRecoveryPromptOptions _options;
    private readonly Func<bool> _restartApp;
    private DispatcherTimer? _restartTimer;
    private int _remainingSeconds;

    public CrashRecoveryWindow()
        : this(new CrashRecoveryPromptOptions(string.Empty, false), static () => false)
    {
    }

    public CrashRecoveryWindow(CrashRecoveryPromptOptions options, Func<bool> restartApp)
    {
        _options = options;
        _restartApp = restartApp;
        CrashReport = CrashRecoveryRuntime.ReadCrashReport(options.CrashReportPath);
        DataContext = this;
        InitializeComponent();

        if (options.AutoRestart)
            StartAutoRestartCountdown();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public string CrashReport { get; }

    public string RestartButtonText => _remainingSeconds > 0
        ? string.Format(CrashResources.C_RestartCountdown, _remainingSeconds)
        : CrashResources.C_Restart;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _restartTimer?.Stop();
        base.OnClosed(e);
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

    private async void CopyReport_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(CrashReport) ?? System.Threading.Tasks.Task.CompletedTask)
                .ConfigureAwait(true);
            if (sender is Button button)
                button.Content = CrashResources.C_CopyDone;
        }
        catch (Exception)
        {
            if (sender is Button button)
                button.Content = CrashResources.C_CopyFailed;
        }
    }

    private void ReportIssue_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            CrashRecoveryRuntime.OpenFeedbackIssue(CrashReport);
        }
        catch (Exception)
        {
            if (sender is Button button)
                button.Content = CrashResources.C_ReportFailed;
        }
    }

    private void Restart_OnClick(object? sender, RoutedEventArgs e)
    {
        Restart();
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Restart()
    {
        _restartTimer?.Stop();

        if (_options.AutoRestart && !CrashRecoveryRuntime.TryBeginAutomaticRestart())
            return;

        if (!_restartApp())
            OnPropertyChanged(nameof(RestartButtonText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
