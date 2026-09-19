using System;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Shared.Enums;
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
    private DateTime? _lastKnownCourseEnd;

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

            // ClassIsland 只在当前时间落在上课/课间时间点内时才置 IsLessonConfirmed，放学与课前必然为 false，
            // 因此这里不能再把 IsLessonConfirmed 当作可用性门槛；今天没有课表的情况由上面的
            // IsClassPlanLoaded 继续保证放行
            var state = MapCurrentState(lessons.CurrentState);
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
            // 记录当前课程结束时间，供课后禁用延迟窗口计算使用
            if (state == CourseTimeState.OnClass && currentItem?.EndTime is { } endTime)
                _lastKnownCourseEnd = now.Date + endTime;
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
            // 与 CSES 源一致：课后经过的时间驱动课后禁用延迟窗口与刷新调度
            var sincePreviousEnd = _lastKnownCourseEnd is { } lastEnd &&
                lastEnd.Date == now.Date &&
                lastEnd.TimeOfDay <= now.TimeOfDay
                ? (TimeSpan?)(now - lastEnd)
                : null;
            // Version 只包含稳定的定位信息（时间点索引 + 状态），不含倒计时字段：联动的语义比较依赖它
            return new CourseScheduleSnapshot(
                true,
                state,
                current,
                previous,
                next,
                currentCourseRemaining,
                nextCourseIn,
                sincePreviousEnd,
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

    /// <summary>
    /// 把 ClassIsland 的时间状态映射为课程联动状态。ClassIsland 在最后一节课后报告 AfterSchool，
    /// 在第一节课前或时间表未覆盖的间隙报告 None，PrepareOnClass 为预留的上课准备状态。这些都是
    /// 明确的非上课时段，与 CSES 源一致视为课间并保持快照可用，再由课前解禁与课后延迟窗口决定豁免。
    /// </summary>
    internal static CourseTimeState MapCurrentState(TimeState state) => state switch
    {
        TimeState.OnClass => CourseTimeState.OnClass,
        TimeState.Breaking or TimeState.None or TimeState.AfterSchool or TimeState.PrepareOnClass
            => CourseTimeState.Breaking,
        _ => CourseTimeState.Unknown
    };

    private static TimeSpan? Positive(TimeSpan value) => value > TimeSpan.Zero ? value : null;

    private static TimeSpan ParseTime(TimeSpan? value, TimeSpan fallback)
    {
        return value ?? fallback;
    }

    private static int DayOfWeekNumber(DayOfWeek dayOfWeek) => ((int)dayOfWeek + 6) % 7 + 1;
}
