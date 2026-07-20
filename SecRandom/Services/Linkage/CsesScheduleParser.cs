using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using YamlDotNet.Serialization;
using SecRandom.Core.Models.Linkage;

namespace SecRandom.Services.Linkage;

public sealed class CsesScheduleParser
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();

    public CsesSchedule Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw CsesScheduleException.Create(CsesScheduleError.Empty);

        var root = AsMap(_deserializer.Deserialize<object>(content))
            ?? throw CsesScheduleException.Create(CsesScheduleError.RootNotObject);
        var courses = ParseTimeslots(root).ToList();
        if (courses.Count == 0)
            throw CsesScheduleException.Create(CsesScheduleError.NoValidItems);

        ValidateCourses(courses);
        var normalized = string.Join("\n", courses
            .OrderBy(course => course.DayOfWeek)
            .ThenBy(course => course.StartTime)
            .Select(course => $"{course.DayOfWeek}|{course.Name}|{course.StartTime:HH:mm:ss}|{course.EndTime:HH:mm:ss}|{course.Teacher}|{course.Location}"));
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
        return new CsesSchedule(courses, version);
    }

    private static IEnumerable<CourseInfo> ParseTimeslots(IReadOnlyDictionary<string, object?> root)
    {
        var subjects = BuildSubjectTeacherMap(root.TryGetValue("subjects", out var subjectsValue) ? subjectsValue : null);
        if (root.TryGetValue("schedule", out var scheduleValue) && AsMap(scheduleValue) is { } schedule)
        {
            var timeslots = AsList(schedule.TryGetValue("timeslots", out var value) ? value : null).ToArray();
            if (timeslots.Length > 0)
            {
                foreach (var item in timeslots)
                {
                    if (AsMap(item) is not { } slot || !TryCreateCourse(slot, subjects, out var course))
                        throw CsesScheduleException.Create(CsesScheduleError.InvalidItem);
                    yield return course;
                }

                yield break;
            }
        }

        if (root.TryGetValue("schedules", out var schedulesValue))
        {
            foreach (var scheduleItem in AsList(schedulesValue))
            {
                if (AsMap(scheduleItem) is not { } daySchedule)
                    continue;
                var day = GetInt(daySchedule, "enable_day", 0);
                foreach (var classItem in AsList(daySchedule.TryGetValue("classes", out var classes) ? classes : null))
                {
                    if (AsMap(classItem) is not { } @class)
                        throw CsesScheduleException.Create(CsesScheduleError.InvalidItem);
                    var mapped = new Dictionary<string, object?>(@class, StringComparer.OrdinalIgnoreCase)
                    {
                        ["day_of_week"] = day
                    };
                    if (!mapped.ContainsKey("name") && mapped.TryGetValue("subject", out var subject))
                        mapped["name"] = subject;
                    if (!mapped.ContainsKey("location") && mapped.TryGetValue("room", out var room))
                        mapped["location"] = room;
                    if (!TryCreateCourse(mapped, subjects, out var course))
                        throw CsesScheduleException.Create(CsesScheduleError.InvalidItem);
                    yield return course;
                }
            }
        }
    }

    private static Dictionary<string, string> BuildSubjectTeacherMap(object? value)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (var item in AsList(value))
        {
            if (AsMap(item) is not { } subject)
                continue;
            var name = GetString(subject, "name");
            var teacher = GetString(subject, "teacher");
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(teacher))
                result[name] = teacher;
        }

        return result;
    }

    private static bool TryCreateCourse(
        IReadOnlyDictionary<string, object?> slot,
        IReadOnlyDictionary<string, string> subjectTeachers,
        out CourseInfo course)
    {
        course = default!;
        var name = GetString(slot, "name");
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var day = GetInt(slot, "day_of_week", 0);
        var start = ParseTime(slot.TryGetValue("start_time", out var startValue) ? startValue : null);
        var end = ParseTime(slot.TryGetValue("end_time", out var endValue) ? endValue : null);
        if (day is < 1 or > 7 || start is null || end is null)
            return false;

        var teacher = GetString(slot, "teacher");
        if (string.IsNullOrWhiteSpace(teacher))
            subjectTeachers.TryGetValue(name, out teacher);
        course = new CourseInfo(name.Trim(), day, start.Value, end.Value, teacher ?? string.Empty,
            GetString(slot, "location").Length > 0 ? GetString(slot, "location") : GetString(slot, "room"));
        return true;
    }

    private static void ValidateCourses(IReadOnlyList<CourseInfo> courses)
    {
        foreach (var course in courses)
        {
            if (course.StartTime >= course.EndTime)
                throw CsesScheduleException.Create(CsesScheduleError.InvalidTime, course.Name);
        }

        foreach (var group in courses.GroupBy(course => course.DayOfWeek))
        {
            var ordered = group.OrderBy(course => course.StartTime).ToArray();
            for (var i = 1; i < ordered.Length; i++)
                if (ordered[i].StartTime < ordered[i - 1].EndTime)
                    throw CsesScheduleException.Create(CsesScheduleError.OverlappingItems, group.Key);
        }
    }

    private static TimeOnly? ParseTime(object? value)
    {
        if (value is null)
            return null;
        if (value is int integer)
            return ParseSeconds(integer);
        if (value is long longValue)
            return ParseSeconds(longValue);
        if (value is double doubleValue)
            return ParseSeconds(doubleValue);

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return ParseSeconds(seconds);
        if (TimeOnly.TryParseExact(text, ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return time;
        return null;
    }

    private static TimeOnly? ParseSeconds(double seconds)
    {
        return seconds is >= 0 and < 24 * 60 * 60
            ? TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(seconds))
            : null;
    }

    private static string GetString(IReadOnlyDictionary<string, object?> map, string key)
    {
        return map.TryGetValue(key, out var value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
    }

    private static int GetInt(IReadOnlyDictionary<string, object?> map, string key, int fallback)
    {
        return map.TryGetValue(key, out var value) && int.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var result) ? result : fallback;
    }

    private static IReadOnlyDictionary<string, object?>? AsMap(object? value)
    {
        if (value is IDictionary dictionary)
        {
            Dictionary<string, object?> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dictionary)
                result[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = entry.Value;
            return result;
        }

        return null;
    }

    private static IEnumerable<object?> AsList(object? value)
    {
        return value is IEnumerable enumerable and not string ? enumerable.Cast<object?>() : [];
    }
}

public sealed record CsesSchedule(IReadOnlyList<CourseInfo> Courses, string Version)
{
    public int PeriodCount => Courses.Count;
    public TimeOnly Earliest => Courses.Min(course => course.StartTime);
    public TimeOnly Latest => Courses.Max(course => course.EndTime);
}

public enum CsesScheduleError
{
    Empty,
    RootNotObject,
    NoValidItems,
    InvalidItem,
    InvalidTime,
    OverlappingItems
}

// InvalidDataException 是密封类型，CsesScheduleException 无法作为异常继承它；
// 改为工厂方法：生成固定中文 Message 的 InvalidDataException，并把结构化错误码/参数
// 存入 Data，UI 层用 TryGetError 取回后按资源本地化（与 Message 的文化无关性解耦）。
public static class CsesScheduleException
{
    private const string ErrorKey = nameof(CsesScheduleError);
    private const string ArgumentKey = "Argument";

    public static InvalidDataException Create(CsesScheduleError error, object? argument = null)
    {
        var exception = new InvalidDataException(Describe(error, argument));
        exception.Data[ErrorKey] = error;
        if (argument is not null)
            exception.Data[ArgumentKey] = argument;
        return exception;
    }

    public static bool TryGetError(
        InvalidDataException exception,
        out CsesScheduleError error,
        out object? argument)
    {
        if (exception.Data[ErrorKey] is CsesScheduleError value)
        {
            error = value;
            argument = exception.Data[ArgumentKey];
            return true;
        }

        error = default;
        argument = null;
        return false;
    }

    private static string Describe(CsesScheduleError error, object? argument) => error switch
    {
        CsesScheduleError.Empty => "课表内容为空。",
        CsesScheduleError.RootNotObject => "课表根节点不是对象。",
        CsesScheduleError.NoValidItems => "课表不包含任何有效课程。",
        CsesScheduleError.InvalidItem => "课表包含无效课程项。",
        CsesScheduleError.InvalidTime => $"课程时间无效：{argument}。",
        CsesScheduleError.OverlappingItems => $"课程时间重叠：{argument}。",
        _ => "课表数据无效。"
    };
}
