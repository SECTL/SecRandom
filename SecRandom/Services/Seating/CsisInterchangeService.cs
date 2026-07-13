using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Seating;

public sealed class CsisInterchangeService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public CsisStudentListImport ReadStudentLists(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        EnsureVersion(root);
        if (!root.TryGetProperty("classes", out var classes) || classes.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("CSLS 文件缺少 classes 数组。");

        var result = new List<CsisClassImport>();
        foreach (var item in classes.EnumerateArray())
        {
            var name = RequiredString(item, "name");
            _ = RequiredInteger(item, "class");
            _ = RequiredInteger(item, "grade");
            var students = new List<Student>();
            if (item.TryGetProperty("students", out var studentsElement) && studentsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var studentElement in studentsElement.EnumerateArray())
                {
                    var externalId = RequiredInteger(studentElement, "id").ToString();
                    var student = new Student
                    {
                        // CSPS uses CSLS student.id as its join key. Keep it intact so a CSLS/CSPS pair remains interoperable.
                        Id = externalId,
                        Name = RequiredString(studentElement, "name"),
                        Gender = OptionalString(studentElement, "gender"),
                        Group = OptionalString(studentElement, "group"),
                        Tags = ReadTags(studentElement),
                        Exists = true
                    };
                    ProfileRecordIdentity.EnsureRecordId(student);
                    students.Add(student);
                }
            }
            result.Add(new CsisClassImport(name, students));
        }
        return new CsisStudentListImport(result);
    }

    public CsisSeatingChartImport ReadSeatingChart(string json, IReadOnlyCollection<Student> students)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        EnsureVersion(root);
        RejectExtendedPlacement(root);
        if (!root.TryGetProperty("students", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("CSPS 文件缺少 students 数组。");

        var chart = new SeatingChart { Name = "导入的座位表" };
        chart.IsDeskmateLayout = root.TryGetProperty("deskmate", out var deskmate) && deskmate.ValueKind == JsonValueKind.True;
        chart.Rotation = ReadRotation(root);
        var unmatched = new List<CsisPlacementIssue>();
        var coordinates = new HashSet<(int Row, int Column)>();
        var maxRow = 0;
        var maxColumn = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            var id = RequiredInteger(entry, "id").ToString();
            var name = RequiredString(entry, "name");
            if (!entry.TryGetProperty("position", out var position) || position.ValueKind != JsonValueKind.Array || position.GetArrayLength() != 2 ||
                position[0].ValueKind != JsonValueKind.Number || position[1].ValueKind != JsonValueKind.Number)
                throw new InvalidDataException("CSPS 学生必须包含两个整数的 position 坐标。");

            var sourceColumn = position[0].GetInt32();
            var row = position[1].GetInt32();
            var deskmatePosition = chart.IsDeskmateLayout ? RequiredDeskmatePosition(entry) : null;
            var column = chart.IsDeskmateLayout ? sourceColumn * 2 + (deskmatePosition == "right" ? 1 : 0) : sourceColumn;
            if (row < 0 || sourceColumn < 0 || !coordinates.Add((row, column)))
                throw new InvalidDataException("CSPS 包含无效或重复的座位坐标。");

            maxRow = Math.Max(maxRow, row);
            maxColumn = Math.Max(maxColumn, column);
            var matches = students.Where(student => string.Equals(student.Id, id, StringComparison.Ordinal) &&
                                                    string.Equals(student.Name, name, StringComparison.Ordinal)).ToList();
            if (matches.Count != 1)
            {
                unmatched.Add(new CsisPlacementIssue(id, name, row, column, matches.Count > 1 ? "匹配到多个学生" : "名单中未找到学生"));
                continue;
            }

            chart.Seats.Add(new SeatingChartSeat
            {
                Row = row,
                Column = column,
                StudentRecordId = ProfileRecordIdentity.EnsureRecordId(matches[0]),
                DeskmatePosition = deskmatePosition
            });
        }
        chart.Rows = Math.Max(1, maxRow + 1);
        chart.Columns = Math.Max(1, maxColumn + 1);
        return new CsisSeatingChartImport(chart, unmatched);
    }

    public string WriteStudentLists(IReadOnlyCollection<(string Name, IReadOnlyCollection<Student> Students)> classes)
    {
        var payload = new
        {
            version = 1,
            classes = classes.Select((item, index) => new
            {
                name = item.Name,
                @class = index + 1,
                grade = 0,
                students = item.Students.Select((student, studentIndex) => new
                {
                    id = GetExportId(student),
                    name = student.Name,
                    group = EmptyToNull(student.Group),
                    gender = EmptyToNull(student.Gender),
                    number = int.TryParse(student.Id, out var number) ? number : (int?)null,
                    tags = SplitTags(student.Tags)
                })
            })
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public string WriteSeatingChart(SeatingChart chart, IReadOnlyCollection<Student> students)
    {
        var byRecordId = students.ToDictionary(student => ProfileRecordIdentity.EnsureRecordId(student).ToString());
        var placements = chart.Seats.Where(seat => !seat.IsDisabled && !string.IsNullOrWhiteSpace(seat.StudentRecordId))
            .Select(seat =>
            {
                if (!Guid.TryParse(seat.StudentRecordId, out _) || !byRecordId.TryGetValue(seat.StudentRecordId, out var student))
                    throw new InvalidDataException("座位表包含无法解析的学生关联。");
                return new
                {
                    id = GetExportId(student),
                    name = student.Name,
                    gender = EmptyToNull(student.Gender),
                    deskmate_pos = chart.IsDeskmateLayout ? seat.DeskmatePosition ?? (seat.Column % 2 == 0 ? "left" : "right") : null,
                    position = new[] { chart.IsDeskmateLayout ? seat.Column / 2 : seat.Column, seat.Row }
                };
            });
        return JsonSerializer.Serialize(new
        {
            version = 1,
            rotation = new
            {
                enabled = chart.Rotation.Enabled,
                to_left = chart.Rotation.ToLeft,
                cycle_days = chart.Rotation.CycleDays,
                cycle_in_columns = chart.Rotation.CycleInColumns
            },
            deskmate = chart.IsDeskmateLayout,
            students = placements
        }, JsonOptions);
    }

    private static void EnsureVersion(JsonElement root)
    {
        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1)
            throw new InvalidDataException("仅支持 CSIS API 版本 1。");
    }

    private static void RejectExtendedPlacement(JsonElement root)
    {
        if (root.TryGetProperty("students", out var students) && students.ValueKind == JsonValueKind.Array &&
            students.EnumerateArray().Any(student => student.TryGetProperty("height", out _) || student.TryGetProperty("ruleset", out _) || student.TryGetProperty("ext", out _) || student.TryGetProperty("deskmate_pos", out _)))
            throw new InvalidDataException("暂不支持 ESPS 扩展座位字段。");
    }

    private static SeatingChartRotation ReadRotation(JsonElement root)
    {
        if (!root.TryGetProperty("rotation", out var rotation) || rotation.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("CSPS 文件缺少 rotation 配置。");
        if (!rotation.TryGetProperty("enabled", out var enabled) || (enabled.ValueKind != JsonValueKind.True && enabled.ValueKind != JsonValueKind.False))
            throw new InvalidDataException("CSPS rotation.enabled 必须是布尔值。");
        if (enabled.ValueKind == JsonValueKind.False)
            return new SeatingChartRotation();
        return new SeatingChartRotation
        {
            Enabled = true,
            ToLeft = RequiredBoolean(rotation, "to_left"),
            CycleDays = RequiredInteger(rotation, "cycle_days"),
            CycleInColumns = RequiredBoolean(rotation, "cycle_in_columns")
        };
    }

    private static bool RequiredBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : throw new InvalidDataException($"CSIS 字段 {property} 必须是布尔值。");

    private static string RequiredDeskmatePosition(JsonElement element)
    {
        var value = RequiredString(element, "deskmate_pos").ToLowerInvariant();
        return value is "left" or "right" ? value : throw new InvalidDataException("CSPS deskmate_pos 必须为 left 或 right。");
    }

    private static string RequiredString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : throw new InvalidDataException($"CSIS 字段 {property} 为必填项。");

    private static int RequiredInteger(JsonElement element, string property) =>
        TryGetInteger(element, property) ?? throw new InvalidDataException($"CSIS 字段 {property} 必须是整数。");

    private static int? TryGetInteger(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static string OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : string.Empty;

    private static string ReadTags(JsonElement element) =>
        element.TryGetProperty("tags", out var value) && value.ValueKind == JsonValueKind.Array
            ? string.Join(' ', value.EnumerateArray().Where(tag => tag.ValueKind == JsonValueKind.String).Select(tag => tag.GetString()).Where(tag => !string.IsNullOrWhiteSpace(tag)))
            : string.Empty;

    private static string[] SplitTags(string tags) => tags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static int GetExportId(Student student) =>
        int.TryParse(student.Id, out var id) && id > 0
            ? id
            : throw new InvalidDataException("CSIS 导出要求每个学生具有正整数的学号或序号。");
}

public sealed record CsisClassImport(string Name, IReadOnlyList<Student> Students);
public sealed record CsisStudentListImport(IReadOnlyList<CsisClassImport> Classes);
public sealed record CsisPlacementIssue(string Id, string Name, int Row, int Column, string Reason);
public sealed record CsisSeatingChartImport(SeatingChart Chart, IReadOnlyList<CsisPlacementIssue> Issues);
