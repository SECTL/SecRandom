using SecRandom.Services.Linkage;

namespace SecRandom.Core.Tests;

public sealed class CsesScheduleParserTests
{
    private readonly CsesScheduleParser _parser = new();

    [Fact]
    public void Parse_TimeslotsLayout_NormalizesCourses()
    {
        var schedule = _parser.Parse("""
                                     schedule:
                                       timeslots:
                                         - name: 数学
                                           start_time: "08:00"
                                           end_time: "08:40"
                                           day_of_week: 1
                                     """);

        var course = Assert.Single(schedule.Courses);
        Assert.Equal("数学", course.Name);
        Assert.Equal(1, course.DayOfWeek);
        Assert.Equal(new TimeOnly(8, 0), course.StartTime);
        Assert.Equal(new TimeOnly(8, 40), course.EndTime);
    }

    [Fact]
    public void Parse_SchedulesLayout_UsesSubjectTeacherFallback()
    {
        var schedule = _parser.Parse("""
                                     subjects:
                                       - name: 英语
                                         teacher: 李老师
                                     schedules:
                                       - enable_day: 2
                                         weeks: all
                                         classes:
                                           - subject: 英语
                                             start_time: 32400
                                             end_time: "09:40:00"
                                             room: 302
                                     """);

        var course = Assert.Single(schedule.Courses);
        Assert.Equal("英语", course.Name);
        Assert.Equal("李老师", course.Teacher);
        Assert.Equal("302", course.Location);
        Assert.Equal(new TimeOnly(9, 0), course.StartTime);
    }

    [Fact]
    public void Parse_MixedLayouts_PrefersTimeslotsLikeV2()
    {
        var schedule = _parser.Parse("""
                                     schedule:
                                       timeslots:
                                         - name: 数学
                                           start_time: "08:00"
                                           end_time: "08:40"
                                           day_of_week: 1
                                     schedules:
                                       - enable_day: 1
                                         classes:
                                           - subject: 英语
                                             start_time: "08:00"
                                             end_time: "08:40"
                                     """);

        var course = Assert.Single(schedule.Courses);
        Assert.Equal("数学", course.Name);
    }

    [Fact]
    public void Parse_OverlappingCourses_RejectsSchedule()
    {
        var exception = Assert.Throws<InvalidDataException>(() => _parser.Parse("""
                                                                                  schedule:
                                                                                    timeslots:
                                                                                      - name: A
                                                                                        start_time: "08:00"
                                                                                        end_time: "08:40"
                                                                                        day_of_week: 1
                                                                                      - name: B
                                                                                        start_time: "08:30"
                                                                                        end_time: "09:00"
                                                                                        day_of_week: 1
                                                                                  """));

        Assert.Contains("重叠", exception.Message);
    }

    [Fact]
    public void Evaluate_BreakingState_ExposesPreviousAndNextCourse()
    {
        var schedule = _parser.Parse("""
                                     schedule:
                                       timeslots:
                                         - name: A
                                           start_time: "08:00"
                                           end_time: "08:40"
                                           day_of_week: 1
                                         - name: B
                                           start_time: "09:00"
                                           end_time: "09:40"
                                           day_of_week: 1
                                     """);
        var now = new DateTimeOffset(2026, 7, 13, 8, 50, 0, TimeSpan.Zero);

        var snapshot = CourseScheduleMath.Evaluate(schedule, now);

        Assert.Equal(SecRandom.Core.Models.Linkage.CourseTimeState.Breaking, snapshot.State);
        Assert.Equal("A", snapshot.PreviousCourse?.Name);
        Assert.Equal("B", snapshot.NextCourse?.Name);
        Assert.Equal(TimeSpan.FromMinutes(10), snapshot.TimeUntilNextCourse);
    }

    [Fact]
    public void Evaluate_BeforeFirstCourse_DoesNotAssignPreviousWeekCourse()
    {
        var schedule = _parser.Parse("""
                                     schedule:
                                       timeslots:
                                         - name: 周日课程
                                           start_time: "08:00"
                                           end_time: "08:40"
                                           day_of_week: 7
                                         - name: 周一课程
                                           start_time: "09:00"
                                           end_time: "09:40"
                                           day_of_week: 1
                                     """);
        var now = new DateTimeOffset(2026, 7, 13, 8, 30, 0, TimeSpan.Zero);

        var snapshot = CourseScheduleMath.Evaluate(schedule, now);

        Assert.Null(snapshot.PreviousCourse);
        Assert.Equal("周一课程", snapshot.NextCourse?.Name);
    }
}
