using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Draw;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services;

public static partial class CoreRuntimeServiceCollectionExtensions
{
    private sealed class DrawTemporaryRecordService(ILogger<DrawTemporaryRecordService> logger) : IDrawTemporaryRecordService, IDrawTemporaryRecordCompensation
    {
    private const string PrizeScopeKey = "prizes";
    // 学生临时记录按历史记录组分桶：抽中次数是"某一轮/某个用途"内的计数，换组不应该互相影响。
    // null（抽取时完全不按组）= 所有组共用的桶；""（默认记录组）= 默认组的桶；其余就是组名本身。
    // 两个下划线包围的键是 HistoryGroupName 的保留形式，真实组名不会和它们相撞。
    private const string AllGroupsScopeKey = "__all__";
    private const string DefaultGroupScopeKey = "__default__";
    // 分桶格式版本：旧文件按 (性别, 分组) 过滤条件分桶（更早）或只有一个 "*" 桶（#294 之后），都没有这个字段。
    private const int StudentStateFormatVersion = 2;
    private readonly HashSet<string> _clearedStudentLists = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _clearedPrizeLists = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public IReadOnlyDictionary<string, int> GetStudentCounts(string listName, string? historyGroup)
    {
        lock (_gate)
        {
            var state = LoadStudentState(listName);
            return state.Scopes.TryGetValue(ToScopeKey(historyGroup), out var scope)
                ? scope.Records.ToDictionary(pair => pair.Key, pair => pair.Value.Count)
                : new Dictionary<string, int>();
        }
    }

    public IReadOnlyDictionary<string, int> GetStudentCountsForList(string listName)
    {
        lock (_gate)
        {
            var state = LoadStudentState(listName);
            var counts = new Dictionary<string, int>();
            foreach (var scope in state.Scopes.Values)
            foreach (var (recordId, record) in scope.Records)
            {
                var current = counts.GetValueOrDefault(recordId);
                counts[recordId] = current > int.MaxValue - record.Count ? int.MaxValue : current + record.Count;
            }

            return counts;
        }
    }

    public void RecordStudents(string listName, string? historyGroup, IEnumerable<Student> students)
    {
        lock (_gate)
        {
            var state = LoadStudentState(listName);
            var scopeKey = ToScopeKey(historyGroup);
            if (!state.Scopes.TryGetValue(scopeKey, out var scope))
            {
                scope = new TemporaryRecordScope();
                state.Scopes[scopeKey] = scope;
            }

            var now = DateTimeOffset.Now;
            foreach (var student in students)
            {
                var recordId = ProfileRecordIdentity.EnsureRecordId(student);
                if (!scope.Records.TryGetValue(recordId, out var record))
                {
                    record = new TemporaryRecordItem();
                    scope.Records[recordId] = record;
                }

                record.Name = student.Name;
                record.Id = student.Id;
                record.Count++;
                record.LastDrawnTime = now;
            }

            state.UpdatedAt = now;
            SaveStudentState(listName, state);
        }
    }

    public void ClearStudentScope(string listName, string? historyGroup)
    {
        lock (_gate)
        {
            var state = LoadStudentState(listName);
            if (!state.Scopes.Remove(ToScopeKey(historyGroup)))
                return;

            state.UpdatedAt = DateTimeOffset.Now;
            SaveStudentState(listName, state);
        }
    }

    public void RenameStudentScope(string listName, string from, string to)
    {
        lock (_gate)
        {
            var state = LoadStudentState(listName);
            var fromKey = ToScopeKey(from);
            var toKey = ToScopeKey(to);
            if (string.Equals(fromKey, toKey, StringComparison.Ordinal))
                return;
            if (!state.Scopes.TryGetValue(fromKey, out var source))
                return;

            state.Scopes.Remove(fromKey);
            if (state.Scopes.TryGetValue(toKey, out var target))
                MergeScopeInto(target, source);
            else
                state.Scopes[toKey] = source;

            state.UpdatedAt = DateTimeOffset.Now;
            SaveStudentState(listName, state);
        }
    }

    public void ResetStudentList(string listName)
    {
        lock (_gate)
        {
            SaveStudentState(listName, new TemporaryRecordState
            {
                ListName = listName,
                FormatVersion = StudentStateFormatVersion,
                UpdatedAt = DateTimeOffset.Now
            });
        }
    }

    public void ClearStudentList(string listName)
    {
        lock (_gate)
        {
            var path = GetStudentPath(listName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public void ClearStudentListOnce(string listName)
    {
        lock (_gate)
        {
            var key = NormalizeFileComponent(listName);
            if (_clearedStudentLists.Add(key))
                ClearStudentList(listName);
        }
    }

    public IReadOnlyDictionary<string, int> GetPrizeCounts(string listName)
    {
        lock (_gate)
        {
            var state = LoadPrizeState(listName);
            return state.Scopes.TryGetValue(PrizeScopeKey, out var scope)
                ? scope.Records.ToDictionary(pair => pair.Key, pair => pair.Value.Count)
                : new Dictionary<string, int>();
        }
    }

    public void RecordPrizes(string listName, IEnumerable<Prize> prizes)
    {
        lock (_gate)
        {
            var state = LoadPrizeState(listName);
            if (!state.Scopes.TryGetValue(PrizeScopeKey, out var scope))
            {
                scope = new TemporaryRecordScope();
                state.Scopes[PrizeScopeKey] = scope;
            }

            var now = DateTimeOffset.Now;
            foreach (var prize in prizes)
            {
                var recordId = ProfileRecordIdentity.EnsureRecordId(prize);
                if (!scope.Records.TryGetValue(recordId, out var record))
                {
                    record = new TemporaryRecordItem();
                    scope.Records[recordId] = record;
                }

                record.Name = prize.Name;
                record.Id = prize.Id;
                record.Count++;
                record.LastDrawnTime = now;
            }

            state.UpdatedAt = now;
            SavePrizeState(listName, state);
        }
    }

    public void ResetPrizeList(string listName)
    {
        lock (_gate)
        {
            SavePrizeState(listName, new TemporaryRecordState
            {
                ListName = listName,
                FormatVersion = StudentStateFormatVersion,
                UpdatedAt = DateTimeOffset.Now
            });
        }
    }

    public void ClearPrizeList(string listName)
    {
        lock (_gate)
        {
            var path = GetPrizePath(listName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public void ClearPrizeListOnce(string listName)
    {
        lock (_gate)
        {
            var key = NormalizeFileComponent(listName);
            if (_clearedPrizeLists.Add(key))
                ClearPrizeList(listName);
        }
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            var directory = Utils.GetDirectoryPath("TEMP");
            foreach (var file in Directory.GetFiles(directory, "roll_call_record_*.json")
                         .Concat(Directory.GetFiles(directory, "roll_call_record__*.json"))
                         .Concat(Directory.GetFiles(directory, "lottery_record_*.json")))
                File.Delete(file);
        }
    }

    private TemporaryRecordState LoadStudentState(string listName)
    {
        var state = LoadState(GetStudentPath(listName), listName, "读取临时抽取记录失败，将使用空记录：{Path}");
        if (MigrateStudentScopes(state))
            SaveStudentState(listName, state);
        return state;
    }

    private TemporaryRecordState LoadPrizeState(string listName) => LoadState(GetPrizePath(listName), listName, "读取临时抽奖记录失败，将使用空记录：{Path}");
    private static void SaveStudentState(string listName, TemporaryRecordState state) => SaveState(GetStudentPath(listName), state);
    private static void SavePrizeState(string listName, TemporaryRecordState state) => SaveState(GetPrizePath(listName), state);

    private TemporaryRecordState LoadState(string path, string listName, string failureMessage)
    {
        if (!File.Exists(path))
            return CreateState(listName);

        try
        {
            return JsonSerializer.Deserialize<TemporaryRecordState>(File.ReadAllText(path), JsonOptions)
                   ?? CreateState(listName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, failureMessage, path);
            return CreateState(listName);
        }
    }

    private static TemporaryRecordState CreateState(string listName) => new()
    {
        ListName = listName,
        FormatVersion = StudentStateFormatVersion
    };

    private static void SaveState(string path, TemporaryRecordState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // 写临时文件再原子替换，避免进程中途被杀留下截断 JSON。
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    public string? CaptureStudentScopeSnapshot(string listName, string? historyGroup)
    {
        lock (_gate)
        {
            var state = LoadStudentState(listName);
            return state.Scopes.TryGetValue(ToScopeKey(historyGroup), out var scope)
                ? JsonSerializer.Serialize(scope, JsonOptions)
                : null;
        }
    }

    public void RestoreStudentScopeSnapshot(string listName, string? historyGroup, string? snapshotJson)
    {
        lock (_gate)
        {
            var state = LoadStudentState(listName);
            var scopeKey = ToScopeKey(historyGroup);
            if (snapshotJson is null)
                state.Scopes.Remove(scopeKey);
            else
                state.Scopes[scopeKey] = JsonSerializer.Deserialize<TemporaryRecordScope>(snapshotJson, JsonOptions)
                                         ?? new TemporaryRecordScope();
            state.UpdatedAt = DateTimeOffset.Now;
            SaveStudentState(listName, state);
        }
    }

    public string? CapturePrizeScopeSnapshot(string listName)
    {
        lock (_gate)
        {
            var state = LoadPrizeState(listName);
            return state.Scopes.TryGetValue(PrizeScopeKey, out var scope)
                ? JsonSerializer.Serialize(scope, JsonOptions)
                : null;
        }
    }

    public void RestorePrizeScopeSnapshot(string listName, string? snapshotJson)
    {
        lock (_gate)
        {
            var state = LoadPrizeState(listName);
            if (snapshotJson is null)
                state.Scopes.Remove(PrizeScopeKey);
            else
                state.Scopes[PrizeScopeKey] = JsonSerializer.Deserialize<TemporaryRecordScope>(snapshotJson, JsonOptions)
                                              ?? new TemporaryRecordScope();
            state.UpdatedAt = DateTimeOffset.Now;
            SavePrizeState(listName, state);
        }
    }

    private static string GetStudentPath(string listName) =>
        Utils.GetFilePath("TEMP", $"roll_call_record_{NormalizeFileComponent(listName)}.json");

    private static string GetPrizePath(string listName) =>
        Utils.GetFilePath("TEMP", $"lottery_record_{NormalizeFileComponent(listName)}.json");

    /// <summary>
    ///     Turns the scope value the draw ran with into its bucket key: <c>null</c> (no group scope at all) shares
    ///     one bucket, the empty string is the default record group, and a name is that group.
    /// </summary>
    private static string ToScopeKey(string? historyGroup)
    {
        if (historyGroup is null)
            return AllGroupsScopeKey;
        var name = historyGroup.Trim();
        return name.Length == 0 ? DefaultGroupScopeKey : name;
    }

    /// <summary>
    ///     Legacy files had no format version: the oldest ones bucketed by the (gender, group) draw filter, and the
    ///     #294 build kept a single <c>"*"</c> bucket, so no count can be attributed to a record group. Those counts
    ///     are summed into the all-groups bucket once, so an upgraded install keeps every recorded draw instead of
    ///     silently restarting the repeat limit; files written by this version are left exactly as they are.
    /// </summary>
    private static bool MigrateStudentScopes(TemporaryRecordState state)
    {
        if (state.FormatVersion >= StudentStateFormatVersion)
            return false;

        var merged = new TemporaryRecordScope();
        foreach (var scope in state.Scopes.Values)
            MergeScopeInto(merged, scope);

        state.Scopes.Clear();
        state.Scopes[AllGroupsScopeKey] = merged;
        state.FormatVersion = StudentStateFormatVersion;
        return true;
    }

    /// <summary>Adds one bucket's counts into another, keeping the newest name/id/time per record.</summary>
    private static void MergeScopeInto(TemporaryRecordScope target, TemporaryRecordScope source)
    {
        foreach (var (recordId, record) in source.Records)
        {
            if (!target.Records.TryGetValue(recordId, out var existing))
            {
                target.Records[recordId] = new TemporaryRecordItem
                {
                    Name = record.Name,
                    Id = record.Id,
                    Count = record.Count,
                    LastDrawnTime = record.LastDrawnTime
                };
                continue;
            }

            existing.Count = existing.Count > int.MaxValue - record.Count
                ? int.MaxValue
                : existing.Count + record.Count;
            if (record.LastDrawnTime > existing.LastDrawnTime)
            {
                existing.LastDrawnTime = record.LastDrawnTime;
                existing.Name = record.Name;
                existing.Id = record.Id;
            }
        }
    }

    private static string NormalizeFileComponent(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            text = text.Replace(invalid, '_');
        return text.Replace(' ', '_');
    }

    private sealed class TemporaryRecordState
    {
        public string ListName { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
        /// <summary>Student bucket-layout version; 0 means a file written before record groups split the buckets.</summary>
        public int FormatVersion { get; set; }
        public Dictionary<string, TemporaryRecordScope> Scopes { get; set; } = [];
    }

    private sealed class TemporaryRecordScope
    {
        public Dictionary<string, TemporaryRecordItem> Records { get; set; } = [];
    }

    private sealed class TemporaryRecordItem
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public int Count { get; set; }
        public DateTimeOffset LastDrawnTime { get; set; }
    }
    }
}
