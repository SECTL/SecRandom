using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Shared;

namespace SecRandom.Core.Services.Stats;

/// <summary>One committed number-range draw kept for the overview and the random-number settings page.</summary>
public sealed class RandomNumberDrawRecord
{
    public DateTime DrawTime { get; set; }
    public string Source { get; set; } = string.Empty;
    public int RangeStart { get; set; }
    public int RangeEnd { get; set; }
    public int Count { get; set; }
    public List<int> Numbers { get; set; } = [];
}

/// <summary>Lifetime draw count for one configured number range.</summary>
public sealed class RandomNumberRangeStatistics
{
    public int RangeStart { get; set; }
    public int RangeEnd { get; set; }
    public int DrawCount { get; set; }
    public DateTime? LastDrawAt { get; set; }
}

/// <summary>Persisted random-number statistics document.</summary>
public sealed class RandomNumberStatistics
{
    public const string CurrentFormat = "SecRandomRandomNumberStats/v1";
    public const int MaxRecentRecords = 100;

    public string Format { get; set; } = CurrentFormat;
    public int TotalRounds { get; set; }
    public int TotalDrawnCount { get; set; }
    public DateTime? LastDrawAt { get; set; }
    public List<RandomNumberRangeStatistics> Ranges { get; set; } = [];
    public List<RandomNumberDrawRecord> Recent { get; set; } = [];
}

/// <summary>Immutable view of the random-number statistics for presentation code.</summary>
public sealed record RandomNumberStatisticsSnapshot(
    int TotalRounds,
    int TotalDrawnCount,
    DateTime? LastDrawAt,
    IReadOnlyList<RandomNumberRangeStatistics> Ranges,
    IReadOnlyList<RandomNumberDrawRecord> Recent);

/// <summary>
///     Legacy standalone random-number statistics store kept for file-format compatibility and isolated tests.
///     Production registration uses <see cref="OverviewStatisticsService"/>, which stores this section in
///     <c>data/config/overview.json</c> alongside roll-call and lottery aggregates.
/// </summary>
public interface IRandomNumberStatisticsService
{
    RandomNumberStatisticsSnapshot Load();
    void Record(string source, int rangeStart, int rangeEnd, IReadOnlyList<int> numbers);
    void Clear();
    event EventHandler? Changed;
}

/// <summary>
///     Atomic JSON store for <see cref="RandomNumberStatistics" />. A draw is best-effort: a statistics write
///     failure must never surface to the draw that has already committed.
/// </summary>
public sealed class RandomNumberStatisticsService : IRandomNumberStatisticsService
{
    private readonly ILogger<RandomNumberStatisticsService> _logger;
    private readonly string _filePath;
    private readonly object _gate = new();

    public RandomNumberStatisticsService(ILogger<RandomNumberStatisticsService> logger)
        // This constructor is retained for callers that still explicitly create the legacy store. The host does
        // not register this type; its shared overview service is the only production persistence boundary.
        : this(logger, Utils.GetFilePath("config", "random-number-stats.json"),
            Utils.GetFilePath("history", "random-number", "stats.json"))
    {
    }

    /// <summary>Test seam: lets a test point at a temporary file instead of the data root.</summary>
    public RandomNumberStatisticsService(ILogger<RandomNumberStatisticsService> logger, string filePath)
        : this(logger, filePath, legacyFilePath: null)
    {
    }

    private RandomNumberStatisticsService(ILogger<RandomNumberStatisticsService> logger, string filePath,
        string? legacyFilePath)
    {
        _logger = logger;
        _filePath = filePath;
        _legacyFilePath = legacyFilePath;
    }

    public event EventHandler? Changed;

    private readonly string? _legacyFilePath;

    public RandomNumberStatisticsSnapshot Load()
    {
        lock (_gate)
            return ToSnapshot(LoadDocument());
    }

    public void Record(string source, int rangeStart, int rangeEnd, IReadOnlyList<int> numbers)
    {
        if (numbers.Count == 0)
            return;

        lock (_gate)
        {
            try
            {
                var document = LoadDocument();
                var drawTime = DateTime.Now;
                document.TotalRounds++;
                document.TotalDrawnCount += numbers.Count;
                document.LastDrawAt = drawTime;

                var range = document.Ranges.FirstOrDefault(item =>
                    item.RangeStart == rangeStart && item.RangeEnd == rangeEnd);
                if (range is null)
                {
                    range = new RandomNumberRangeStatistics
                    {
                        RangeStart = rangeStart,
                        RangeEnd = rangeEnd
                    };
                    document.Ranges.Add(range);
                }

                range.DrawCount++;
                range.LastDrawAt = drawTime;

                document.Recent.InsertRange(0,
                [
                    new RandomNumberDrawRecord
                    {
                        DrawTime = drawTime,
                        Source = source,
                        RangeStart = rangeStart,
                        RangeEnd = rangeEnd,
                        Count = numbers.Count,
                        Numbers = [.. numbers]
                    }
                ]);
                if (document.Recent.Count > RandomNumberStatistics.MaxRecentRecords)
                    document.Recent.RemoveRange(RandomNumberStatistics.MaxRecentRecords,
                        document.Recent.Count - RandomNumberStatistics.MaxRecentRecords);

                Save(document);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "随机数抽取已提交，但随机数统计写入失败。");
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            try
            {
                Save(new RandomNumberStatistics());
                if (!string.IsNullOrWhiteSpace(_legacyFilePath) && File.Exists(_legacyFilePath))
                    File.Move(_legacyFilePath, _legacyFilePath + ".migrated", overwrite: true);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "清除随机数统计失败。");
            }
        }
    }

    private RandomNumberStatistics LoadDocument()
    {
        if (!File.Exists(_filePath))
        {
            if (string.IsNullOrWhiteSpace(_legacyFilePath) || !File.Exists(_legacyFilePath))
                return new RandomNumberStatistics();

            try
            {
                var legacy = LoadFromPath(_legacyFilePath);
                Save(legacy);
                try
                {
                    File.Move(_legacyFilePath, _legacyFilePath + ".migrated", overwrite: true);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "随机数统计已迁移，但无法改名旧文件：{Path}", _legacyFilePath);
                }
                return legacy;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _logger.LogWarning(exception, "迁移旧版随机数统计失败：{Path}", _legacyFilePath);
                return new RandomNumberStatistics();
            }
        }

        try
        {
            return LoadFromPath(_filePath);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.LogWarning(exception, "随机数统计文件不可读，已隔离并重新开始：{Path}", _filePath);
            TryQuarantine();
            return new RandomNumberStatistics();
        }
    }

    private RandomNumberStatistics LoadFromPath(string path)
    {
        var document = JsonSerializer.Deserialize<RandomNumberStatistics>(
            File.ReadAllText(path), ConfigServiceBase.JsonOptions);
        if (document is null
            || !string.Equals(document.Format, RandomNumberStatistics.CurrentFormat, StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported random-number statistics format.");

        document.TotalRounds = Math.Max(0, document.TotalRounds);
        document.TotalDrawnCount = Math.Max(0, document.TotalDrawnCount);
        document.Recent ??= [];
        document.Recent = document.Recent
            .Where(record => record is not null)
            .Take(RandomNumberStatistics.MaxRecentRecords)
            .ToList();
        document.Ranges = NormalizeRanges(document.Ranges, document.Recent);
        return document;
    }

    private static List<RandomNumberRangeStatistics> NormalizeRanges(
        List<RandomNumberRangeStatistics>? stored,
        IReadOnlyList<RandomNumberDrawRecord> recent)
    {
        var ranges = new Dictionary<(int Start, int End), RandomNumberRangeStatistics>();
        foreach (var item in stored ?? [])
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

            range.DrawCount += item.DrawCount;
            if (item.LastDrawAt is { } lastDrawAt
                && (range.LastDrawAt is null || lastDrawAt > range.LastDrawAt))
                range.LastDrawAt = lastDrawAt;
        }

        // Older documents predate per-range counters; their bounded recent list is the best available summary.
        if (ranges.Count == 0)
        {
            foreach (var record in recent)
            {
                if (record.RangeEnd < record.RangeStart)
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

                range.DrawCount++;
                if (range.LastDrawAt is null || record.DrawTime > range.LastDrawAt)
                    range.LastDrawAt = record.DrawTime;
            }
        }

        return ranges.Values
            .OrderBy(item => item.RangeStart)
            .ThenBy(item => item.RangeEnd)
            .ToList();
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
            _logger.LogWarning(exception, "无法隔离损坏的随机数统计文件：{Path}", _filePath);
        }
    }

    private void Save(RandomNumberStatistics document)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath,
            JsonSerializer.Serialize(document, ConfigServiceBase.JsonOptions));
        File.Move(temporaryPath, _filePath, overwrite: true);
    }

    private static RandomNumberStatisticsSnapshot ToSnapshot(RandomNumberStatistics document) =>
        new(
            document.TotalRounds,
            document.TotalDrawnCount,
            document.LastDrawAt,
            document.Ranges.Select(Clone).ToArray(),
            document.Recent.Select(Clone).ToArray());

    private static RandomNumberRangeStatistics Clone(RandomNumberRangeStatistics range) =>
        new()
        {
            RangeStart = range.RangeStart,
            RangeEnd = range.RangeEnd,
            DrawCount = range.DrawCount,
            LastDrawAt = range.LastDrawAt
        };

    private static RandomNumberDrawRecord Clone(RandomNumberDrawRecord record) =>
        new()
        {
            DrawTime = record.DrawTime,
            Source = record.Source,
            RangeStart = record.RangeStart,
            RangeEnd = record.RangeEnd,
            Count = record.Count,
            Numbers = [.. record.Numbers]
        };
}
