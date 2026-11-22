using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core.Models.Linkage;

namespace SecRandom.Services.Linkage;

public sealed class CsesScheduleSource : ICourseScheduleSource
{
    private readonly ICsesScheduleStore _scheduleStore;

    public CsesScheduleSource(ICsesScheduleStore scheduleStore)
    {
        _scheduleStore = scheduleStore;
        _scheduleStore.ScheduleChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public string SourceName => "CSES";
    public event EventHandler? StateChanged;

    public System.Collections.Generic.IReadOnlyList<string> GetCourseNames() => _scheduleStore.Load()?.Courses
        .Select(course => course.Name.Trim())
        .Where(name => name.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.CurrentCulture)
        .ToArray() ?? [];

    public Task<CourseScheduleSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var schedule = _scheduleStore.Load();
        return Task.FromResult(schedule is null
            ? CourseScheduleSnapshot.Unavailable(SourceName, ScheduleErrorCodes.CsesMissing)
            : CourseScheduleMath.Evaluate(schedule, DateTimeOffset.Now));
    }
}

internal static class ScheduleErrorCodes
{
    public const string CsesMissing = "cses.missing";
    public const string ClassIslandUnavailable = "classisland.unavailable";
    public const string ClassIslandTimerStopped = "classisland.timer-stopped";
    public const string ClassIslandScheduleDisabled = "classisland.schedule-disabled";
    public const string ClassIslandScheduleUnloaded = "classisland.schedule-unloaded";
    public const string ClassIslandUnsupportedState = "classisland.unsupported-state";
    public const string ClassIslandReadFailed = "classisland.read-failed";
}
