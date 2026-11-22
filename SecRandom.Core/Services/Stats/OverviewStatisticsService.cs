using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.HistoryStore;
using SecRandom.Shared;

namespace SecRandom.Core.Services.Stats;

/// <summary>Aggregated draw data shown by the overview page.</summary>
public sealed class OverviewStatistics
{
    public const string CurrentFormat = "SecRandomOverviewStats/v1";

    public string Format { get; set; } = CurrentFormat;
    public bool LegacyHistoryMigrated { get; set; }
    public bool LegacyRandomNumberMigrated { get; set; }
    public Dictionary<string, OverviewProfileStatistics> RollCall { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, OverviewProfileStatistics> Lottery { get; set; } = new(StringComparer.Ordinal);
    public RandomNumberStatistics RandomNumbers { get; set; } = new();
}

public sealed class OverviewProfileStatistics
{
    public int TotalRounds { get; set; }
    public int TotalDrawnCount { get; set; }
    public DateTime? LastDrawAt { get; set; }
}

public sealed record OverviewProfileStatisticsSnapshot(
    int TotalRounds,
    int TotalDrawnCount,
    DateTime? LastDrawAt);

public sealed record OverviewStatisticsSnapshot(
    IReadOnlyDictionary<string, OverviewProfileStatisticsSnapshot> RollCall,
    IReadOnlyDictionary<string, OverviewProfileStatisticsSnapshot> Lottery);

/// <summary>
///     Owns the small aggregate document used by the overview page. History records are deliberately not read when
///     the overview is refreshed, so clearing this document never changes a history database.
/// </summary>
public interface IOverviewStatisticsService
{
    OverviewStatisticsSnapshot Load();
    void RecordStudentDraw(string profileName, int count, DateTime drawTime);
    void RecordPrizeDraw(string profileName, int count, DateTime drawTime);
    void Clear();
    void ClearStudentStatistics(string? profileName = null);
    void ClearPrizeStatistics(string? profileName = null);
    void ClearRandomNumberStatistics();
    void RenameStudent(string oldName, string newName);
    void RenamePrize(string oldName, string newName);
    void RemoveStudent(string profileName);
    void RemovePrize(string profileName);
    event EventHandler? Changed;
}

/// <summary>Atomic JSON storage for <see cref="OverviewStatistics" /> under <c>data/config</c>.</summary>
public sealed class OverviewStatisticsService : IOverviewStatisticsService, IRandomNumberStatisticsService
{
    private readonly ILogger<OverviewStatisticsService> _logger;
    private readonly string _filePath;
    private readonly object _gate = new();

    public OverviewStatisticsService(ILogger<OverviewStatisticsService> logger, ConfigServiceBase configService)
        : this(logger, Utils.GetFilePath("config", "overview.json"))
    {
        MigrateLegacyHistory(configService);
    }

    /// <summary>Test seam for an isolated overview document.</summary>
    public OverviewStatisticsService(ILogger<OverviewStatisticsService> logger, string filePath)
    {
        _logger = logger;
        _filePath = filePath;
    }

    public event EventHandler? Changed;

    public OverviewStatisticsSnapshot Load()
    {
        lock (_gate)
        {
            var document = LoadDocument();
            return ToSnapshot(document);
        }
    }

    public void RecordStudentDraw(string profileName, int count, DateTime drawTime) =>
        Record(profileName, count, drawTime, student: true);

    public void RecordPrizeDraw(string profileName, int count, DateTime drawTime) =>
        Record(profileName, count, drawTime, student: false);

    public void Clear()
    {
        lock (_gate)
        {
            Save(new OverviewStatistics
            {
                LegacyHistoryMigrated = true,
                LegacyRandomNumberMigrated = true
            });
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearStudentStatistics(string? profileName = null) =>
        ClearProfileStatistics(profileName, student: true);

    public void ClearPrizeStatistics(string? profileName = null) =>
        ClearProfileStatistics(profileName, student: false);

    public void ClearRandomNumberStatistics()
    {
        lock (_gate)
        {
            try
            {
                var document = LoadDocument();
                document.RandomNumbers = new RandomNumberStatistics();
                document.LegacyRandomNumberMigrated = true;
                Save(document);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "清除随机数概览数据失败。");
            }
        }
    }

    public void RenameStudent(string oldName, string newName) => Rename(oldName, newName, student: true);

    public void RenamePrize(string oldName, string newName) => Rename(oldName, newName, student: false);

    public void RemoveStudent(string profileName) => Remove(profileName, student: true);

    public void RemovePrize(string profileName) => Remove(profileName, student: false);

    RandomNumberStatisticsSnapshot IRandomNumberStatisticsService.Load()
    {
        lock (_gate)
            return ToRandomSnapshot(LoadDocument().RandomNumbers);
    }

    void IRandomNumberStatisticsService.Record(string source, int rangeStart, int rangeEnd,
        IReadOnlyList<int> numbers) => RecordRandomNumberDraw(source, rangeStart, rangeEnd, numbers);

    void IRandomNumberStatisticsService.Clear() => ClearRandomNumberStatistics();

    private void Record(string profileName, int count, DateTime drawTime, bool student)
    {
        if (string.IsNullOrWhiteSpace(profileName) || count <= 0)
            return;

        lock (_gate)
        {
            try
            {
                var document = LoadDocument();
                var values = student ? document.RollCall : document.Lottery;
                if (!values.TryGetValue(profileName, out var statistics))
                {
                    statistics = new OverviewProfileStatistics();
                    values[profileName] = statistics;
                }

                statistics.TotalRounds = SaturatingAdd(statistics.TotalRounds, 1);
                statistics.TotalDrawnCount = SaturatingAdd(statistics.TotalDrawnCount, count);
                if (statistics.LastDrawAt is null || drawTime > statistics.LastDrawAt)
                    statistics.LastDrawAt = drawTime;

                Save(document);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "抽取已提交，但概览数据写入失败：名单={ProfileName}。", profileName);
            }
        }
    }

    private void Rename(string oldName, string newName, bool student)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) ||
            string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

        lock (_gate)
        {
            try
            {
                var document = LoadDocument();
                var values = student ? document.RollCall : document.Lottery;
                if (!values.Remove(oldName, out var statistics))
                    return;

                values[newName] = statistics;
                Save(document);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "名单已处理，但概览数据重命名失败：{ProfileName}", oldName);
            }
        }
    }

    private void Remove(string profileName, bool student)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            return;

        lock (_gate)
        {
            try
            {
                var document = LoadDocument();
                var values = student ? document.RollCall : document.Lottery;
                if (!values.Remove(profileName))
                    return;

                Save(document);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "名单已处理，但概览数据删除失败：{ProfileName}", profileName);
            }
        }
    }

    private void MigrateLegacyHistory(ConfigServiceBase configService)
    {
        lock (_gate)
        {
            var document = LoadDocument();
            var changed = false;
            if (!document.LegacyHistoryMigrated)
            {
                MigrateStudentHistory(document, configService);
                MigratePrizeHistory(document, configService);
                document.LegacyHistoryMigrated = true;
                changed = true;
            }

            if (!document.LegacyRandomNumberMigrated)
            {
                MigrateLegacyRandomNumbers(document);
                document.LegacyRandomNumberMigrated = true;
                changed = true;
            }

            if (changed)
                Save(document);
        }
    }

    private void RecordRandomNumberDraw(string source, int rangeStart, int rangeEnd,
        IReadOnlyList<int> numbers)
    {
        if (numbers.Count == 0)
            return;

        lock (_gate)
        {
            try
            {
                var document = LoadDocument();
                var drawTime = DateTime.Now;
                var randomNumbers = document.RandomNumbers;
                randomNumbers.TotalRounds = SaturatingAdd(randomNumbers.TotalRounds, 1);
                randomNumbers.TotalDrawnCount = SaturatingAdd(randomNumbers.TotalDrawnCount, numbers.Count);
                randomNumbers.LastDrawAt = drawTime;

                var range = randomNumbers.Ranges.FirstOrDefault(item =>
                    item.RangeStart == rangeStart && item.RangeEnd == rangeEnd);
                if (range is null)
                {
                    range = new RandomNumberRangeStatistics
                    {
                        RangeStart = rangeStart,
                        RangeEnd = rangeEnd
                    };
                    randomNumbers.Ranges.Add(range);
                }

                range.DrawCount = SaturatingAdd(range.DrawCount, 1);
                range.LastDrawAt = drawTime;
                randomNumbers.Recent.Insert(0, new RandomNumberDrawRecord
                {
                    DrawTime = drawTime,
                    Source = source,
                    RangeStart = rangeStart,
                    RangeEnd = rangeEnd,
                    Count = numbers.Count,
                    Numbers = [.. numbers]
                });
                if (randomNumbers.Recent.Count > RandomNumberStatistics.MaxRecentRecords)
                    randomNumbers.Recent.RemoveRange(RandomNumberStatistics.MaxRecentRecords,
                        randomNumbers.Recent.Count - RandomNumberStatistics.MaxRecentRecords);

                document.LegacyRandomNumberMigrated = true;
                Save(document);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "随机数抽取已提交，但概览数据写入失败。");
            }
        }
    }

    private void ClearProfileStatistics(string? profileName, bool student)
    {
        lock (_gate)
        {
            try
            {
                var document = LoadDocument();
                var values = student ? document.RollCall : document.Lottery;
                if (string.IsNullOrWhiteSpace(profileName))
                    values.Clear();
                else
                    values.Remove(profileName);

                Save(document);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "清除概览数据失败：{ProfileName}", profileName);
            }
        }
    }

    private void MigrateLegacyRandomNumbers(OverviewStatistics document)
    {
        var legacyPath = Utils.GetFilePath("history", "random-number", "stats.json");
        if (!File.Exists(legacyPath))
            return;

        try
        {
            var legacy = JsonSerializer.Deserialize<RandomNumberStatistics>(
                File.ReadAllText(legacyPath), ConfigServiceBase.JsonOptions);
            if (legacy is not null && string.Equals(legacy.Format, RandomNumberStatistics.CurrentFormat,
                    StringComparison.Ordinal))
            {
                document.RandomNumbers = NormalizeRandomNumbers(legacy);
            }

            File.Move(legacyPath, legacyPath + ".migrated", overwrite: true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.LogWarning(exception, "迁移旧版随机数概览数据失败：{Path}", legacyPath);
        }
    }

    private void MigrateStudentHistory(OverviewStatistics document, ConfigServiceBase configService)
    {
        foreach (var name in ProfileHistoryPaths.EnumerateProfileNames(HistoryGroupKind.RollCall))
        {
            try
            {
                var config = new StudentHistoryConfig(name, _logger, configService);
                var history = config.Data;
                if (history.TotalRounds <= 0 && history.TotalStats <= 0)
                    continue;

                if (!document.RollCall.ContainsKey(name))
                {
                    document.RollCall[name] = new OverviewProfileStatistics
                    {
                        TotalRounds = Math.Max(0, history.TotalRounds),
                        TotalDrawnCount = Math.Max(0, history.TotalStats),
                        LastDrawAt = GetLastDrawAt(history.Students.Values.Select(item => item.LastDrawnTime))
                    };
                }

                history.TotalRounds = 0;
                history.TotalStats = 0;
                config.Save();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "迁移点名概览数据失败：名单={ProfileName}。", name);
            }
        }
    }

    private void MigratePrizeHistory(OverviewStatistics document, ConfigServiceBase configService)
    {
        foreach (var name in ProfileHistoryPaths.EnumerateProfileNames(HistoryGroupKind.Lottery))
        {
            try
            {
                var config = new PrizeHistoryConfig(name, _logger, configService);
                var history = config.Data;
                if (history.TotalRounds <= 0 && history.TotalStats <= 0)
                    continue;

                if (!document.Lottery.ContainsKey(name))
                {
                    document.Lottery[name] = new OverviewProfileStatistics
                    {
                        TotalRounds = Math.Max(0, history.TotalRounds),
                        TotalDrawnCount = Math.Max(0, history.TotalStats),
                        LastDrawAt = GetLastDrawAt(history.Prizes.Values.Select(item => item.LastDrawnTime))
                    };
                }

                history.TotalRounds = 0;
                history.TotalStats = 0;
                config.Save();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "迁移抽奖概览数据失败：奖池={ProfileName}。", name);
            }
        }
    }

    private OverviewStatistics LoadDocument()
    {
        if (!File.Exists(_filePath))
            return new OverviewStatistics();

        try
        {
            var document = JsonSerializer.Deserialize<OverviewStatistics>(
                File.ReadAllText(_filePath), ConfigServiceBase.JsonOptions);
            if (document is null || !string.Equals(document.Format, OverviewStatistics.CurrentFormat, StringComparison.Ordinal))
                throw new InvalidDataException("Unsupported overview statistics format.");

            document.RollCall = Normalize(document.RollCall);
            document.Lottery = Normalize(document.Lottery);
            document.RandomNumbers = NormalizeRandomNumbers(document.RandomNumbers);
            return document;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.LogWarning(exception, "概览数据文件不可读，已隔离并重新开始：{Path}", _filePath);
            TryQuarantine();
            return new OverviewStatistics();
        }
    }

    private static Dictionary<string, OverviewProfileStatistics> Normalize(
        Dictionary<string, OverviewProfileStatistics>? values)
    {
        var normalized = new Dictionary<string, OverviewProfileStatistics>(StringComparer.Ordinal);
        foreach (var pair in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
                continue;

            normalized[pair.Key] = new OverviewProfileStatistics
            {
                TotalRounds = Math.Max(0, pair.Value.TotalRounds),
                TotalDrawnCount = Math.Max(0, pair.Value.TotalDrawnCount),
                LastDrawAt = pair.Value.LastDrawAt
            };
        }

        return normalized;
    }

    private void Save(OverviewStatistics document)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, ConfigServiceBase.JsonOptions));
        File.Move(temporaryPath, _filePath, overwrite: true);
    }

    private void TryQuarantine()
    {
        try
        {
            if (File.Exists(_filePath))
                File.Move(_filePath, _filePath + ".corrupt", overwrite: true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "无法隔离损坏的概览数据文件：{Path}", _filePath);
        }
    }

    private static OverviewStatisticsSnapshot ToSnapshot(OverviewStatistics document) =>
        new(
            document.RollCall.ToDictionary(pair => pair.Key, pair => ToSnapshot(pair.Value), StringComparer.Ordinal),
            document.Lottery.ToDictionary(pair => pair.Key, pair => ToSnapshot(pair.Value), StringComparer.Ordinal));

    private static OverviewProfileStatisticsSnapshot ToSnapshot(OverviewProfileStatistics statistics) =>
        new(statistics.TotalRounds, statistics.TotalDrawnCount, statistics.LastDrawAt);

    private static DateTime? GetLastDrawAt(IEnumerable<DateTime> times)
    {
        var last = times.Where(time => time != DateTime.MinValue).DefaultIfEmpty().Max();
        return last == DateTime.MinValue ? null : last;
    }

    private static RandomNumberStatistics NormalizeRandomNumbers(RandomNumberStatistics? document)
    {
        document ??= new RandomNumberStatistics();
        document.TotalRounds = Math.Max(0, document.TotalRounds);
        document.TotalDrawnCount = Math.Max(0, document.TotalDrawnCount);
        document.Recent ??= [];
        document.Recent = document.Recent
            .Where(record => record is not null)
            .Take(RandomNumberStatistics.MaxRecentRecords)
            .ToList();
        document.Ranges ??= [];

        var ranges = new Dictionary<(int Start, int End), RandomNumberRangeStatistics>();
        foreach (var item in document.Ranges)
        {
            if (item is null || item.RangeEnd < item.RangeStart || item.DrawCount <= 0)
                continue;

            var key = (item.RangeStart, item.RangeEnd);
            if (!ranges.TryGetValue(key, out var range))
            {
                ranges[key] = range = new RandomNumberRangeStatistics
                {
                    RangeStart = item.RangeStart,
                    RangeEnd = item.RangeEnd
                };
            }

            range.DrawCount = SaturatingAdd(range.DrawCount, item.DrawCount);
            if (item.LastDrawAt is { } last && (range.LastDrawAt is null || last > range.LastDrawAt))
                range.LastDrawAt = last;
        }

        if (ranges.Count == 0)
        {
            foreach (var record in document.Recent)
            {
                if (record is null || record.RangeEnd < record.RangeStart)
                    continue;

                var key = (record.RangeStart, record.RangeEnd);
                if (!ranges.TryGetValue(key, out var range))
                {
                    ranges[key] = range = new RandomNumberRangeStatistics
                    {
                        RangeStart = record.RangeStart,
                        RangeEnd = record.RangeEnd
                    };
                }

                range.DrawCount = SaturatingAdd(range.DrawCount, 1);
                if (range.LastDrawAt is null || record.DrawTime > range.LastDrawAt)
                    range.LastDrawAt = record.DrawTime;
            }
        }

        document.Ranges = ranges.Values.OrderBy(item => item.RangeStart).ThenBy(item => item.RangeEnd).ToList();
        return document;
    }

    private static RandomNumberStatisticsSnapshot ToRandomSnapshot(RandomNumberStatistics document) =>
        new(document.TotalRounds, document.TotalDrawnCount, document.LastDrawAt,
            document.Ranges.Select(item => new RandomNumberRangeStatistics
            {
                RangeStart = item.RangeStart,
                RangeEnd = item.RangeEnd,
                DrawCount = item.DrawCount,
                LastDrawAt = item.LastDrawAt
            }).ToArray(),
            document.Recent.Select(item => new RandomNumberDrawRecord
            {
                DrawTime = item.DrawTime,
                Source = item.Source,
                RangeStart = item.RangeStart,
                RangeEnd = item.RangeEnd,
                Count = item.Count,
                Numbers = [.. item.Numbers]
            }).ToArray());

    private static int SaturatingAdd(int value, int increment) =>
        value > int.MaxValue - increment ? int.MaxValue : value + increment;
}
