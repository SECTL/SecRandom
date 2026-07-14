using System;
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

    public Task<CourseScheduleSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var schedule = _scheduleStore.Load();
        return Task.FromResult(schedule is null
            ? CourseScheduleSnapshot.Unavailable(SourceName, "未导入有效的 CSES 课程表。")
            : CourseScheduleMath.Evaluate(schedule, DateTimeOffset.Now));
    }
}
