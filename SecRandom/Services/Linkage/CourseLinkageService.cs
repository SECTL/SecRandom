using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Linkage;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;

namespace SecRandom.Services.Linkage;

public sealed class CourseLinkageService
{
    private readonly MainConfigHandler _configHandler;
    private readonly ICsesScheduleStore _csesScheduleStore;
    private readonly CsesScheduleSource _csesSource;
    private readonly ClassIslandScheduleSource _classIslandSource;
    private readonly ILogger<CourseLinkageService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CourseScheduleSnapshot _snapshot = CourseScheduleSnapshot.Unavailable("Off");

    public CourseLinkageService(
        MainConfigHandler configHandler,
        ICsesScheduleStore csesScheduleStore,
        CsesScheduleSource csesSource,
        ClassIslandScheduleSource classIslandSource,
        ILogger<CourseLinkageService> logger)
    {
        _configHandler = configHandler;
        _csesScheduleStore = csesScheduleStore;
        _csesSource = csesSource;
        _classIslandSource = classIslandSource;
        _logger = logger;
        Settings.PropertyChanged += SettingsOnPropertyChanged;
        _csesSource.StateChanged += (_, _) => _ = RefreshAsync();
        _classIslandSource.StateChanged += (_, _) => _ = RefreshAsync();
    }

    public event EventHandler? StateChanged;
    public CourseScheduleSnapshot Snapshot => _snapshot;
    public LinkageSettingsConfig Settings => _configHandler.Data.LinkageSettings;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var stateChanged = false;
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var source = Settings.DataSource switch
            {
                LinkageDataSource.Cses => (ICourseScheduleSource)_csesSource,
                LinkageDataSource.ClassIsland => _classIslandSource,
                _ => null
            };
            var next = source is null
                ? CourseScheduleSnapshot.Unavailable("Off")
                : await source.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (Equals(_snapshot, next))
                return;
            _snapshot = next;
            stateChanged = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "刷新课程联动状态失败。");
            var unavailable = CourseScheduleSnapshot.Unavailable("Unknown", exception.Message);
            if (!Equals(_snapshot, unavailable))
            {
                _snapshot = unavailable;
                stateChanged = true;
            }
        }
        finally
        {
            _refreshGate.Release();
        }

        if (stateChanged)
            NotifyStateChanged();
    }

    public bool IsConfirmedBreakTime => _snapshot.IsAvailable
        && _snapshot.State == CourseTimeState.Breaking
        && !IsWithinEnableWindow(_snapshot);

    public bool IsConfirmedNonClassTime => Settings.InstantDrawDisable
        && IsConfirmedBreakTime;

    public string GetSubjectFilter()
    {
        if (!Settings.SubjectHistoryFilterEnabled || !_snapshot.IsAvailable)
            return string.Empty;
        if (_snapshot.State == CourseTimeState.OnClass)
            return _snapshot.CurrentCourse?.Name ?? string.Empty;
        if (_snapshot.State != CourseTimeState.Breaking)
            return string.Empty;

        var assignedCourse = Settings.SubjectHistoryBreakAssignment switch
        {
            LinkageBreakAssignment.Break => "__break__",
            LinkageBreakAssignment.PreviousClass => _snapshot.PreviousCourse?.Name ?? string.Empty,
            LinkageBreakAssignment.NextClass => _snapshot.NextCourse?.Name ?? string.Empty,
            _ => string.Empty
        };
        if (!string.IsNullOrWhiteSpace(assignedCourse) || _snapshot.Source != "ClassIsland")
            return assignedCourse;

        var fallback = _csesScheduleStore.Load();
        if (fallback is null)
            return string.Empty;
        var fallbackSnapshot = CourseScheduleMath.Evaluate(fallback, DateTimeOffset.Now);
        return Settings.SubjectHistoryBreakAssignment switch
        {
            LinkageBreakAssignment.PreviousClass => fallbackSnapshot.PreviousCourse?.Name ?? string.Empty,
            LinkageBreakAssignment.NextClass => fallbackSnapshot.NextCourse?.Name ?? string.Empty,
            _ => string.Empty
        };
    }

    public TimeSpan GetNextRefreshDelay()
    {
        if (Settings.DataSource == LinkageDataSource.ClassIsland && !_snapshot.IsAvailable)
            return TimeSpan.FromSeconds(5);

        List<TimeSpan> candidates = [];
        if (_snapshot.TimeUntilNextCourse is { } untilNext && untilNext > TimeSpan.Zero)
        {
            candidates.Add(untilNext);
            var preClass = TimeSpan.FromSeconds(Math.Clamp(Settings.PreClassEnableTime, 0, 3600));
            if (preClass > TimeSpan.Zero && untilNext > preClass)
                candidates.Add(untilNext - preClass);
            var preReset = TimeSpan.FromSeconds(Math.Clamp(Settings.PreClassResetTime, 1, 3600));
            if (Settings.PreClassResetEnabled && untilNext > preReset)
                candidates.Add(untilNext - preReset);
        }
        if (_snapshot.CurrentCourseRemaining is { } remaining && remaining > TimeSpan.Zero)
        {
            candidates.Add(remaining + TimeSpan.FromSeconds(Math.Clamp(Settings.PostClassDisableDelay, 0, 3600)));
        }
        if (_snapshot.TimeSincePreviousCourseEnd is { } sinceEnd)
        {
            var postDelay = TimeSpan.FromSeconds(Math.Clamp(Settings.PostClassDisableDelay, 0, 3600));
            if (postDelay > sinceEnd)
                candidates.Add(postDelay - sinceEnd);
        }

        return candidates.Where(delay => delay > TimeSpan.Zero).DefaultIfEmpty(TimeSpan.FromMinutes(2)).Min();
    }

    public bool IsPreClassResetDue(out string resetKey)
    {
        resetKey = string.Empty;
        if (!Settings.PreClassResetEnabled || !_snapshot.IsAvailable || _snapshot.NextCourse is null
            || _snapshot.TimeUntilNextCourse is not { } untilNext)
            return false;

        var window = TimeSpan.FromSeconds(Math.Clamp(Settings.PreClassResetTime, 1, 3600));
        if (untilNext <= TimeSpan.Zero || untilNext > window)
            return false;
        resetKey = string.Join('|', _snapshot.Source, _snapshot.Version, DateTime.Today.ToString("yyyyMMdd"),
            _snapshot.NextCourse.DayOfWeek, _snapshot.NextCourse.StartTime, _snapshot.NextCourse.Name);
        return true;
    }

    private bool IsWithinEnableWindow(CourseScheduleSnapshot snapshot)
    {
        if (snapshot.TimeUntilNextCourse is { } untilNext
            && untilNext > TimeSpan.Zero
            && untilNext <= TimeSpan.FromSeconds(Math.Clamp(Settings.PreClassEnableTime, 0, 3600)))
            return true;
        if (snapshot.TimeSincePreviousCourseEnd is { } sinceEnd
            && sinceEnd >= TimeSpan.Zero
            && sinceEnd <= TimeSpan.FromSeconds(Math.Clamp(Settings.PostClassDisableDelay, 0, 3600)))
            return true;
        return false;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        _ = RefreshAsync();
    }

    private void NotifyStateChanged()
    {
        foreach (var handler in StateChanged?.GetInvocationList().OfType<EventHandler>() ?? [])
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "课程联动状态订阅者处理失败。");
            }
        }
    }
}
