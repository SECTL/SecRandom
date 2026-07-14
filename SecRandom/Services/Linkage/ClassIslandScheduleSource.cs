using System;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Shared.Enums;
using ClassIsland.Shared.IPC;
using ClassIsland.Shared.IPC.Abstractions.Services;
using dotnetCampus.Ipc.CompilerServices.GeneratedProxies;
using dotnetCampus.Ipc.Pipes;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Models.Linkage;

namespace SecRandom.Services.Linkage;

public sealed class ClassIslandScheduleSource(ILogger<ClassIslandScheduleSource> logger) : ICourseScheduleSource
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private IpcClient? _client;
    private IPublicLessonsService? _lessons;
    private string _lastKnownCourseName = string.Empty;
    private DateOnly? _lastKnownCourseDate;
    private DateTimeOffset _nextConnectAttempt = DateTimeOffset.MinValue;

    public string SourceName => "ClassIsland";
    public event EventHandler? StateChanged;

    public async Task<CourseScheduleSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var lessons = await GetLessonsAsync(cancellationToken).ConfigureAwait(false);
        if (lessons is null)
            return CourseScheduleSnapshot.Unavailable(SourceName, "无法连接到 ClassIsland。");

        try
        {
            if (!lessons.IsTimerRunning)
                return CourseScheduleSnapshot.Unavailable(SourceName, "ClassIsland 计时器未运行。");
            if (!lessons.IsClassPlanEnabled)
                return CourseScheduleSnapshot.Unavailable(SourceName, "ClassIsland 未启用课程表。");
            if (!lessons.IsClassPlanLoaded)
                return CourseScheduleSnapshot.Unavailable(SourceName, "ClassIsland 尚未加载当天课表。");
            if (!lessons.IsLessonConfirmed)
                return CourseScheduleSnapshot.Unavailable(SourceName, "ClassIsland 尚未确认当前时间点。");

            var state = lessons.CurrentState switch
            {
                TimeState.OnClass => CourseTimeState.OnClass,
                TimeState.Breaking => CourseTimeState.Breaking,
                _ => CourseTimeState.Unknown
            };
            if (state == CourseTimeState.Unknown)
                return CourseScheduleSnapshot.Unavailable(SourceName, $"ClassIsland 当前状态为 {lessons.CurrentState}。");

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
            logger.LogDebug(exception, "读取 ClassIsland 课程状态失败。");
            InvalidateConnection();
            return CourseScheduleSnapshot.Unavailable(SourceName, "读取 ClassIsland 课程状态失败。");
        }
    }

    private async Task<IPublicLessonsService?> GetLessonsAsync(CancellationToken cancellationToken)
    {
        if (_lessons is not null)
            return _lessons;
        if (DateTimeOffset.UtcNow < _nextConnectAttempt)
            return null;

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lessons is not null)
                return _lessons;
            if (DateTimeOffset.UtcNow < _nextConnectAttempt)
                return null;

            var client = new IpcClient();
            client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnClassNotifyId, OnClassIslandStateChanged);
            client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnBreakingTimeNotifyId, OnClassIslandStateChanged);
            client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnAfterSchoolNotifyId, OnClassIslandStateChanged);
            client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.CurrentTimeStateChangedNotifyId, OnClassIslandStateChanged);
            await client.Connect().WaitAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false);
            if (client.PeerProxy is null)
            {
                ScheduleRetry();
                return null;
            }

            _client = client;
            _lessons = GeneratedIpcFactory.CreateIpcProxy<IPublicLessonsService>(client.Provider, client.PeerProxy);
            _nextConnectAttempt = DateTimeOffset.MinValue;
            logger.LogInformation("已连接到 ClassIsland IPC：管道={PipeName}。", IpcClient.PipeName);
            return _lessons;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "连接 ClassIsland IPC 失败，将在 {RetryDelay} 后重试。", RetryDelay);
            InvalidateConnection();
            ScheduleRetry();
            return null;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private void OnClassIslandStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void InvalidateConnection()
    {
        _lessons = null;
        _client = null;
    }

    private void ScheduleRetry()
    {
        _nextConnectAttempt = DateTimeOffset.UtcNow.Add(RetryDelay);
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
