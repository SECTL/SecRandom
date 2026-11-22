using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Services.Linkage;

/// <summary>Reads the course names offered by the selected linkage source.</summary>
public sealed class CourseRosterCourseCatalog(
    CsesScheduleSource csesSource,
    ClassIslandScheduleSource classIslandSource)
{
    public async Task<IReadOnlyList<string>> GetCourseNamesAsync(
        LinkageDataSource source,
        CancellationToken cancellationToken = default)
    {
        return source switch
        {
            LinkageDataSource.Cses => csesSource.GetCourseNames(),
            LinkageDataSource.ClassIsland => await classIslandSource.GetCourseNamesAsync(cancellationToken)
                .ConfigureAwait(false),
            _ => []
        };
    }
}
