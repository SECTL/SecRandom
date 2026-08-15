using System.ComponentModel;
using System.Diagnostics;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Icons;
using R = SecRandom.Langs.MainPages.Timer.Resources;

namespace SecRandom.ViewModels.MainPages;

public sealed partial class TimerViewModel : ObservableObject, IDisposable
{
    private readonly MainConfigHandler _configHandler;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly EventHandler _refreshHandler;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimerMode _mode;
    private bool _isRunning;
    private TimeSpan _remaining = TimeSpan.FromMinutes(5);
    private TimeSpan _elapsed;
    private long _startedAt;
    private long _deadline;
    private bool _finished;
    private double _totalSeconds = TimeSpan.FromMinutes(5).TotalSeconds;
    private bool _disposed;
    private TimeSpan _lastLapTime;
    private int _presetCategoryIndex;
    private bool _showStopwatchMilliseconds;

    public TimerViewModel(MainConfigHandler configHandler)
    {
        _configHandler = configHandler;
        LoadRecentPresets();
        _refreshHandler = (_, _) => Refresh();
        _refreshTimer.Tick += _refreshHandler;
        _refreshTimer.Start();
        Refresh();
    }

    public bool IsCountdownMode => _mode == TimerMode.Countdown;
    public bool IsStopwatchMode => _mode == TimerMode.Stopwatch;
    public bool IsClockMode => _mode == TimerMode.Clock;
    public bool HasControls => !IsClockMode;
    public bool IsRunning => _isRunning;
    public string PageTitle => R.Page_Title;
    public string CountdownModeText => R.C_Countdown;
    public string StopwatchModeText => R.C_Stopwatch;
    public string ClockModeText => R.C_Clock;
    public string ModeText => _mode switch
    {
        TimerMode.Countdown => R.C_Countdown,
        TimerMode.Stopwatch => R.C_Stopwatch,
        _ => R.C_Clock
    };
    public string StartPauseText => _isRunning ? R.C_Pause : R.C_Start;
    public string ResetText => R.C_Reset;
    public string PresetsTitle => R.C_Presets;
    public string CommonPresetsText => R.C_CommonPresets;
    public string RecentPresetsText => R.C_RecentPresets;
    public string StopwatchHint => R.C_Stopwatch;
    public string StopwatchDetails => R.M_StopwatchDetails;
    public string ClockHint => R.C_Clock;
    public string ClockDetails => R.M_ClockDetails;
    public string LapText => R.C_Lap;
    public string ClearLapsText => R.C_ClearLaps;
    public string TotalTimeText => R.C_TotalTime;
    public string ShowMillisecondsText => R.C_ShowMilliseconds;
    public string MiniWindowText => R.C_MiniWindow;
    public string RestoreText => R.C_Restore;
    public string StartPauseIcon => _isRunning ? FluentIcons.PauseFilled : FluentIcons.PlayFilled;
    public ObservableCollection<StopwatchLap> Laps { get; } = [];
    public ObservableCollection<RecentTimerPreset> RecentPresets { get; } = [];
    public bool HasLaps => Laps.Count > 0;
    public bool HasNoRecentPresets => RecentPresets.Count == 0;
    public string NoRecentPresetsText => R.M_NoRecentPresets;
    public bool IsRecentPresetMode => PresetCategoryIndex == 1;
    public int PresetCategoryIndex
    {
        get => _presetCategoryIndex;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (_presetCategoryIndex == value)
                return;
            _presetCategoryIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRecentPresetMode));
        }
    }
    public bool ShowStopwatchMilliseconds
    {
        get => _showStopwatchMilliseconds;
        set
        {
            if (_showStopwatchMilliseconds == value)
                return;

            _showStopwatchMilliseconds = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTime));
        }
    }
    public string DisplayTime => IsStopwatchMode
        ? FormatStopwatchTime(_elapsed, _showStopwatchMilliseconds)
        : FormatTime(IsCountdownMode ? _remaining : DateTime.Now.TimeOfDay);
    public string SecondaryText => IsCountdownMode
        ? $"{Math.Round(Progress * 100):0}%"
        : IsClockMode
            ? DateTime.Now.ToString("yyyy-MM-dd dddd")
            : string.Empty;
    public TimeSpan SelectedTime
    {
        get => _remaining;
        set => SetCountdown(value, remember: true);
    }
    public IBrush RingBrush => _finished || (IsCountdownMode && _remaining <= TimeSpan.FromSeconds(10) && _remaining > TimeSpan.Zero)
        ? Brushes.IndianRed
        : Brushes.DodgerBlue;
    public double Progress => IsCountdownMode && _totalSeconds > 0
        ? Math.Clamp(_remaining.TotalSeconds / _totalSeconds, 0, 1)
        : 0;
    public int ModeIndex
    {
        get => (int)_mode;
        set => SetMode(value);
    }

    public string HoursText
    {
        get => ((int)_remaining.TotalHours).ToString("00");
        set => SetTimePart(value, TimePart.Hours);
    }

    public string MinutesText
    {
        get => _remaining.Minutes.ToString("00");
        set => SetTimePart(value, TimePart.Minutes);
    }

    public string SecondsText
    {
        get => _remaining.Seconds.ToString("00");
        set => SetTimePart(value, TimePart.Seconds);
    }

    [RelayCommand]
    private void ToggleStartPause()
    {
        if (_isRunning)
            Pause();
        else
            Start();
    }

    [RelayCommand]
    private void Reset()
    {
        _isRunning = false;
        _finished = false;
        if (IsCountdownMode)
            _remaining = TimeSpan.FromSeconds(_totalSeconds);
        else
        {
            _elapsed = TimeSpan.Zero;
            ClearLaps();
        }
        Refresh();
    }

    [RelayCommand]
    private void SetPreset(object seconds)
    {
        if (seconds is int value)
            SetCountdown(TimeSpan.FromSeconds(Math.Max(0, value)), remember: true);
        
        if (seconds is string preset && int.TryParse(preset, out var parsed))
            SetCountdown(TimeSpan.FromSeconds(Math.Max(0, parsed)), remember: true);
    }

    [RelayCommand]
    private void AddLap()
    {
        if (!IsStopwatchMode)
            return;

        UpdateTime();
        var lapTime = _elapsed - _lastLapTime;
        _lastLapTime = _elapsed;
        Laps.Add(new StopwatchLap(
            Laps.Count + 1,
            FormatStopwatchTime(lapTime),
            FormatStopwatchTime(_elapsed)));
        OnPropertyChanged(nameof(HasLaps));
    }

    [RelayCommand]
    private void ClearLaps()
    {
        Laps.Clear();
        _lastLapTime = TimeSpan.Zero;
        OnPropertyChanged(nameof(HasLaps));
    }

    public void HandleKey(Avalonia.Input.Key key)
    {
        if (key == Avalonia.Input.Key.Space && HasControls)
        {
            ToggleStartPause();
            return;
        }

        if (key == Avalonia.Input.Key.R && HasControls)
        {
            Reset();
            return;
        }

        if (IsCountdownMode && !_isRunning && key is Avalonia.Input.Key.Up or Avalonia.Input.Key.Down)
            SetCountdown(_remaining + TimeSpan.FromSeconds(key == Avalonia.Input.Key.Up ? 10 : -10));
    }

    private void SetMode(int value)
    {
        var mode = (TimerMode)Math.Clamp(value, (int)TimerMode.Countdown, (int)TimerMode.Clock);
        if (_mode == mode)
            return;

        _isRunning = false;
        _finished = false;
        _mode = mode;
        Refresh();
    }

    private void SetCountdown(TimeSpan value, bool remember = false)
    {
        _isRunning = false;
        _finished = false;
        _remaining = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        _totalSeconds = Math.Max(1, _remaining.TotalSeconds);
        if (remember)
            RememberRecentPreset((int)_remaining.TotalSeconds);
        Refresh();
    }

    private void LoadRecentPresets()
    {
        var values = _configHandler.Data.RecentTimerPresetSeconds
            .Where(seconds => seconds > 0)
            .Distinct()
            .Take(6)
            .ToArray();
        _configHandler.Data.RecentTimerPresetSeconds = values.ToList();
        foreach (var seconds in values)
            RecentPresets.Add(new RecentTimerPreset(seconds, FormatDuration(seconds)));
        OnPropertyChanged(nameof(HasNoRecentPresets));
    }

    private void RememberRecentPreset(int seconds)
    {
        if (seconds <= 0)
            return;

        var values = _configHandler.Data.RecentTimerPresetSeconds;
        values.Remove(seconds);
        values.Insert(0, seconds);
        if (values.Count > 6)
            values.RemoveRange(6, values.Count - 6);

        RecentPresets.Clear();
        foreach (var value in values)
            RecentPresets.Add(new RecentTimerPreset(value, FormatDuration(value)));
        OnPropertyChanged(nameof(HasNoRecentPresets));
        _configHandler.Save();
    }

    private static string FormatDuration(int seconds)
    {
        var value = TimeSpan.FromSeconds(seconds);
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }

    private void SetTimePart(string? value, TimePart part)
    {
        if (!int.TryParse(value, out var parsed))
            return;

        var hours = (int)_remaining.TotalHours;
        var minutes = _remaining.Minutes;
        var seconds = _remaining.Seconds;
        switch (part)
        {
            case TimePart.Hours:
                hours = Math.Max(0, parsed);
                break;
            case TimePart.Minutes:
                minutes = Math.Clamp(parsed, 0, 59);
                break;
            default:
                seconds = Math.Clamp(parsed, 0, 59);
                break;
        }

        SetCountdown(new TimeSpan(hours, minutes, seconds));
    }

    private void Start()
    {
        if (IsCountdownMode && _remaining <= TimeSpan.Zero)
            return;

        _finished = false;
        _isRunning = true;
        if (IsCountdownMode)
            _deadline = _clock.ElapsedMilliseconds + (long)_remaining.TotalMilliseconds;
        else
            _startedAt = _clock.ElapsedMilliseconds;
        Refresh();
    }

    private void Pause()
    {
        UpdateTime();
        _isRunning = false;
        Refresh();
    }

    private void Refresh()
    {
        if (_disposed)
            return;

        UpdateTime();
        foreach (var name in new[]
        {
            nameof(IsCountdownMode), nameof(IsStopwatchMode), nameof(IsClockMode), nameof(HasControls),
            nameof(ModeIndex), nameof(IsRunning), nameof(ModeText), nameof(StartPauseText), nameof(DisplayTime),
            nameof(SecondaryText), nameof(RingBrush), nameof(Progress), nameof(HasLaps),
            nameof(SelectedTime), nameof(HoursText), nameof(MinutesText), nameof(SecondsText), nameof(StartPauseIcon)
        })
            OnPropertyChanged(name);
    }

    private void UpdateTime()
    {
        if (!_isRunning)
            return;

        if (IsCountdownMode)
        {
            _remaining = TimeSpan.FromMilliseconds(Math.Max(0, _deadline - _clock.ElapsedMilliseconds));
            if (_remaining > TimeSpan.Zero)
                return;

            _isRunning = false;
            _finished = true;
        }
        else
        {
            _elapsed += TimeSpan.FromMilliseconds(Math.Max(0, _clock.ElapsedMilliseconds - _startedAt));
            _startedAt = _clock.ElapsedMilliseconds;
        }
    }

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{value.Minutes:00}:{value.Seconds:00}";

    private static string FormatStopwatchTime(TimeSpan value, bool includeMilliseconds = true) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}{(includeMilliseconds ? $".{value.Milliseconds:000}" : string.Empty)}"
        : $"{value.Minutes:00}:{value.Seconds:00}{(includeMilliseconds ? $".{value.Milliseconds:000}" : string.Empty)}";

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= _refreshHandler;
    }

    private enum TimerMode
    {
        Countdown,
        Stopwatch,
        Clock
    }

    private enum TimePart
    {
        Hours,
        Minutes,
        Seconds
    }

    public sealed record StopwatchLap(int Number, string LapTime, string TotalTime);
    public sealed record RecentTimerPreset(int Seconds, string Label);
}
