namespace SecRandom.Core.Models.Linkage;

public enum CourseTimeState
{
    Unknown,
    OnClass,
    Breaking
}

public sealed record CourseInfo(
    string Name,
    int DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string Teacher = "",
    string Location = "");

public sealed record CourseScheduleSnapshot(
    bool IsAvailable,
    CourseTimeState State,
    CourseInfo? CurrentCourse,
    CourseInfo? PreviousCourse,
    CourseInfo? NextCourse,
    TimeSpan? CurrentCourseRemaining,
    TimeSpan? TimeUntilNextCourse,
    TimeSpan? TimeSincePreviousCourseEnd,
    string Source,
    string Version,
    string? Error = null)
{
    public static CourseScheduleSnapshot Unavailable(string source, string? error = null) => new(
        false,
        CourseTimeState.Unknown,
        null,
        null,
        null,
        null,
        null,
        null,
        source,
        string.Empty,
        error);
}
