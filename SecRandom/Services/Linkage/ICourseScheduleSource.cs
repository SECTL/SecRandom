using System;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core.Models.Linkage;

namespace SecRandom.Services.Linkage;

public interface ICourseScheduleSource
{
    string SourceName { get; }
    event EventHandler? StateChanged;
    Task<CourseScheduleSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
