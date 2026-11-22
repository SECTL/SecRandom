using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Profiles;
using SecRandom.Core.Enums;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public sealed class RosterDatabaseTests : IDisposable
{
    private const string ProfileName = "Roster DB 测试";
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "roster-database-tests", Guid.NewGuid().ToString("N"));

    public RosterDatabaseTests()
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
    public void FirstLoad_MigratesStudentJsonAndPreservesOrderIdentityAndAttachedSettings()
    {
        using var provider = CreateProvider();
        var recordId = Guid.NewGuid();
        var attachedKey = Guid.NewGuid();
        var legacy = new StudentList(ProfileName);
        legacy.Students.Add(new Student { Id = "12", Name = "林", Gender = "女", Group = "二组", Tags = "a,b", RecordId = recordId });
        legacy.Students[0].AttachedObjects[attachedKey] = "voice-setting";
        legacy.AttachedObjects[attachedKey] = "list-setting";
        File.WriteAllText(ProfileRosterPaths.LegacyJsonPath(ProfileName, SecRandom.Core.Enums.ProfileRosterKind.RollCall),
            JsonSerializer.Serialize(legacy, ConfigServiceBase.JsonOptions));

        var loaded = new StudentListConfig(ProfileName, provider.GetRequiredService<ILogger<StudentListConfig>>(),
            provider.GetRequiredService<ConfigServiceBase>()).Data;

        var student = Assert.Single(loaded.Students);
        Assert.Equal(recordId, student.RecordId);
        Assert.Equal("林", student.Name);
        Assert.Equal("女", student.Gender);
        Assert.Equal("二组", student.Group);
        Assert.Equal("a,b", student.Tags);
        Assert.Equal("voice-setting", student.AttachedObjects[attachedKey]?.ToString());
        Assert.Equal("list-setting", loaded.AttachedObjects[attachedKey]?.ToString());
        Assert.True(File.Exists(ProfileRosterPaths.DatabasePath(ProfileName, SecRandom.Core.Enums.ProfileRosterKind.RollCall)));
        Assert.False(File.Exists(ProfileRosterPaths.LegacyJsonPath(ProfileName, SecRandom.Core.Enums.ProfileRosterKind.RollCall)));
        Assert.True(File.Exists(ProfileRosterPaths.LegacyBackupPath(ProfileName, SecRandom.Core.Enums.ProfileRosterKind.RollCall)));
    }

    [Fact]
    public void SaveReloadAndEnumeratePrizeDatabase_PreservesPrizeFieldsAndExcludesBackup()
    {
        using var provider = CreateProvider();
        var attachedKey = Guid.NewGuid();
        var prize = new Prize { Id = "P1", Name = "书", Count = 4, Weight = 2.5, Tags = "rare", RecordId = Guid.NewGuid() };
        prize.AttachedObjects[attachedKey] = "prize-setting";
        var handler = new PrizeListConfig(ProfileName, provider.GetRequiredService<ILogger<PrizeListConfig>>(),
            provider.GetRequiredService<ConfigServiceBase>());
        handler.Data.Prizes.Add(prize);
        handler.Save();
        File.WriteAllText(ProfileRosterPaths.LegacyBackupPath("legacy-only", SecRandom.Core.Enums.ProfileRosterKind.Lottery), "backup");

        var reloaded = new PrizeListConfig(ProfileName, provider.GetRequiredService<ILogger<PrizeListConfig>>(),
            provider.GetRequiredService<ConfigServiceBase>()).Data;
        var saved = Assert.Single(reloaded.Prizes);
        Assert.Equal(prize.RecordId, saved.RecordId);
        Assert.Equal(4, saved.Count);
        Assert.Equal(2.5, saved.Weight);
        Assert.Equal("rare", saved.Tags);
        Assert.Equal("prize-setting", saved.AttachedObjects[attachedKey]?.ToString());
        Assert.Contains(ProfileName, ProfileRosterPaths.EnumerateProfileNames(SecRandom.Core.Enums.ProfileRosterKind.Lottery));
        Assert.DoesNotContain("legacy-only", ProfileRosterPaths.EnumerateProfileNames(SecRandom.Core.Enums.ProfileRosterKind.Lottery));
    }

    [Fact]
    public void Save_NormalizesMissingAndDuplicateRecordIds()
    {
        using var provider = CreateProvider();
        var handler = new StudentListConfig(ProfileName,
            provider.GetRequiredService<ILogger<StudentListConfig>>(),
            provider.GetRequiredService<ConfigServiceBase>());
        var duplicate = Guid.NewGuid();
        handler.Data.Students.Add(new Student { Name = "missing", RecordId = Guid.Empty });
        handler.Data.Students.Add(new Student { Name = "first", RecordId = duplicate });
        handler.Data.Students.Add(new Student { Name = "duplicate", RecordId = duplicate });

        handler.Save();

        var loaded = new StudentListConfig(ProfileName,
            provider.GetRequiredService<ILogger<StudentListConfig>>(),
            provider.GetRequiredService<ConfigServiceBase>()).Data;
        Assert.Equal(3, loaded.Students.Count);
        Assert.All(loaded.Students, student => Assert.NotEqual(Guid.Empty, student.RecordId));
        Assert.Equal(3, loaded.Students.Select(student => student.RecordId).Distinct().Count());
    }

    [Fact]
    public void MultipleProfilesShareOneDatabasePerRosterKind_AndRenameDeleteOnlyOneProfile()
    {
        using var provider = CreateProvider();
        var logger = provider.GetRequiredService<ILogger<StudentListConfig>>();
        var configService = provider.GetRequiredService<ConfigServiceBase>();
        var first = new StudentListConfig("class-a", logger, configService);
        first.Data.Students.Add(new Student { Name = "甲" });
        first.Save();
        var second = new StudentListConfig("class-b", logger, configService);
        second.Data.Students.Add(new Student { Name = "乙" });
        second.Save();

        var rollCallDatabase = ProfileRosterPaths.DatabasePath(ProfileRosterKind.RollCall);
        Assert.Equal(rollCallDatabase, first.Data.ConfigFilePath);
        Assert.Equal(rollCallDatabase, second.Data.ConfigFilePath);
        Assert.Equal(["class-a", "class-b"], ProfileRosterPaths.EnumerateProfileNames(ProfileRosterKind.RollCall));

        ProfileRosterPaths.Rename("class-a", "class-renamed", ProfileRosterKind.RollCall);
        ProfileRosterPaths.Delete("class-b", ProfileRosterKind.RollCall);

        Assert.Equal(["class-renamed"], ProfileRosterPaths.EnumerateProfileNames(ProfileRosterKind.RollCall));
        Assert.Equal("甲", Assert.Single(new StudentListConfig("class-renamed", logger, configService).Data.Students).Name);
        Assert.True(File.Exists(rollCallDatabase));

        var prizeLogger = provider.GetRequiredService<ILogger<PrizeListConfig>>();
        var firstPool = new PrizeListConfig("pool-a", prizeLogger, configService);
        firstPool.Data.Prizes.Add(new Prize { Name = "书" });
        firstPool.Save();
        var secondPool = new PrizeListConfig("pool-b", prizeLogger, configService);
        secondPool.Data.Prizes.Add(new Prize { Name = "笔" });
        secondPool.Save();

        Assert.Equal(ProfileRosterPaths.DatabasePath(ProfileRosterKind.Lottery), firstPool.Data.ConfigFilePath);
        Assert.Equal(firstPool.Data.ConfigFilePath, secondPool.Data.ConfigFilePath);
        Assert.Equal(["pool-a", "pool-b"], ProfileRosterPaths.EnumerateProfileNames(ProfileRosterKind.Lottery));
    }

    [Fact]
    public void LegacyPerProfileSqliteMigratesAndKeepsItsRollbackDatabase()
    {
        using var provider = CreateProvider();
        var legacyPath = ProfileRosterPaths.LegacyDatabasePath(ProfileName, ProfileRosterKind.RollCall);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = legacyPath,
                   Pooling = false
               }.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);" +
                                  "CREATE TABLE members (ordinal INTEGER PRIMARY KEY, record_id TEXT NOT NULL, [exists] INTEGER NOT NULL, id TEXT NOT NULL, name TEXT NOT NULL, gender TEXT NOT NULL, group_name TEXT NOT NULL, tags TEXT NOT NULL, count INTEGER NOT NULL, weight REAL NOT NULL, payload TEXT NOT NULL);" +
                                  "CREATE TABLE attached_objects (control_id TEXT PRIMARY KEY, payload TEXT NOT NULL);" +
                                  "INSERT INTO meta VALUES ('format', 'SecRandomRosterDatabase/v1');" +
                                  "INSERT INTO meta VALUES ('schema_version', '1');" +
                                  "INSERT INTO meta VALUES ('profile_name', $profile);" +
                                  "INSERT INTO meta VALUES ('kind', 'RollCall');" +
                                  "INSERT INTO members VALUES (0, $recordId, 1, '7', '旧名单成员', '女', '一组', 'x', 1, 1, '{}');";
            command.Parameters.AddWithValue("$profile", ProfileName);
            command.Parameters.AddWithValue("$recordId", Guid.NewGuid().ToString("D"));
            command.ExecuteNonQuery();
        }

        var loaded = new StudentListConfig(ProfileName, provider.GetRequiredService<ILogger<StudentListConfig>>(),
            provider.GetRequiredService<ConfigServiceBase>()).Data;

        Assert.Equal("旧名单成员", Assert.Single(loaded.Students).Name);
        Assert.False(File.Exists(legacyPath));
        Assert.True(File.Exists(ProfileRosterPaths.LegacyDatabaseBackupPath(ProfileName, ProfileRosterKind.RollCall)));
        Assert.True(File.Exists(ProfileRosterPaths.DatabasePath(ProfileRosterKind.RollCall)));
    }

    [Fact]
    public void UnrecognizedCentralDatabase_IsKeptOpaqueAndNeverReplacedBySave()
    {
        using var provider = CreateProvider();
        var path = ProfileRosterPaths.DatabasePath(ProfileRosterKind.RollCall);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = [0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x00, 0x01];
        File.WriteAllBytes(path, bytes);

        var list = new StudentListConfig(ProfileName, provider.GetRequiredService<ILogger<StudentListConfig>>(),
            provider.GetRequiredService<ConfigServiceBase>());
        Assert.Empty(list.Data.Students);
        Assert.ThrowsAny<Exception>(() => list.Save());
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void MissingRosterLoad_DoesNotCreateDatabase()
    {
        using var provider = CreateProvider();
        _ = new StudentListConfig("missing", provider.GetRequiredService<ILogger<StudentListConfig>>(),
            provider.GetRequiredService<ConfigServiceBase>());
        Assert.False(File.Exists(ProfileRosterPaths.DatabasePath("missing", SecRandom.Core.Enums.ProfileRosterKind.RollCall)));
    }

    [Fact]
    public void ProfileServiceStartup_MigratesLegacyRostersForBothDrawKinds()
    {
        using var provider = CreateProvider();
        var legacyStudent = new StudentList("legacy-class");
        legacyStudent.Students.Add(new Student { Name = "旧学生" });
        var legacyPrize = new PrizeList("legacy-pool");
        legacyPrize.Prizes.Add(new Prize { Name = "旧奖品" });
        var studentPath = ProfileRosterPaths.LegacyJsonPath("legacy-class", ProfileRosterKind.RollCall);
        var prizePath = ProfileRosterPaths.LegacyJsonPath("legacy-pool", ProfileRosterKind.Lottery);
        Directory.CreateDirectory(Path.GetDirectoryName(studentPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(prizePath)!);
        File.WriteAllText(studentPath, JsonSerializer.Serialize(legacyStudent, ConfigServiceBase.JsonOptions));
        File.WriteAllText(prizePath, JsonSerializer.Serialize(legacyPrize, ConfigServiceBase.JsonOptions));

        var profile = provider.GetRequiredService<SecRandom.Core.Abstraction.Services.IProfileService>();
        var catalog = provider.GetRequiredService<SecRandom.Core.Abstraction.Services.IProfileCatalogManager>();

        Assert.Equal("legacy-class", profile.StudentListConfig?.Name);
        Assert.Equal("legacy-pool", profile.PrizeListConfig?.Name);
        Assert.Equal("旧学生", Assert.Single(catalog.LoadStudentList("legacy-class")!.Students).Name);
        Assert.Equal("旧奖品", Assert.Single(catalog.LoadPrizeList("legacy-pool")!.Prizes).Name);
        Assert.True(File.Exists(ProfileRosterPaths.DatabasePath(ProfileRosterKind.RollCall)));
        Assert.True(File.Exists(ProfileRosterPaths.DatabasePath(ProfileRosterKind.Lottery)));
        Assert.True(File.Exists(ProfileRosterPaths.LegacyBackupPath("legacy-class", ProfileRosterKind.RollCall)));
        Assert.True(File.Exists(ProfileRosterPaths.LegacyBackupPath("legacy-pool", ProfileRosterKind.Lottery)));
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
