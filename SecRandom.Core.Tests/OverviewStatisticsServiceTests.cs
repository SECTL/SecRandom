using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Stats;

namespace SecRandom.Core.Tests;

public sealed class OverviewStatisticsServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"secrandom-overview-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(_directory, "overview.json");

    private OverviewStatisticsService CreateService() =>
        new(NullLogger<OverviewStatisticsService>.Instance, FilePath);

    [Fact]
    public void DrawsAreStoredPerProfileAndSurviveReload()
    {
        var service = CreateService();
        var drawTime = new DateTime(2026, 11, 21, 9, 30, 0);

        service.RecordStudentDraw("一班", 2, drawTime);
        service.RecordStudentDraw("一班", 1, drawTime.AddMinutes(2));
        service.RecordPrizeDraw("奖池", 1, drawTime);

        var snapshot = CreateService().Load();

        Assert.Equal((2, 3),
            (snapshot.RollCall["一班"].TotalRounds, snapshot.RollCall["一班"].TotalDrawnCount));
        Assert.Equal(drawTime.AddMinutes(2), snapshot.RollCall["一班"].LastDrawAt);
        Assert.Equal(1, snapshot.Lottery["奖池"].TotalRounds);
    }

    [Fact]
    public void ClearOnlyRemovesOverviewDocument()
    {
        var service = CreateService();
        service.RecordStudentDraw("一班", 1, DateTime.Now);

        service.Clear();

        Assert.Empty(CreateService().Load().RollCall);
        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
        Assert.Equal(OverviewStatistics.CurrentFormat, document.RootElement.GetProperty("format").GetString());
    }

    [Fact]
    public void CorruptDocumentIsQuarantined()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{broken");

        var snapshot = CreateService().Load();

        Assert.Empty(snapshot.RollCall);
        Assert.True(File.Exists(FilePath + ".corrupt"));
    }

    [Fact]
    public void RandomNumbersShareTheOverviewDocumentAndCanBeClearedSeparately()
    {
        var service = CreateService();
        var random = (IRandomNumberStatisticsService)service;

        service.RecordStudentDraw("一班", 1, DateTime.Now);
        random.Record("roll_call", 1, 10, [3]);
        random.Clear();

        Assert.True(service.Load().RollCall.ContainsKey("一班"));
        Assert.Empty(random.Load().Ranges);
        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
        Assert.True(document.RootElement.TryGetProperty("random_numbers", out _));
    }

    [Fact]
    public void ProfileClearOnlyRemovesThatProfilesOverview()
    {
        var service = CreateService();
        service.RecordStudentDraw("一班", 1, DateTime.Now);
        service.RecordStudentDraw("二班", 1, DateTime.Now);

        service.ClearStudentStatistics("一班");

        var snapshot = service.Load();
        Assert.DoesNotContain("一班", snapshot.RollCall.Keys);
        Assert.Contains("二班", snapshot.RollCall.Keys);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
