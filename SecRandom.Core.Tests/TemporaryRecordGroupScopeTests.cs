using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     学生临时记录（「已抽过几次」）按历史记录组分别计数：每个记录组是一轮独立的课堂，
///     换组不会借用别的组的进度，组改名/删除时进度跟着走（删除并入默认记录组）。
/// </summary>
public sealed class TemporaryRecordGroupScopeTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "group-scope-tests", Guid.NewGuid().ToString("N"));

    public TemporaryRecordGroupScopeTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [Fact]
    public void Counts_AreSeparateForUnscopedDefaultGroupAndNamedGroup()
    {
        using var provider = CreateProvider();
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        var unscoped = new Student { Name = "全组", RecordId = Guid.NewGuid() };
        var defaultGroup = new Student { Name = "默认组", RecordId = Guid.NewGuid() };
        var named = new Student { Name = "某组", RecordId = Guid.NewGuid() };

        temporary.RecordStudents("scope-class", null, [unscoped]);
        temporary.RecordStudents("scope-class", string.Empty, [defaultGroup]);
        temporary.RecordStudents("scope-class", "期末抽奖", [named]);

        Assert.Equal([unscoped.RecordId.ToString("D")], temporary.GetStudentCounts("scope-class", null).Keys);
        Assert.Equal([defaultGroup.RecordId.ToString("D")], temporary.GetStudentCounts("scope-class", string.Empty).Keys);
        Assert.Equal([named.RecordId.ToString("D")], temporary.GetStudentCounts("scope-class", "期末抽奖").Keys);

        // 整名单求和是「清掉这份名单的所有组」才需要的口径。
        Assert.Equal(3, temporary.GetStudentCountsForList("scope-class").Count);
    }

    [Fact]
    public void SameStudent_IsCountedOncePerGroup()
    {
        using var provider = CreateProvider();
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        var key = student.RecordId.ToString("D");

        temporary.RecordStudents("scope-class", "期末抽奖", [student]);
        temporary.RecordStudents("scope-class", "期末抽奖", [student]);
        temporary.RecordStudents("scope-class", "课堂", [student]);

        Assert.Equal(2, temporary.GetStudentCounts("scope-class", "期末抽奖")[key]);
        Assert.Equal(1, temporary.GetStudentCounts("scope-class", "课堂")[key]);
        Assert.Empty(temporary.GetStudentCounts("scope-class", null));
    }

    [Fact]
    public void ClearStudentScope_OnlyClearsTheRequestedGroup()
    {
        using var provider = CreateProvider();
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        var first = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        var second = new Student { Name = "Mei", RecordId = Guid.NewGuid() };

        temporary.RecordStudents("scope-class", "期末抽奖", [first]);
        temporary.RecordStudents("scope-class", "课堂", [second]);

        temporary.ClearStudentScope("scope-class", "期末抽奖");

        Assert.Empty(temporary.GetStudentCounts("scope-class", "期末抽奖"));
        Assert.Single(temporary.GetStudentCounts("scope-class", "课堂"));
    }

    [Fact]
    public void RenameStudentScope_MergesIntoAnExistingBucket()
    {
        using var provider = CreateProvider();
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        var key = student.RecordId.ToString("D");

        temporary.RecordStudents("scope-class", "旧组", [student]);
        temporary.RecordStudents("scope-class", "旧组", [student]);
        temporary.RecordStudents("scope-class", "新组", [student]);

        temporary.RenameStudentScope("scope-class", "旧组", "新组");

        Assert.Empty(temporary.GetStudentCounts("scope-class", "旧组"));
        Assert.Equal(3, temporary.GetStudentCounts("scope-class", "新组")[key]);
    }

    [Fact]
    public void RenamingHistoryGroup_FollowsIntoTheTemporaryRecords()
    {
        using var provider = CreateProvider();
        var profile = CreateProfile(provider, "rename-class");
        var listName = profile.StudentListConfig!.Name;
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        var catalog = provider.GetRequiredService<IHistoryGroupCatalog>();

        Assert.True(catalog.TryCreate(HistoryGroupKind.RollCall, "期末抽奖", out _));
        temporary.RecordStudents(listName, "期末抽奖", [new Student { Name = "Lin", RecordId = Guid.NewGuid() }]);
        var key = temporary.GetStudentCounts(listName, "期末抽奖").Keys.Single();

        Assert.True(catalog.TryRename(HistoryGroupKind.RollCall, "期末抽奖", "结课抽奖", out _));

        // 改名不是新的一轮：该组的「已抽过几次」必须跟到新名字下，而不是从零开始。
        Assert.Empty(temporary.GetStudentCounts(listName, "期末抽奖"));
        Assert.Equal(1, temporary.GetStudentCounts(listName, "结课抽奖")[key]);
    }

    [Fact]
    public void DeletingHistoryGroup_FallsItsCountsBackIntoTheDefaultGroup()
    {
        using var provider = CreateProvider();
        var profile = CreateProfile(provider, "delete-class");
        var listName = profile.StudentListConfig!.Name;
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        var catalog = provider.GetRequiredService<IHistoryGroupCatalog>();

        Assert.True(catalog.TryCreate(HistoryGroupKind.RollCall, "期末抽奖", out _));
        temporary.RecordStudents(listName, "期末抽奖", [new Student { Name = "Lin", RecordId = Guid.NewGuid() }]);
        var key = temporary.GetStudentCounts(listName, "期末抽奖").Keys.Single();

        catalog.Delete(HistoryGroupKind.RollCall, "期末抽奖");

        // 删除组会把历史记录移回默认组，临时记录的进度也一样，不能凭空蒸发。
        Assert.Empty(temporary.GetStudentCounts(listName, "期末抽奖"));
        Assert.Equal(1, temporary.GetStudentCounts(listName, string.Empty)[key]);
    }

    [Fact]
    public void LegacyFileWithoutFormatVersion_MigratesIntoTheUnscopedBucket()
    {
        using var provider = CreateProvider();
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        var student = new Student { Name = "Legacy", RecordId = Guid.NewGuid() };
        var recordId = ProfileRecordIdentity.EnsureRecordId(student);

        var legacyPath = Utils.GetFilePath("TEMP", "roll_call_record_legacy-group-class.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, $$"""
                                        {
                                          "listName": "legacy-group-class",
                                          "updatedAt": "2026-01-02T00:00:00+00:00",
                                          "scopes": {
                                            "*": { "records": { "{{recordId}}": { "name": "Legacy", "id": "1", "count": 4, "lastDrawnTime": "2026-01-01T00:00:00+00:00" } } }
                                          }
                                        }
                                        """);

        Assert.Equal(4, temporary.GetStudentCounts("legacy-group-class", null)[recordId]);
        // 迁移后立刻写回新版格式，避免下一次加载又把同一批计数再合并一次。
        Assert.Contains("\"formatVersion\": 2", File.ReadAllText(legacyPath), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static IProfileService CreateProfile(ServiceProvider provider, string listName)
    {
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Data.RollCallSettings.DefaultClass = listName;
        config.Save();

        var profile = provider.GetRequiredService<IProfileService>();
        profile.CurrentStudentList!.Students.Add(new Student { Name = "Lin", RecordId = Guid.NewGuid() });
        profile.SaveProfile();
        return profile;
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
