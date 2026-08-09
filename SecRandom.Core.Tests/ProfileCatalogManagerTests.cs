using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public sealed class ProfileCatalogManagerTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "catalog-manager-tests", Guid.NewGuid().ToString("N"));

    public ProfileCatalogManagerTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [Fact]
    public void CreateEnumerateExists_AndDuplicateCreateIsRejected()
    {
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();

        Assert.True(manager.CreateStudentList("class-a"));
        Assert.False(manager.CreateStudentList("class-a"));
        Assert.False(manager.CreateStudentList("  "));
        Assert.True(manager.StudentListExists("class-a"));
        Assert.Contains("class-a", manager.GetStudentListNames());

        Assert.True(manager.CreatePrizeList("pool-a"));
        Assert.False(manager.CreatePrizeList("pool-a"));
        Assert.True(manager.PrizeListExists("pool-a"));
        Assert.Contains("pool-a", manager.GetPrizeListNames());
    }

    [Fact]
    public void LoadStudentList_ReturnsNullForMissingWithoutCreatingFiles()
    {
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();

        Assert.Null(manager.LoadStudentList("missing-class"));
        Assert.Null(manager.LoadPrizeList("missing-pool"));
        Assert.False(File.Exists(Utils.GetFilePath("list", "roll_call_list", "missing-class.json")));
        Assert.False(File.Exists(Utils.GetFilePath("list", "lottery_list", "missing-pool.json")));
    }

    [Fact]
    public void ReplaceStudents_AssignsRecordIdsSortsPersistsAndSyncsActiveProfile()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        var activeName = profile.StudentListConfig!.Name;

        var students = new List<Student>
        {
            new() { Name = "无名学生" },
            new() { Id = "2", Name = "乙" },
            new() { Id = "10", Name = "甲" }
        };

        Assert.True(manager.ReplaceStudents(activeName, students));

        // 活跃档案已同步为新内容
        var active = profile.CurrentStudentList!;
        Assert.Equal(3, active.Students.Count);
        Assert.All(active.Students, student => Assert.NotEqual(Guid.Empty, student.RecordId));
        // OrderForList：数字 Id 升序在前，无 Id 的按名称排在后
        Assert.Equal("2", active.Students[0].Id);
        Assert.Equal("10", active.Students[1].Id);
        Assert.Equal("无名学生", active.Students[2].Name);

        // 已持久化，只读快照可跨加载读取
        var snapshot = manager.LoadStudentList(activeName);
        Assert.NotNull(snapshot);
        Assert.Equal(3, snapshot.Students.Count);
        Assert.Equal(active.Students.Select(s => s.RecordId), snapshot.Students.Select(s => s.RecordId));
    }

    [Fact]
    public void ReplacePrizes_PersistsAndSyncsActiveProfile()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        var activeName = profile.PrizeListConfig!.Name;

        Assert.True(manager.ReplacePrizes(activeName, [new Prize { Name = "Book", Weight = 2, Count = 3 }]));

        var prize = Assert.Single(profile.CurrentPrizeList!.Prizes);
        Assert.Equal("Book", prize.Name);
        Assert.NotEqual(Guid.Empty, prize.RecordId);
        Assert.NotNull(manager.LoadPrizeList(activeName));
    }

    [Fact]
    public void SaveStudentList_PreservesAttachedObjectsAndUpdatesDefaultClass()
    {
        using var provider = CreateProvider();
        var config = provider.GetRequiredService<MainConfigHandler>();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();

        Assert.True(manager.CreateStudentList("class-b"));
        var snapshot = manager.LoadStudentList("class-b")!;
        var attachedKey = Guid.NewGuid();
        snapshot.AttachedObjects[attachedKey] = "attached-value";
        snapshot.Students.Add(new Student { Name = "丙" });

        Assert.True(manager.SaveStudentList(snapshot));
        manager.SetDefaultStudentList("class-b");
        Assert.Equal("class-b", config.Data.RollCallSettings.DefaultClass);

        var reloaded = manager.LoadStudentList("class-b")!;
        Assert.Single(reloaded.Students);
        Assert.True(reloaded.AttachedObjects.ContainsKey(attachedKey));
    }

    [Fact]
    public void DeleteStudentList_RemovesHistoryAndSwitchesActiveProfile()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var configService = provider.GetRequiredService<SecRandom.Core.Abstraction.ConfigServiceBase>();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        var fallbackName = profile.StudentListConfig!.Name;

        Assert.True(manager.CreateStudentList("class-c"));
        configService.SaveConfig(new StudentHistory("class-c"));
        Assert.True(File.Exists(Utils.GetFilePath("history", "roll_call_history", "class-c.json")));

        profile.LoadStudentProfile("class-c");
        Assert.Equal("class-c", profile.StudentListConfig!.Name);

        Assert.True(manager.DeleteStudentList("class-c", deleteHistory: true));
        Assert.False(manager.StudentListExists("class-c"));
        Assert.False(File.Exists(Utils.GetFilePath("list", "roll_call_list", "class-c.json")));
        Assert.False(File.Exists(Utils.GetFilePath("history", "roll_call_history", "class-c.json")));
        // 活跃档案切换到剩余名单，而不是停留在被删名单
        Assert.Equal(fallbackName, profile.StudentListConfig!.Name);

        Assert.False(manager.DeleteStudentList("class-c", deleteHistory: true));
    }

    [Fact]
    public void DeletePrizeList_RemovesListAndSyncsActiveProfile()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        var fallbackName = profile.PrizeListConfig!.Name;

        Assert.True(manager.CreatePrizeList("pool-c"));
        profile.LoadPrizeProfile("pool-c");

        Assert.True(manager.DeletePrizeList("pool-c", deleteHistory: false));
        Assert.False(manager.PrizeListExists("pool-c"));
        Assert.Equal(fallbackName, profile.PrizeListConfig!.Name);
    }

    [Fact]
    public void RenameStudentList_MovesDataHistoryTemporaryRecordsAndUpdatesActiveProfile()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        var oldName = profile.StudentListConfig!.Name;
        var newName = "renamed-class";
        profile.CurrentStudentList!.Students.Add(new Student { Name = "Alice" });
        var student = profile.CurrentStudentList.Students[0];
        profile.RecordStudentHistory([student], DateTime.Now, 1);
        temporary.RecordStudents(oldName, string.Empty, string.Empty, [student]);

        Assert.True(manager.RenameStudentList(oldName, newName));
        Assert.Equal(newName, profile.StudentListConfig!.Name);
        Assert.True(manager.StudentListExists(newName));
        Assert.False(manager.StudentListExists(oldName));
        Assert.True(File.Exists(Utils.GetFilePath("history", "roll_call_history", $"{newName}.json")));
        Assert.False(File.Exists(Utils.GetFilePath("TEMP", $"roll_call_record_{oldName}.json")));
        Assert.NotEmpty(temporary.GetStudentCounts(newName, string.Empty, string.Empty));
    }

    [Fact]
    public void RenamePrizeList_RejectsDuplicateAndUpdatesDefaultPool()
    {
        using var provider = CreateProvider();
        var config = provider.GetRequiredService<MainConfigHandler>();
        var profile = provider.GetRequiredService<IProfileService>();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        var oldName = profile.PrizeListConfig!.Name;
        manager.SetDefaultPrizePool(oldName);
        Assert.True(manager.CreatePrizeList("existing-pool"));

        Assert.False(manager.RenamePrizeList(oldName, "existing-pool"));
        Assert.True(manager.RenamePrizeList(oldName, "renamed-pool"));
        Assert.Equal("renamed-pool", config.Data.LotterySettings.DefaultPool);
        Assert.Equal("renamed-pool", profile.PrizeListConfig!.Name);
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
