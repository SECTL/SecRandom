using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Draw;

public sealed class DrawTemporaryRecordService(ILogger<DrawTemporaryRecordService> logger) : IDrawTemporaryRecordService
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
            if (!_clearedStudentLists.Add(key))
                return;

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
            if (!_clearedPrizeLists.Add(key))
                return;

            ClearPrizeList(listName);
        }
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            var directory = Utils.GetDirectoryPath("TEMP");
            if (!Directory.Exists(directory))
                return;

            foreach (var file in Directory.GetFiles(directory, "roll_call_record_*.json")
                         .Concat(Directory.GetFiles(directory, "roll_call_record__*.json"))
                         .Concat(Directory.GetFiles(directory, "lottery_record_*.json")))
                File.Delete(file);
        }
    }

    private TemporaryRecordState LoadStudentState(string listName)
    {
        var path = GetStudentPath(listName);
        if (!File.Exists(path))
            return new TemporaryRecordState { ListName = listName };

        try
        {
            return JsonSerializer.Deserialize<TemporaryRecordState>(File.ReadAllText(path), JsonOptions)
                   ?? new TemporaryRecordState { ListName = listName };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取临时抽取记录失败，将使用空记录：{Path}", path);
            return new TemporaryRecordState { ListName = listName };
        }
    }

    private static void SaveStudentState(string listName, TemporaryRecordState state)
    {
        var path = GetStudentPath(listName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
    }

    private TemporaryRecordState LoadPrizeState(string listName)
    {
        var path = GetPrizePath(listName);
        if (!File.Exists(path))
            return new TemporaryRecordState { ListName = listName };

        try
        {
            return JsonSerializer.Deserialize<TemporaryRecordState>(File.ReadAllText(path), JsonOptions)
                   ?? new TemporaryRecordState { ListName = listName };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取临时抽奖记录失败，将使用空记录：{Path}", path);
            return new TemporaryRecordState { ListName = listName };
        }
    }

    private static void SavePrizeState(string listName, TemporaryRecordState state)
    {
        var path = GetPrizePath(listName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
    }

    private static string GetStudentPath(string listName)
    {
        return Utils.GetFilePath("TEMP", $"roll_call_record_{NormalizeFileComponent(listName)}.json");
    }

    private static string GetPrizePath(string listName)
    {
        return Utils.GetFilePath("TEMP", $"lottery_record_{NormalizeFileComponent(listName)}.json");
    }

    private static string BuildScopeKey(string gender, string group)
    {
        return $"gender={NormalizeScopeValue(gender)}|group={NormalizeScopeValue(group)}";
    }

    private static string NormalizeScopeValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "*" : value.Trim();
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
