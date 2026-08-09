using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public sealed class HistoryQuerySnapshotTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "history-snapshot-tests", Guid.NewGuid().ToString("N"));

    public HistoryQuerySnapshotTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [Fact]
    public void LoadStudentHistory_ReturnsFullSnapshotWithoutSwitchingActiveProfile()
    {
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        Assert.True(manager.CreateStudentList("snapshot-class"));

        var profile = provider.GetRequiredService<IProfileService>();
        profile.LoadStudentProfile("snapshot-class");
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(student);
        profile.SaveProfile();
        profile.RecordStudentHistory([student], DateTime.Now, 1);

        var activeList = profile.StudentListConfig;
        var query = provider.GetRequiredService<IHistoryQueryService>();
        var snapshot = query.LoadStudentHistory("snapshot-class");

        Assert.NotNull(snapshot);
        var record = Assert.Single(snapshot.Students.Values);
        Assert.Equal(1, record.TotalCount);
        var item = Assert.Single(record.Histories);
        Assert.Equal("Lin", item.RecordName);
        Assert.True(item.Weight > 0);
        // 不切换活跃档案
        Assert.Same(activeList, profile.StudentListConfig);
    }

    [Fact]
    public void LoadPrizeHistory_ReturnsFullSnapshot()
    {
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        Assert.True(manager.CreatePrizeList("snapshot-pool"));

        var profile = provider.GetRequiredService<IProfileService>();
        profile.LoadPrizeProfile("snapshot-pool");
        var prize = new Prize { Name = "Book", Count = 1, RecordId = Guid.NewGuid() };
        profile.CurrentPrizeList!.Prizes.Add(prize);
        profile.SaveProfile();
        profile.RecordPrizeHistory([prize], DateTime.Now, 1);

        var query = provider.GetRequiredService<IHistoryQueryService>();
        var snapshot = query.LoadPrizeHistory("snapshot-pool");

        Assert.NotNull(snapshot);
        var record = Assert.Single(snapshot.Prizes.Values);
        Assert.Equal(1, record.TotalCount);
        Assert.Single(record.Histories);
    }

    [Fact]
    public void LoadHistory_ReturnsNullForMissingWithoutCreatingFiles()
    {
        using var provider = CreateProvider();
        var query = provider.GetRequiredService<IHistoryQueryService>();

        Assert.Null(query.LoadStudentHistory("missing-class"));
        Assert.Null(query.LoadPrizeHistory("missing-pool"));
        Assert.Null(query.LoadStudentHistory("  "));
        Assert.False(File.Exists(Utils.GetFilePath("history", "roll_call_history", "missing-class.json")));
        Assert.False(File.Exists(Utils.GetFilePath("history", "lottery_history", "missing-pool.json")));
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        return services.BuildServiceProvider();
    }

    private static void ConfigureDataRootForTests(string dataRoot)
    {
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);
    }

    private static void ResetDataRootForTests()
    {
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);
    }

    private static MethodInfo GetUtilsMethod(string name)
    {
        return typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException($"Utils.{name} was not found.");
    }
}
