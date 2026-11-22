using System.Reflection;
using System.Text.Json;
using SecRandom.Core.Models.Orchestration;
using SecRandom.Core.Services.Orchestration;
using SecRandom.Shared;

namespace SecRandom.Core.Tests;

public sealed class OrchestrationStoreTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "orchestration-store-tests", Guid.NewGuid().ToString("N"));

    public OrchestrationStoreTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [Fact]
    public void ProfilesAndRuntimeShareOneDatabaseAndRoundTrip()
    {
        var store = new OrchestrationStore();
        var memberId = Guid.NewGuid();
        var profile = new OrchestrationProfile
        {
            Id = OrchestrationStore.NewId(),
            Name = "编排名单",
            Target = OrchestrationTarget.RollCall,
            SourceListName = "class-a",
            IsActive = true,
            RoundCount = 2,
            Members = [new OrchestrationMember { RecordId = memberId, Number = "7", Name = "甲" }]
        };
        profile.SetOrder(profile.GetRound(1), [memberId]);

        store.Save(profile);
        store.SaveRuntime(new OrchestrationRuntime { ProfileId = profile.Id, Round = 2 });

        Assert.True(File.Exists(OrchestrationStore.DatabasePath()));
        Assert.Equal("编排名单", Assert.Single(store.LoadAll()).Name);
        Assert.Equal("甲", Assert.Single(store.Load(profile.Id)!.Members).Name);
        Assert.Equal(2, store.GetRuntime(profile).Round);
        Assert.False(File.Exists(Utils.GetFilePath("config", "orchestration", profile.Id + ".json")));
    }

    [Fact]
    public void LoadAutomaticallyMigratesLegacyProfileAndRuntimeJsonWithoutDiscardingBackup()
    {
        var id = OrchestrationStore.NewId();
        var legacyProfilePath = Utils.GetFilePath("config", "orchestration", id + ".json");
        var legacyRuntimePath = Utils.GetFilePath("config", "orchestration", "runtime", id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyProfilePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyRuntimePath)!);
        var profile = new OrchestrationProfile
        {
            Id = id,
            Name = "旧编排名单",
            Target = OrchestrationTarget.Lottery,
            SourceListName = "pool-a",
            RoundCount = 3
        };
        File.WriteAllText(legacyProfilePath, JsonSerializer.Serialize(profile));
        File.WriteAllText(legacyRuntimePath, JsonSerializer.Serialize(new OrchestrationRuntime { ProfileId = id, Round = 3 }));
        var oldBackup = legacyProfilePath + ".migrated";
        File.WriteAllText(oldBackup, "older backup");

        var store = new OrchestrationStore();
        var loaded = store.Load(id);

        Assert.Equal("旧编排名单", loaded?.Name);
        Assert.Equal(3, store.GetRuntime(loaded!).Round);
        Assert.False(File.Exists(legacyProfilePath));
        Assert.False(File.Exists(legacyRuntimePath));
        Assert.Equal("older backup", File.ReadAllText(oldBackup));
        Assert.True(File.Exists(oldBackup + ".1"));
        Assert.True(File.Exists(legacyRuntimePath + ".migrated"));
        Assert.True(File.Exists(OrchestrationStore.DatabasePath()));
    }

    [Fact]
    public void UnrecognizedDatabase_IsReadOnlyOpaqueAndCannotBeOverwritten()
    {
        var path = OrchestrationStore.DatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = [0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x00, 0x02];
        File.WriteAllBytes(path, bytes);
        var store = new OrchestrationStore();

        Assert.Empty(store.LoadAll());
        Assert.Throws<InvalidDataException>(() => store.Save(new OrchestrationProfile { Id = OrchestrationStore.NewId() }));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static void ConfigureDataRootForTests(string dataRoot) =>
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);

    private static void ResetDataRootForTests() =>
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);

    private static MethodInfo GetUtilsMethod(string name) =>
        typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Utils.{name} was not found.");
}
