using System;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Shared.Enums;
using ClassIsland.Shared.IPC;
using ClassIsland.Shared.IPC.Abstractions.Services;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Models.Linkage;

namespace SecRandom.Services.Linkage;

public sealed class ClassIslandScheduleSource : ICourseScheduleSource
{
    private readonly ClassIslandIpcConnection _ipcConnection;
    private readonly ILogger<ClassIslandScheduleSource> _logger;
    private string _lastKnownCourseName = string.Empty;
    private DateOnly? _lastKnownCourseDate;

    public string SourceName => "ClassIsland";
    public event EventHandler? StateChanged;

    public ClassIslandScheduleSource(ClassIslandIpcConnection ipcConnection, ILogger<ClassIslandScheduleSource> logger)
    {
        _ipcConnection = ipcConnection;
        _logger = logger;
        _ipcConnection.StateChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<CourseScheduleSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var lessons = await _ipcConnection.GetLessonsServiceAsync(cancellationToken).ConfigureAwait(false);
        if (lessons is null)
            return CourseScheduleSnapshot.Unavailable(SourceName, ScheduleErrorCodes.ClassIslandUnavailable);

        try
        {
            if (!lessons.IsTimerRunning)
                return CourseScheduleSnapshot.Unavailable(SourceName, ScheduleErrorCodes.ClassIslandTimerStopped);
            if (!lessons.IsClassPlanEnabled)
                return CourseScheduleSnapshot.Unavailable(SourceName, ScheduleErrorCodes.ClassIslandScheduleDisabled);
            if (!lessons.IsClassPlanLoaded)
                return CourseScheduleSnapshot.Unavailable(SourceName, ScheduleErrorCodes.ClassIslandScheduleUnloaded);
            if (!lessons.IsLessonConfirmed)
                return CourseScheduleSnapshot.Unavailable(SourceName, ScheduleErrorCodes.ClassIslandTimeUnconfirmed);

            var state = lessons.CurrentState switch
            {
                TimeState.OnClass => CourseTimeState.OnClass,
                TimeState.Breaking => CourseTimeState.Breaking,
                _ => CourseTimeState.Unknown
            };
            if (state == CourseTimeState.Unknown)
                return CourseScheduleSnapshot.Unavailable(SourceName,
                    $"{ScheduleErrorCodes.ClassIslandUnsupportedState}:{lessons.CurrentState}");

            // Latest ClassIsland exposes the break label through CurrentSubject during Breaking.
            var currentName = state == CourseTimeState.OnClass
                ? NormalizeSubjectName(lessons.CurrentSubject?.Name)
                : string.Empty;
            var nextName = NormalizeSubjectName(lessons.NextClassSubject?.Name);
            var now = DateTime.Now;
            if (!string.IsNullOrEmpty(currentName))
            {
                _lastKnownCourseName = currentName;
                _lastKnownCourseDate = DateOnly.FromDateTime(now);
            }

            var currentItem = lessons.CurrentTimeLayoutItem;
            var start = ParseTime(currentItem?.StartTime, now.TimeOfDay);
            var end = ParseTime(currentItem?.EndTime, now.TimeOfDay);
            var current = string.IsNullOrEmpty(currentName)
                ? null
                : new CourseInfo(currentName, DayOfWeekNumber(now.DayOfWeek), TimeOnly.FromTimeSpan(start), TimeOnly.FromTimeSpan(end));
            var nextStart = now.TimeOfDay.Add(lessons.OnClassLeftTime > TimeSpan.Zero
                ? lessons.OnClassLeftTime
                : TimeSpan.Zero);
            var next = string.IsNullOrEmpty(nextName)
                ? null
                : new CourseInfo(nextName, DayOfWeekNumber(now.DayOfWeek), TimeOnly.FromTimeSpan(nextStart), TimeOnly.MinValue);
            var previous = _lastKnownCourseDate != DateOnly.FromDateTime(now) || string.IsNullOrWhiteSpace(_lastKnownCourseName)
                ? null
                : new CourseInfo(_lastKnownCourseName, DayOfWeekNumber(now.DayOfWeek), TimeOnly.MinValue, TimeOnly.MinValue);

            var nextCourseIn = Positive(lessons.OnClassLeftTime);
            var currentCourseRemaining = state == CourseTimeState.OnClass
                ? Positive(lessons.OnBreakingTimeLeftTime)
                : null;
            // Version only includes stable identifiers (schedule index + state), NOT countdown timers
            // This prevents false StateChanged triggers from continuously changing OnClassLeftTime/OnBreakingTimeLeftTime
            return new CourseScheduleSnapshot(
                true,
                state,
                current,
                previous,
                next,
                currentCourseRemaining,
                nextCourseIn,
                null,
                SourceName,
                $"{lessons.CurrentSelectedIndex}:{lessons.CurrentState}");
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "读取 ClassIsland 日程状态失败。");
            return CourseScheduleSnapshot.Unavailable(SourceName, ScheduleErrorCodes.ClassIslandReadFailed);
        }
    }

    private static string NormalizeSubjectName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        return normalized is "" or "???" ? string.Empty : normalized;
    }

    private static TimeSpan? Positive(TimeSpan value) => value > TimeSpan.Zero ? value : null;

    private static TimeSpan ParseTime(TimeSpan? value, TimeSpan fallback)
    {
        return value ?? fallback;
    }

    private static int DayOfWeekNumber(DayOfWeek dayOfWeek) => ((int)dayOfWeek + 6) % 7 + 1;
}
