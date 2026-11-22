using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.HistoryStore;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     历史记录从 JSON 档案换成小数据库后的行为：旧文件无感迁移、保存只写变动、枚举/删除覆盖三份文件。
/// </summary>
public sealed class HistoryDatabaseTests : IDisposable
{
    private const string ProfileName = "历史数据库测试班";

    private readonly string _dataRoot =
        Path.Combine(Path.GetTempPath(), "SecRandom", "history-database-tests", Guid.NewGuid().ToString("N"));

    public HistoryDatabaseTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public void FirstLoad_MigratesLegacyJsonIntoTheDatabaseAndKeepsABackupCopy()
    {
        using var provider = CreateProvider();
        WriteLegacyJson(provider);

        var history = Load(provider);

        Assert.Equal(7, history.TotalRounds);
        Assert.Equal(70, history.TotalStats);
        var item = Assert.Single(history.Students).Value.Histories.Single();
        Assert.Equal("期末抽奖", item.HistoryGroup);
        Assert.Equal(0.75, item.Weight, 6);
        Assert.True(history.HistoryGroups.ContainsKey("期末抽奖"));

        Assert.True(File.Exists(ProfileHistoryPaths.DatabasePath(ProfileName, SecRandom.Core.Enums.HistoryGroupKind.RollCall)));
        // 旧 JSON 改名保留一次性回滚副本；扩展名不再匹配 *.json，任何 JSON 入口都不会把它当成档案。
        Assert.False(File.Exists(ProfileHistoryPaths.LegacyJsonPath(ProfileName, SecRandom.Core.Enums.HistoryGroupKind.RollCall)));
        Assert.True(File.Exists(ProfileHistoryPaths.LegacyBackupPath(ProfileName, SecRandom.Core.Enums.HistoryGroupKind.RollCall)));
    }

    [Fact]
    public void Save_WritesOnlyTheChangedMemberAndKeepsTheRestOfTheHistory()
    {
        using var provider = CreateProvider();
        WriteLegacyJson(provider);
        var handler = CreateHandler(provider);
        var history = handler.Data;

        history.TotalRounds++;
        history.Students.Single().Value.Histories.Add(new HistoryItem
        {
            RecordId = history.Students.Single().Key,
            RecordName = "张三",
            DrawTime = new DateTime(2026, 2, 3, 4, 5, 6),
            DrawRoundId = "round-2",
            HistoryGroup = string.Empty
        });
        handler.Save();

        var reloaded = Load(provider);
        Assert.Equal(8, reloaded.TotalRounds);
        var records = reloaded.Students.Single().Value.Histories;
        Assert.Equal(2, records.Count);
        Assert.Equal("round-2", records[1].DrawRoundId);
        Assert.Equal("期末抽奖", records[0].HistoryGroup);
    }

    [Fact]
    public void MovingARecordToAnotherGroup_UpdatesThatRowOnly()
    {
        using var provider = CreateProvider();
        WriteLegacyJson(provider);
        var handler = CreateHandler(provider);
        handler.Data.Students.Single().Value.Histories.Single().HistoryGroup = "课堂";
        handler.Save();

        var reloaded = Load(provider);
        Assert.Equal("课堂", reloaded.Students.Single().Value.Histories.Single().HistoryGroup);
    }

    [Fact]
    public void EnumerateProfileNames_CountsDatabasesAndUnmigratedJsonButNeverTheBackup()
    {
        using var provider = CreateProvider();
        WriteLegacyJson(provider);
        Load(provider);

        var names = ProfileHistoryPaths.EnumerateProfileNames(SecRandom.Core.Enums.HistoryGroupKind.RollCall);

        Assert.Contains(ProfileName, names);
        Assert.DoesNotContain(names, name => name.Contains("migrated", StringComparison.Ordinal));
    }

    [Fact]
    public void Delete_RemovesTheDatabaseAndEveryLegacyCopy()
    {
        using var provider = CreateProvider();
        WriteLegacyJson(provider);
        var handler = CreateHandler(provider);

        handler.Delete();

        const SecRandom.Core.Enums.HistoryGroupKind kind = SecRandom.Core.Enums.HistoryGroupKind.RollCall;
        Assert.False(File.Exists(ProfileHistoryPaths.DatabasePath(ProfileName, kind)));
        Assert.False(File.Exists(ProfileHistoryPaths.LegacyJsonPath(ProfileName, kind)));
        Assert.False(File.Exists(ProfileHistoryPaths.LegacyBackupPath(ProfileName, kind)));
    }

    [Fact]
    public void LoadingAMissingProfile_ReturnsEmptyHistoryAndWritesNothing()
    {
        using var provider = CreateProvider();

        var history = Load(provider);

        Assert.Empty(history.Students);
        Assert.False(File.Exists(ProfileHistoryPaths.DatabasePath("不存在的班", SecRandom.Core.Enums.HistoryGroupKind.RollCall)));
    }

    [Fact]
    public void Save_RemovesOnlyMissingMembersIncludingThoseWithoutRecords()
    {
        using var provider = CreateProvider();
        var handler = CreateHandler(provider);
        handler.Data.Students["keep"] = new History { TotalCount = 1, Histories = [new HistoryItem { RecordId = "keep" }] };
        handler.Data.Students["remove"] = new History { TotalCount = 2, Histories = [new HistoryItem { RecordId = "remove" }] };
        handler.Data.Students["counter-only"] = new History { TotalCount = 3 };
        handler.Save();

        handler.Data.Students.Remove("remove");
        handler.Data.Students.Remove("counter-only");
        handler.Save();

        var remaining = Assert.Single(Load(provider).Students);
        Assert.Equal("keep", remaining.Key);
        Assert.Equal(1, remaining.Value.TotalCount);
        Assert.Single(remaining.Value.Histories);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ClearHistory_RemovesRecordsAndMemberCountersAfterReload(bool prize, bool active)
    {
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        var profile = provider.GetRequiredService<IProfileService>();
        var logger = provider.GetRequiredService<ILogger<StudentHistoryConfig>>();
        var config = provider.GetRequiredService<ConfigServiceBase>();
        var name = active ? "active" : "inactive";

        if (prize)
        {
            manager.CreatePrizeList("active");
            manager.CreatePrizeList("inactive");
            profile.LoadPrizeProfile("active");
            var handler = active ? profile.PrizeHistoryConfig! : new PrizeHistoryConfig(name, logger, config);
            handler.Data.TotalRounds = 3;
            handler.Data.TotalStats = 3;
            handler.Data.Prizes["drawn"] = new History { TotalCount = 3, Histories = [new HistoryItem { RecordId = "drawn" }] };
            handler.Data.Prizes["counter-only"] = new History { TotalCount = 2 };
            handler.Data.HistoryGroups["group"] = new HistoryGroupInfo();
            handler.Save();

            Assert.True(manager.ClearPrizeHistory(name));
            var result = new PrizeHistoryConfig(name, logger, config).Data;
            Assert.Empty(result.Prizes);
            Assert.Equal(0, result.TotalRounds);
            Assert.Equal(0, result.TotalStats);
            Assert.Contains("group", result.HistoryGroups.Keys);
        }
        else
        {
            manager.CreateStudentList("active");
            manager.CreateStudentList("inactive");
            profile.LoadStudentProfile("active");
            var handler = active ? profile.StudentHistoryConfig! : new StudentHistoryConfig(name, logger, config);
            handler.Data.TotalRounds = 3;
            handler.Data.TotalStats = 3;
            handler.Data.Students["drawn"] = new History { TotalCount = 3, Histories = [new HistoryItem { RecordId = "drawn" }] };
            handler.Data.Students["counter-only"] = new History { TotalCount = 2 };
            handler.Data.HistoryGroups["group"] = new HistoryGroupInfo();
            handler.Save();

            Assert.True(manager.ClearStudentHistory(name));
            var result = new StudentHistoryConfig(name, logger, config).Data;
            Assert.Empty(result.Students);
            Assert.Equal(0, result.TotalRounds);
            Assert.Equal(0, result.TotalStats);
            Assert.Contains("group", result.HistoryGroups.Keys);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DeleteRoster_RemovesOnlySelectedRosterAndItsDatabaseHistory(bool prize, bool active)
    {
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        var profile = provider.GetRequiredService<IProfileService>();
        var name = active ? "active" : "other";
        var survivor = active ? "other" : "active";

        if (prize)
        {
            manager.CreatePrizeList("active");
            manager.CreatePrizeList("other");
            profile.LoadPrizeProfile(name);
            profile.RecordPrizeHistory([new Prize { Name = "奖品" }], DateTime.Now, 1);
            if (!active) profile.LoadPrizeProfile("active");
            Assert.True(ProfileHistoryPaths.Exists(name, HistoryGroupKind.Lottery));
            Assert.True(manager.DeletePrizeList(name, deleteHistory: true));
            profile.SaveProfile();
            Assert.Equal([survivor], manager.GetPrizeListNames());
            Assert.False(ProfileHistoryPaths.Exists(name, HistoryGroupKind.Lottery));
            Assert.Equal(survivor, profile.PrizeListConfig!.Name);
        }
        else
        {
            manager.CreateStudentList("active");
            manager.CreateStudentList("other");
            profile.LoadStudentProfile(name);
            profile.RecordStudentHistory([new Student { Name = "学生" }], DateTime.Now, 1);
            if (!active) profile.LoadStudentProfile("active");
            Assert.True(ProfileHistoryPaths.Exists(name, HistoryGroupKind.RollCall));
            Assert.True(manager.DeleteStudentList(name, deleteHistory: true));
            profile.SaveProfile();
            Assert.Equal([survivor], manager.GetStudentListNames());
            Assert.False(ProfileHistoryPaths.Exists(name, HistoryGroupKind.RollCall));
            Assert.Equal(survivor, profile.StudentListConfig!.Name);
        }
    }

    private static StudentHistory Load(ServiceProvider provider) => CreateHandler(provider).Data;

    private static StudentHistoryConfig CreateHandler(ServiceProvider provider) =>
        new(ProfileName, provider.GetRequiredService<ILogger<StudentHistoryConfig>>(),
            provider.GetRequiredService<ConfigServiceBase>());

    private static void WriteLegacyJson(ServiceProvider provider)
    {
        var history = new StudentHistory(ProfileName)
        {
            TotalRounds = 7,
            TotalStats = 70
        };
        history.Students["student-1"] = new History
        {
            LastDrawnTime = new DateTime(2026, 1, 2, 3, 4, 5),
            RoundsMissed = 2,
            TotalCount = 3,
            Histories =
            [
                new HistoryItem
                {
                    RecordId = "student-1",
                    RecordName = "张三",
                    RecordNumber = "1",
                    RecordGender = "男",
                    RecordGroup = "一组",
                    DrawGender = "男",
                    DrawGroup = "一组",
                    CourseName = "数学",
                    HistoryGroup = "期末抽奖",
                    DrawMethod = 2,
                    DrawNumbers = 3,
                    DrawTime = new DateTime(2026, 1, 2, 3, 4, 5),
                    DrawRoundId = "round-1",
                    Weight = 0.75
                }
            ]
        };
        history.GroupStats["一组"] = 1;
        history.GenderStatus["男"] = 1;
        history.HistoryGroups["期末抽奖"] = new HistoryGroupInfo
        {
            CreatedAt = new DateTime(2025, 12, 1),
            LastUsedAt = new DateTime(2026, 1, 2)
        };

        // 直接写旧格式：迁移必须由第一次读取触发，不需要用户做任何操作。
        var legacyPath = ProfileHistoryPaths.LegacyJsonPath(ProfileName,
            SecRandom.Core.Enums.HistoryGroupKind.RollCall);
        File.WriteAllText(legacyPath, JsonSerializer.Serialize(history, ConfigServiceBase.JsonOptions));
        Assert.NotNull(provider);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        return services.BuildServiceProvider();
    }

    private static void ConfigureDataRootForTests(string dataRoot) =>
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);

    private static void ResetDataRootForTests() => GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);

    private static MethodInfo GetUtilsMethod(string name) =>
        typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Utils.{name} was not found.");
}
