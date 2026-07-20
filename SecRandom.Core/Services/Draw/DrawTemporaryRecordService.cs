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
    private readonly HashSet<string> _clearedStudentLists = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _clearedPrizeLists = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public IReadOnlyDictionary<string, int> GetStudentCounts(string listName, string gender, string group)
    {
        lock (_gate)
        {
            var state = LoadStudentState(listName);
            return state.Scopes.TryGetValue(BuildScopeKey(gender, group), out var scope)
                ? scope.Records.ToDictionary(pair => pair.Key, pair => pair.Value.Count)
                : new Dictionary<string, int>();
        }
    }

    public void RecordStudents(string listName, string gender, string group, IEnumerable<Student> students)
    {
        lock (_gate)
        {
            var state = LoadStudentState(listName);
            var scopeKey = BuildScopeKey(gender, group);
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

    public void ClearStudentScope(string listName, string gender, string group)
    {
        lock (_gate)
        {
            var state = LoadStudentState(listName);
            if (!state.Scopes.Remove(BuildScopeKey(gender, group)))
                return;

            state.UpdatedAt = DateTimeOffset.Now;
            SaveStudentState(listName, state);
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

    private TemporaryRecordState LoadStudentState(string listName) => LoadState(GetStudentPath(listName), listName, "读取临时抽取记录失败，将使用空记录：{Path}");
    private TemporaryRecordState LoadPrizeState(string listName) => LoadState(GetPrizePath(listName), listName, "读取临时抽奖记录失败，将使用空记录：{Path}");
    private static void SaveStudentState(string listName, TemporaryRecordState state) => SaveState(GetStudentPath(listName), state);
    private static void SavePrizeState(string listName, TemporaryRecordState state) => SaveState(GetPrizePath(listName), state);

    private TemporaryRecordState LoadState(string path, string listName, string failureMessage)
    {
        if (!File.Exists(path))
            return new TemporaryRecordState { ListName = listName };

        try
        {
            return JsonSerializer.Deserialize<TemporaryRecordState>(File.ReadAllText(path), JsonOptions)
                   ?? new TemporaryRecordState { ListName = listName };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, failureMessage, path);
            return new TemporaryRecordState { ListName = listName };
        }
    }

    private static void SaveState(string path, TemporaryRecordState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // 写临时文件再原子替换，避免进程中途被杀留下截断 JSON。
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    public string? CaptureStudentScopeSnapshot(string listName, string gender, string group)
    {
        lock (_gate)
        {
            var state = LoadStudentState(listName);
            return state.Scopes.TryGetValue(BuildScopeKey(gender, group), out var scope)
                ? JsonSerializer.Serialize(scope, JsonOptions)
                : null;
        }
    }

    public void RestoreStudentScopeSnapshot(string listName, string gender, string group, string? snapshotJson)
    {
        lock (_gate)
        {
            var state = LoadStudentState(listName);
            var scopeKey = BuildScopeKey(gender, group);
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

    private static string BuildScopeKey(string gender, string group) =>
        $"gender={NormalizeScopeValue(gender)}|group={NormalizeScopeValue(group)}";

    private static string NormalizeScopeValue(string value) => string.IsNullOrWhiteSpace(value) ? "*" : value.Trim();

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
