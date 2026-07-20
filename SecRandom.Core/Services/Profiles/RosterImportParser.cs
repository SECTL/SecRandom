using System.Globalization;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Profiles;

/// <summary>
/// 表格名单导入的纯解析逻辑：列映射打分、行字典到 Student/Prize 的转换、重名检测与改名。
/// 视图只负责 MiniExcel 读取和交互；移动端接入表格解析后可直接复用。
/// </summary>
public static class RosterImportParser
{
    public static string[] SplitKeywords(string keywords)
    {
        return keywords.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static string? FindBestColumn(IEnumerable<string> columns, IReadOnlyList<string> keywords)
    {
        var bestScore = 0;
        string? bestColumn = null;

        foreach (var column in columns)
        {
            var normalizedColumn = column.ToLowerInvariant();
            for (var i = 0; i < keywords.Count; i++)
            {
                var keyword = keywords[i].ToLowerInvariant();
                var score = normalizedColumn == keyword ? 100 - i : normalizedColumn.Contains(keyword) ? 50 - i : 0;
                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestColumn = column;
            }
        }

        return bestColumn;
    }

    public static string GetValue(IReadOnlyDictionary<string, string> row, string? column)
    {
        return string.IsNullOrWhiteSpace(column)
            ? string.Empty
            : row.GetValueOrDefault(column, string.Empty).Trim();
    }

    public static IReadOnlyList<string> SplitTags(string rawTags)
    {
        if (string.IsNullOrWhiteSpace(rawTags))
            return [];

        foreach (var separator in new[] { '，', ',', '；', ';', '|', '/', '\\', '\n', '\t' })
            rawTags = rawTags.Replace(separator, ' ');

        return rawTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// 行字典 + 列映射 → 候选学生列表；Id/Name 双空白行由 IsCandidate 过滤剔除。
    /// </summary>
    public static RosterImportResult<Student> ParseStudents(
        IEnumerable<IReadOnlyDictionary<string, string>> rows,
        StudentRosterColumnMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(mapping);

        var items = rows
            .Select(row => new Student
            {
                Id = GetValue(row, mapping.Id),
                Name = GetValue(row, mapping.Name),
                Gender = GetValue(row, mapping.Gender),
                Group = GetValue(row, mapping.Group),
                Tags = string.Join(' ', SplitTags(GetValue(row, mapping.Tags))),
                Exists = true
            })
            .Where(student => student.IsCandidate)
            .ToList();

        return new RosterImportResult<Student>(items, FindDuplicatedNames(items, student => student.Name));
    }

    public static RosterImportResult<Prize> ParsePrizes(
        IEnumerable<IReadOnlyDictionary<string, string>> rows,
        PrizeRosterColumnMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(mapping);

        var items = rows
            .Select(row => new Prize
            {
                Id = GetValue(row, mapping.Id),
                Name = GetValue(row, mapping.Name),
                Weight = ParseDouble(GetValue(row, mapping.Weight), 1),
                Count = Math.Max(0, ParseInt(GetValue(row, mapping.Count), 1)),
                Tags = string.Join(' ', SplitTags(GetValue(row, mapping.Tags))),
                Exists = true
            })
            .Where(prize => prize.IsCandidate)
            .ToList();

        return new RosterImportResult<Prize>(items, FindDuplicatedNames(items, prize => prize.Name));
    }

    public static void RenameDuplicatedStudents(IEnumerable<Student> students)
    {
        RenameDuplicates(students, student => student.Name, (student, name) => student.Name = name);
    }

    public static void RenameDuplicatedPrizes(IEnumerable<Prize> prizes)
    {
        RenameDuplicates(prizes, prize => prize.Name, (prize, name) => prize.Name = name);
    }

    private static IReadOnlyList<string> FindDuplicatedNames<T>(IEnumerable<T> items, Func<T, string> getName)
    {
        return items
            .Select(getName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(name => name)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
    }

    private static void RenameDuplicates<T>(IEnumerable<T> items, Func<T, string> getName, Action<T, string> setName)
    {
        var counts = new Dictionary<string, int>();

        foreach (var item in items)
        {
            var baseName = getName(item);
            counts.TryAdd(baseName, 0);
            counts[baseName]++;
            if (counts[baseName] > 1)
                setName(item, $"{baseName} ({counts[baseName]})");
        }
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var result)
            ? result
            : fallback;
    }

    private static double ParseDouble(string value, double fallback)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var result)
            ? result
            : fallback;
    }
}

public sealed record StudentRosterColumnMapping(string? Id, string? Name, string? Gender, string? Group, string? Tags);

public sealed record PrizeRosterColumnMapping(string? Id, string? Name, string? Weight, string? Count, string? Tags);

public sealed class RosterImportResult<T>(List<T> items, IReadOnlyList<string> duplicatedNames)
{
    public List<T> Items { get; } = items;
    public IReadOnlyList<string> DuplicatedNames { get; } = duplicatedNames;
}
