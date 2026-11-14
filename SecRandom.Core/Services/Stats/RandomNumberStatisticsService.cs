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

/// <summary>Persisted random-number statistics document.</summary>
public sealed class RandomNumberStatistics
{
    public const string CurrentFormat = "SecRandomRandomNumberStats/v1";
    public const int MaxRecentRecords = 100;

    public string Format { get; set; } = CurrentFormat;
    public int TotalRounds { get; set; }
    public int TotalDrawnCount { get; set; }
    public DateTime? LastDrawAt { get; set; }
    public List<RandomNumberDrawRecord> Recent { get; set; } = [];
}

/// <summary>Immutable view of the random-number statistics for presentation code.</summary>
public sealed record RandomNumberStatisticsSnapshot(
    int TotalRounds,
    int TotalDrawnCount,
    DateTime? LastDrawAt,
    IReadOnlyList<RandomNumberDrawRecord> Recent);

/// <summary>
///     Owns the random-number statistics file. Number draws deliberately never write roster history, so this
///     small document is the only durable record of them; it is also the overview page's only data source for
///     that mode.
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
        : this(logger, Utils.GetFilePath("history", "random-number", "stats.json"))
    {
    }

    /// <summary>Test seam: lets a test point at a temporary file instead of the data root.</summary>
    public RandomNumberStatisticsService(ILogger<RandomNumberStatisticsService> logger, string filePath)
    {
        _logger = logger;
        _filePath = filePath;
    }

    public event EventHandler? Changed;

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
                document.TotalRounds++;
                document.TotalDrawnCount += numbers.Count;
                document.LastDrawAt = DateTime.Now;
                document.Recent.InsertRange(0,
                [
                    new RandomNumberDrawRecord
                    {
                        DrawTime = DateTime.Now,
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
            return new RandomNumberStatistics();

        try
        {
            var document = JsonSerializer.Deserialize<RandomNumberStatistics>(
                File.ReadAllText(_filePath), ConfigServiceBase.JsonOptions);
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
            return document;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.LogWarning(exception, "随机数统计文件不可读，已隔离并重新开始：{Path}", _filePath);
            TryQuarantine();
            return new RandomNumberStatistics();
        }
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
            document.Recent.Select(Clone).ToArray());

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