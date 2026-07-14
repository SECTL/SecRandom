using System;
using System.Collections.Generic;
using System.Linq;
using SecRandom.Core.Models.Linkage;

namespace SecRandom.Services.Linkage;

public static class CourseScheduleMath
{
    public static CourseScheduleSnapshot Evaluate(CsesSchedule schedule, DateTimeOffset now)
    {
        var localNow = now.DateTime;
        var today = DayOfWeekNumber(localNow.DayOfWeek);
        var occurrences = schedule.Courses
            .SelectMany(course => BuildOccurrences(course, localNow.Date, today))
            .OrderBy(occurrence => occurrence.Start)
            .ToArray();
        var current = occurrences.FirstOrDefault(item => item.Course.DayOfWeek == today && item.Start <= localNow && localNow < item.End);
        var previous = occurrences.Where(item => item.Date == localNow.Date && item.End <= localNow)
            .OrderByDescending(item => item.End).FirstOrDefault();
        var next = occurrences.Where(item => item.Start > localNow).OrderBy(item => item.Start).FirstOrDefault();

        return new CourseScheduleSnapshot(
            true,
            current is not null ? CourseTimeState.OnClass : CourseTimeState.Breaking,
            current?.Course,
            previous?.Course,
            next?.Course,
            current is null ? null : current.End - localNow,
            next is null ? null : next.Start - localNow,
            previous is null ? null : localNow - previous.End,
            "CSES",
            schedule.Version);
    }

    private static IEnumerable<CourseOccurrence> BuildOccurrences(CourseInfo course, DateTime today, int todayNumber)
    {
        var daysUntil = (course.DayOfWeek - todayNumber + 7) % 7;
        var nextDate = today.AddDays(daysUntil);
        yield return CreateOccurrence(course, nextDate.AddDays(-7));
        yield return CreateOccurrence(course, nextDate);
        yield return CreateOccurrence(course, nextDate.AddDays(7));
    }

    private static CourseOccurrence CreateOccurrence(CourseInfo course, DateTime date) => new(
        course,
        date,
        date.Add(course.StartTime.ToTimeSpan()),
        date.Add(course.EndTime.ToTimeSpan()));

    private static int DayOfWeekNumber(DayOfWeek dayOfWeek) => ((int)dayOfWeek + 6) % 7 + 1;

    private sealed record CourseOccurrence(CourseInfo Course, DateTime Date, DateTime Start, DateTime End);
}
