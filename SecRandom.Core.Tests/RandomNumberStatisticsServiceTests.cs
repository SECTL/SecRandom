using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.Stats;

namespace SecRandom.Core.Tests;

/// <summary>
///     Number draws never write roster history, so this store is the only durable record the overview and the
///     random-number settings page can read. It must survive a corrupt file and must never keep more than the
///     bounded recent list.
/// </summary>
public sealed class RandomNumberStatisticsServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"secrandom-random-number-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(_directory, "stats.json");

    private RandomNumberStatisticsService CreateService() =>
        new(NullLogger<RandomNumberStatisticsService>.Instance, FilePath);

    [Fact]
    public void RecordAccumulatesRoundsAndDrawnNumbers()
    {
        var service = CreateService();

        service.Record("roll_call", 1, 50, [7, 9]);
        service.Record("lottery", 10, 20, [12]);

        var snapshot = service.Load();

        Assert.Equal(2, snapshot.TotalRounds);
        Assert.Equal(3, snapshot.TotalDrawnCount);
        Assert.NotNull(snapshot.LastDrawAt);
        Assert.Equal(2, snapshot.Recent.Count);
        Assert.Equal("lottery", snapshot.Recent[0].Source);
        Assert.Equal(new[] { 1, 50 }, new[] { snapshot.Recent[1].RangeStart, snapshot.Recent[1].RangeEnd });
        Assert.Equal(new[] { 7, 9 }, snapshot.Recent[1].Numbers);
        Assert.Collection(snapshot.Ranges,
            range =>
            {
                Assert.Equal((1, 50), (range.RangeStart, range.RangeEnd));
                Assert.Equal(1, range.DrawCount);
            },
            range =>
            {
                Assert.Equal((10, 20), (range.RangeStart, range.RangeEnd));
                Assert.Equal(1, range.DrawCount);
            });
    }

    [Fact]
    public void EmptyResultIsNotRecorded()
    {
        var service = CreateService();

        service.Record("roll_call", 1, 50, []);

        var snapshot = service.Load();
        Assert.Equal(0, snapshot.TotalRounds);
        Assert.Empty(snapshot.Recent);
    }

    [Fact]
    public void SameRangeCountsEveryDrawWithoutKeepingTheResults()
    {
        var service = CreateService();

        service.Record("roll_call", 1, 50, [7, 9]);
        service.Record("lottery", 1, 50, [42]);
        service.Record("roll_call", 10, 20, [12]);

        var snapshot = service.Load();

        Assert.Equal(3, snapshot.TotalRounds);
        Assert.Equal(2, snapshot.Ranges.Count);
        Assert.Equal(2, snapshot.Ranges[0].DrawCount);
        Assert.Equal((1, 50), (snapshot.Ranges[0].RangeStart, snapshot.Ranges[0].RangeEnd));
        Assert.Equal(1, snapshot.Ranges[1].DrawCount);
    }

    [Fact]
    public void RecentRecordsKeepOnlyTheNewestHundred()
    {
        var service = CreateService();

        for (var number = 1; number <= RandomNumberStatistics.MaxRecentRecords + 5; number++)
            service.Record("roll_call", 1, 500, [number]);

        var snapshot = service.Load();

        Assert.Equal(RandomNumberStatistics.MaxRecentRecords + 5, snapshot.TotalRounds);
        Assert.Equal(RandomNumberStatistics.MaxRecentRecords, snapshot.Recent.Count);
        Assert.Equal(105, snapshot.Recent[0].Numbers[0]);
        Assert.Equal(6, snapshot.Recent[^1].Numbers[0]);
    }

    [Fact]
    public void CorruptFileIsQuarantinedAndTreatedAsEmpty()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ not json");
        var service = CreateService();

        var snapshot = service.Load();

        Assert.Equal(0, snapshot.TotalRounds);
        Assert.False(File.Exists(FilePath));
        Assert.True(File.Exists(FilePath + ".corrupt"));

        // A subsequent commit starts a fresh, valid document.
        service.Record("roll_call", 1, 10, [3]);
        Assert.Equal(1, service.Load().TotalRounds);
    }

    [Fact]
    public void UnsupportedFormatIsQuarantined()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath,
            JsonSerializer.Serialize(new RandomNumberStatistics { Format = "unknown/v9" },
                ConfigServiceBase.JsonOptions));

        var snapshot = CreateService().Load();

        Assert.Equal(0, snapshot.TotalRounds);
        Assert.True(File.Exists(FilePath + ".corrupt"));
    }

    [Fact]
    public void ClearResetsTotalsAndRecent()
    {
        var service = CreateService();
        service.Record("roll_call", 1, 50, [4]);

        service.Clear();

        var snapshot = service.Load();
        Assert.Equal(0, snapshot.TotalRounds);
        Assert.Equal(0, snapshot.TotalDrawnCount);
        Assert.Null(snapshot.LastDrawAt);
        Assert.Empty(snapshot.Recent);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}