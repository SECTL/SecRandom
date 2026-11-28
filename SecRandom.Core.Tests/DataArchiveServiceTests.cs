using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Archive;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.HistoryStore;
using SecRandom.Core.Services.Orchestration;
using SecRandom.Core.Services.Notification;
using SecRandom.Core.Services.Profiles;
using SecRandom.Core.Models.Orchestration;
using SecRandom.Shared;
using SecRandom.Core.Enums;

namespace SecRandom.Core.Tests;

public sealed class DataArchiveServiceTests : IDisposable
{
    // 测试宿主的入口程序集版本不是 v3，因此导出物会带上非 v3 的 producer_version；
    // 需要走通导入路径的用例先把导出物改盖为 v3 戳（等价于桌面真实产物），版本门用例则显式覆盖两个方向。
    private const string TestV3ProducerVersion = "v3.0.0";

    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "archive-tests", Guid.NewGuid().ToString("N"));
    private readonly string _exportDirectory = Path.Combine(Path.GetTempPath(), "SecRandom", "archive-test-exports", Guid.NewGuid().ToString("N"));

    public DataArchiveServiceTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
        Directory.CreateDirectory(_exportDirectory);
    }

    [Fact]
    public async Task ExportAllData_SnapshotsTextLibraryWithLiveWal()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        var path = Path.Combine(_dataRoot, "texts", "library" + SecRandom.Shared.Utils.TextLibraryDatabaseExtension);
        var service = new TextLibraryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<TextLibraryService>.Instance, path);
        service.ReplaceAll(["备份 {result}"]);
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode=WAL; UPDATE entries SET use_count=7";
            command.ExecuteNonQuery();
        }
        var destination = Path.Combine(_exportDirectory, "text-library.zip");
        await provider.GetRequiredService<DataArchiveService>().ExportAllDataAsync(destination, TestContext.Current.CancellationToken);
        using var exported = ZipFile.OpenRead(destination);
        Assert.Null(exported.GetEntry("texts/library" + SecRandom.Shared.Utils.TextLibraryDatabaseExtension + "-wal"));
        var entry = Assert.IsType<ZipArchiveEntry>(exported.GetEntry("texts/library" + SecRandom.Shared.Utils.TextLibraryDatabaseExtension));
        var extracted = Path.Combine(_exportDirectory, "snapshot" + SecRandom.Shared.Utils.TextLibraryDatabaseExtension);
        entry.ExtractToFile(extracted);
        var snapshot = new TextLibraryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<TextLibraryService>.Instance, extracted);
        Assert.Equal(7, snapshot.Entries.Single().UseCount);
    }

    [Fact]
    public async Task ExportAllData_IgnoresRemovedThemeResources()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        File.WriteAllText(Utils.GetFilePath("list", "roll_call_list", "class.json"), "{}");
        File.WriteAllText(Utils.GetFilePath("theme", "theme.json"), "{}");
        File.WriteAllText(Utils.GetFilePath("themes", "theme.json"), "{}");

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "all-data-themes.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);

        var paths = ReadManifestPaths(destination);
        Assert.Contains("list/roll_call.srl", paths);
        Assert.Contains("list/roll_call_list/class.json.migrated", paths);
        Assert.DoesNotContain(paths, path => path.StartsWith("theme", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportAllData_KeepsTheLogFileThatIsStillOpenForWriting()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        var logPath = Utils.GetFilePath("logs", "log-2024-1-1-0-0-0-1.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        // 复现 FileLoggerProvider 的持有方式：整个会话保持 ReadWrite + FileShare.Read，
        // 普通 File.OpenRead（FileShare.Read）会共享冲突并让整份归档导出失败
        using (var stream = new FileStream(logPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        using (var writer = new StreamWriter(stream) { AutoFlush = true })
        {
            writer.WriteLine("2024/1/1 0:00:00|Information|SecRandom|抽取完成");

            var archive = provider.GetRequiredService<DataArchiveService>();
            var destination = Path.Combine(_exportDirectory, "all-data-live-log.zip");
            await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);

            using var exported = ZipFile.OpenRead(destination);
            var entry = exported.GetEntry("logs/log-2024-1-1-0-0-0-1.log");
            Assert.NotNull(entry);
            Assert.Contains("抽取完成", ReadEntryText(entry));
        }
    }

    [Fact]
    public async Task ExportAllData_NeverCarriesTheSecurityDirectory()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        // 安全设置（加密的 settings.json）与安全凭据同住 config/security：任何备份都必须够不到它们，
        // 否则一份备份就能改掉这台机器的防护开关
        File.WriteAllText(Utils.GetFilePath("config", "security", "settings.json"), "{}");
        File.WriteAllText(Utils.GetFilePath("config", "security", "settings.key"), "{}");
        File.WriteAllText(Utils.GetFilePath("config", "security", "credentials.json"), "{}");
        File.WriteAllText(Utils.GetFilePath("config", "security", "totp-standalone.json"), "{}");

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "all-data-security.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);

        var paths = ReadManifestPaths(destination);
        Assert.DoesNotContain(paths, path => path.StartsWith("config/security", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportAllData_CarriesTheOrchestrationSection()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        var profileId = Guid.NewGuid().ToString("N");
        var store = new OrchestrationStore();
        store.Save(new OrchestrationProfile { Id = profileId, Name = "测试编排", SourceListName = "class-a", IsActive = true });
        store.SaveRuntime(new OrchestrationRuntime { ProfileId = profileId, Round = 2 });

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "all-data-orchestration.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);

        var paths = ReadManifestPaths(destination);
        Assert.Contains("config/orchestration.sro", paths);
        Assert.DoesNotContain(paths, path => path.StartsWith("config/orchestration/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportAllData_RestoresTheOrchestrationSection()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        var profileId = Guid.NewGuid().ToString("N");
        var databasePath = OrchestrationStore.DatabasePath();
        var store = new OrchestrationStore();
        store.Save(new OrchestrationProfile { Id = profileId, Name = "导入测试", SourceListName = "class-a" });

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "all-data-orchestration-import.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);
        StampProducerVersion(destination, TestV3ProducerVersion);

        // 编排段在 config/ 下是唯一按目录提交的根：折叠成 settings.json 会让整段顺序名单在导入时消失。
        File.Delete(databasePath);
        await archive.ImportAllDataAsync(destination, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(databasePath));
        Assert.Equal("导入测试", new OrchestrationStore().Load(profileId)?.Name);
    }

    [Fact]
    public async Task ImportAllData_RejectsACorruptOrchestrationFileBeforeTouchingData()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "all-data-orchestration-corrupt.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);
        StampProducerVersion(destination, TestV3ProducerVersion);
        AddArchiveEntry(destination, "config/orchestration/broken.json", Encoding.UTF8.GetBytes("{ broken"));

        await Assert.ThrowsAnyAsync<JsonException>(() =>
            archive.ImportAllDataAsync(destination, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Utils.GetFilePath("config", "orchestration", "broken.json")));
    }

    [Fact]
    public async Task ExportAndImportAllData_CarriesCentralRosterDatabases()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        var manager = provider.GetRequiredService<SecRandom.Core.Abstraction.Services.IProfileCatalogManager>();
        Assert.True(manager.CreateStudentList("class-a"));
        Assert.True(manager.ReplaceStudents("class-a", [new SecRandom.Shared.Models.Profile.Student { Name = "甲" }]));
        Assert.True(manager.CreatePrizeList("pool-a"));
        Assert.True(manager.ReplacePrizes("pool-a", [new SecRandom.Shared.Models.Profile.Prize { Name = "书" }]));

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "central-rosters.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);
        StampProducerVersion(destination, TestV3ProducerVersion);

        var paths = ReadManifestPaths(destination);
        Assert.Contains("list/roll_call.srl", paths);
        Assert.Contains("list/lottery.srl", paths);
        File.Delete(ProfileRosterPaths.DatabasePath(ProfileRosterKind.RollCall));
        File.Delete(ProfileRosterPaths.DatabasePath(ProfileRosterKind.Lottery));

        await archive.ImportAllDataAsync(destination, TestContext.Current.CancellationToken);

        Assert.Equal(["class-a"], manager.GetStudentListNames());
        Assert.Equal(["pool-a"], manager.GetPrizeListNames());
        Assert.Equal("甲", Assert.Single(manager.LoadStudentList("class-a")!.Students).Name);
        Assert.Equal("书", Assert.Single(manager.LoadPrizeList("pool-a")!.Prizes).Name);
    }

    [Fact]
    public async Task ImportAllData_SkipsLegacyThemeEntriesInsteadOfFailing()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "legacy-themes.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);
        StampProducerVersion(destination, TestV3ProducerVersion);
        AddArchiveEntry(destination, "themes/theme.json", Encoding.UTF8.GetBytes("{}"));

        var result = await archive.ImportAllDataAsync(destination, TestContext.Current.CancellationToken);

        Assert.True(result.ImportedFiles > 0);
        Assert.False(File.Exists(Utils.GetFilePath("themes", "theme.json")));
    }

    [Fact]
    public async Task ExportAndImportAllData_CarriesOpaqueRosterSqliteFilesAndLegacyJsonBackups()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        var archive = provider.GetRequiredService<DataArchiveService>();

        var listDirectory = Utils.GetDirectoryPath("list", "roll_call_list");
        var historyDirectory = Utils.GetDirectoryPath("history", "roll_call_history");
        Directory.CreateDirectory(listDirectory);
        Directory.CreateDirectory(historyDirectory);

        var rosterDatabase = Path.Combine(listDirectory, "class.srl");
        var rosterWal = rosterDatabase + "-wal";
        var rosterShm = rosterDatabase + "-shm";
        var legacyRoster = Path.Combine(listDirectory, "legacy.json");
        var legacyRosterBackup = legacyRoster + ".migrated";
        var legacyHistory = Path.Combine(historyDirectory, "legacy.json");
        var legacyHistoryBackup = legacyHistory + ".migrated";
        var databaseBytes = new byte[] { 0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x00, 0x01 };
        File.WriteAllBytes(rosterDatabase, databaseBytes);
        File.WriteAllBytes(rosterWal, [0x01, 0x02, 0x03]);
        File.WriteAllBytes(rosterShm, [0x04, 0x05]);
        File.WriteAllText(legacyRoster, "{}");
        File.WriteAllBytes(legacyRosterBackup, [0xFF, 0x00, 0xFF]);
        File.WriteAllText(legacyHistory, "{}");
        File.WriteAllBytes(legacyHistoryBackup, [0xFF, 0x00, 0xFF]);

        var destination = Path.Combine(_exportDirectory, "roster-sqlite-roundtrip.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);
        StampProducerVersion(destination, TestV3ProducerVersion);

        var paths = ReadManifestPaths(destination);
        Assert.Contains("list/roll_call_list/class.srl", paths);
        Assert.Contains("list/roll_call_list/class.srl-wal", paths);
        Assert.Contains("list/roll_call_list/class.srl-shm", paths);
        Assert.Contains("list/roll_call_list/legacy.json", paths);
        Assert.Contains("list/roll_call_list/legacy.json.migrated", paths);
        Assert.Contains("history/roll_call_history/legacy.json", paths);
        Assert.Contains("history/roll_call_history/legacy.json.migrated", paths);

        Directory.Delete(listDirectory, recursive: true);
        Directory.Delete(historyDirectory, recursive: true);
        await archive.ImportAllDataAsync(destination, TestContext.Current.CancellationToken);

        Assert.Equal(databaseBytes, File.ReadAllBytes(rosterDatabase));
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, File.ReadAllBytes(rosterWal));
        Assert.Equal(new byte[] { 0x04, 0x05 }, File.ReadAllBytes(rosterShm));
        Assert.False(File.Exists(legacyRoster));
        Assert.True(File.Exists(ProfileRosterPaths.DatabasePath("legacy", ProfileRosterKind.RollCall)));
        Assert.Equal(new byte[] { 0xFF, 0x00, 0xFF }, File.ReadAllBytes(legacyRosterBackup));
        Assert.False(File.Exists(legacyHistory));
        Assert.True(File.Exists(ProfileHistoryPaths.DatabasePath("legacy", HistoryGroupKind.RollCall)));
        Assert.Equal(new byte[] { 0xFF, 0x00, 0xFF }, File.ReadAllBytes(legacyHistoryBackup));
    }

    [Fact]
    public async Task ImportAllData_ValidatesLegacyJsonButTreatsSqliteAndMigratedBackupsAsOpaque()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "opaque-roster-files.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);
        StampProducerVersion(destination, TestV3ProducerVersion);
        AddArchiveEntry(destination, "list/roll_call_list/opaque.srl", [0x00, 0xFF, 0x01]);
        AddArchiveEntry(destination, "list/roll_call_list/opaque.srl-wal", [0x02, 0x03]);
        AddArchiveEntry(destination, "list/roll_call_list/opaque.srl-shm", [0x04, 0x05]);
        AddArchiveEntry(destination, "list/roll_call_list/opaque.json.migrated", [0xFF, 0x00, 0xFF]);

        var result = await archive.ImportAllDataAsync(destination, TestContext.Current.CancellationToken);

        Assert.True(result.ImportedFiles > 0);
        Assert.Equal(new byte[] { 0x00, 0xFF, 0x01 },
            File.ReadAllBytes(Utils.GetFilePath("list", "roll_call_list", "opaque.srl")));
        Assert.Equal(new byte[] { 0xFF, 0x00, 0xFF },
            File.ReadAllBytes(Utils.GetFilePath("list", "roll_call_list", "opaque.json.migrated")));
    }

    [Fact]
    public async Task ImportAllData_RejectsMalformedLegacyJsonBeforeReplacingRosterData()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        var existing = Utils.GetFilePath("list", "roll_call_list", "keep.json");
        File.WriteAllText(existing, "{}");

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "malformed-legacy-roster.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);
        StampProducerVersion(destination, TestV3ProducerVersion);
        var migratedBackup = existing + ".migrated";
        var databasePath = ProfileRosterPaths.DatabasePath(ProfileRosterKind.RollCall);
        var databaseBefore = File.ReadAllBytes(databasePath);
        AddArchiveEntry(destination, "list/roll_call_list/broken.json", Encoding.UTF8.GetBytes("{ broken"));

        await Assert.ThrowsAnyAsync<JsonException>(
            () => archive.ImportAllDataAsync(destination, TestContext.Current.CancellationToken));

        Assert.Equal("{}", File.ReadAllText(migratedBackup));
        Assert.Equal(databaseBefore, File.ReadAllBytes(databasePath));
        Assert.False(File.Exists(Utils.GetFilePath("list", "roll_call_list", "broken.json")));
    }

    [Fact]
    public async Task ExportAllData_WritesManifestWithMatchingHashesAndLengths()
    {
        using var provider = CreateProvider();
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Save();
        File.WriteAllText(Utils.GetFilePath("list", "roll_call_list", "class.json"), "{}");
        File.WriteAllText(Utils.GetFilePath("history", "roll_call_history", "class.json"), "{}");

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "all-data.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);

        int manifestFileCount;
        using (var zip = ZipFile.OpenRead(destination))
        {
            var manifestEntry = zip.GetEntry("manifest.json");
            Assert.NotNull(manifestEntry);
            var manifest = JsonSerializer.Deserialize<ArchiveManifest>(ReadEntryText(manifestEntry!));
            Assert.NotNull(manifest);
            Assert.Equal("secrandom-archive", manifest!.Format);
            Assert.Equal(1, manifest.SchemaVersion);
            Assert.Equal(ArchiveKind.AllData.ToString(), manifest.Kind);
            Assert.Equal(GlobalConstants.Version, manifest.ProducerVersion);
            Assert.Contains(manifest.Files, file => file.Path == "config/settings.json");
            Assert.Contains(manifest.Files, file => file.Path == "list/roll_call.srl");
            Assert.Contains(manifest.Files, file => file.Path == "list/roll_call_list/class.json.migrated");
            Assert.Contains(manifest.Files, file => file.Path.StartsWith("history/roll_call_history/", StringComparison.Ordinal));

            var dataEntries = zip.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name) && entry.FullName != "manifest.json").ToList();
            Assert.Equal(manifest.Files.Count, dataEntries.Count);
            foreach (var file in manifest.Files)
            {
                var entry = zip.GetEntry(file.Path);
                Assert.NotNull(entry);
                Assert.Equal(file.Length, entry!.Length);
                using var stream = entry.Open();
                Assert.Equal(file.Sha256, Convert.ToHexString(SHA256.HashData(stream)));
            }
            manifestFileCount = manifest.Files.Count;
        }

        StampProducerVersion(destination, TestV3ProducerVersion);
        var inspection = await archive.InspectAllDataAsync(destination, TestContext.Current.CancellationToken);
        Assert.True(inspection.IsSupportedV3);
        Assert.Equal(ArchiveKind.AllData, inspection.Kind);
        Assert.Equal(manifestFileCount, inspection.FileCount);
    }

    [Fact]
    public async Task InspectAllData_DetectsTamperedEntryHash()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        File.WriteAllText(Utils.GetFilePath("list", "roll_call_list", "class.json"), "{}");

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "tampered.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);

        RewriteArchive(destination, (name, bytes) =>
        {
            if (name != "list/roll_call.srl")
                return bytes;
            var tampered = (byte[])bytes.Clone();
            tampered[0] ^= 0xFF;
            return tampered;
        });

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => archive.InspectAllDataAsync(destination, TestContext.Current.CancellationToken));
        Assert.Contains("校验失败", exception.Message);
    }

    [Fact]
    public async Task V3ProducerVersionGate_RejectsNonV3Archives()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "v2-archive.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);

        StampProducerVersion(destination, "2.9.0");

        var inspection = await archive.InspectAllDataAsync(destination, TestContext.Current.CancellationToken);
        Assert.False(inspection.IsSupportedV3);
        Assert.Contains(inspection.Warnings, warning => warning.Contains("v3"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => archive.ImportAllDataAsync(destination, TestContext.Current.CancellationToken));
        Assert.Contains("仅支持", exception.Message);
        Assert.Contains("2.9.0", exception.Message);
    }

    [Fact]
    public async Task V3ProducerVersionGate_RejectsNonV3SettingsEnvelope()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "v2-settings.json");
        await archive.ExportSettingsAsync(destination, TestContext.Current.CancellationToken);

        StampProducerVersion(destination, "2.9.0");

        var inspection = await archive.InspectSettingsAsync(destination, TestContext.Current.CancellationToken);
        Assert.False(inspection.IsSupportedV3);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => archive.ImportSettingsAsync(destination, TestContext.Current.CancellationToken));
        Assert.Contains("仅支持", exception.Message);
    }

    [Fact]
    public async Task V3ProducerVersionGate_AcceptsPrereleaseArchiveFromTheSameBuild()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        File.WriteAllText(Utils.GetFilePath("list", "roll_call_list", "class.json"), "{}");

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "prerelease-archive.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);

        // 预发布版本号（例如 v3.0.0-alpha.2）会原样进入 producer_version，同版本必须能恢复自己的归档
        StampProducerVersion(destination, "v3.0.0-alpha.2");

        var inspection = await archive.InspectAllDataAsync(destination, TestContext.Current.CancellationToken);
        Assert.True(inspection.IsSupportedV3);
        Assert.Equal("v3.0.0-alpha.2", inspection.ProducerVersion);

        var result = await archive.ImportAllDataAsync(destination, TestContext.Current.CancellationToken);
        Assert.True(result.ImportedFiles > 0);
    }

    [Fact]
    public async Task V3ProducerVersionGate_AcceptsPrereleaseSettingsEnvelopeFromTheSameBuild()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "prerelease-settings.json");
        await archive.ExportSettingsAsync(destination, TestContext.Current.CancellationToken);
        StampProducerVersion(destination, "v3.0.0-alpha.2");

        var inspection = await archive.InspectSettingsAsync(destination, TestContext.Current.CancellationToken);
        Assert.True(inspection.IsSupportedV3);
        Assert.Equal("v3.0.0-alpha.2", inspection.ProducerVersion);
    }

    [Fact]
    public async Task ImportAllData_CommitsArchiveCreatesSnapshotAndInvokesHooks()
    {
        var hooks = new RecordingHooks();
        using var provider = CreateProvider(hooks);
        var config = provider.GetRequiredService<MainConfigHandler>();
        var exportedInterval = config.Data.General.Backup.AutoBackupIntervalDays;
        config.Save();
        var catalog = provider.GetRequiredService<SecRandom.Core.Abstraction.Services.IProfileCatalogManager>();
        Assert.True(catalog.CreateStudentList("class"));
        catalog.SetDefaultStudentList("class");
        var profile = provider.GetRequiredService<SecRandom.Core.Abstraction.Services.IProfileService>();
        profile.LoadStudentProfile("class", saveCurrent: false);
        Assert.True(catalog.ReplaceStudents("class", [new SecRandom.Shared.Models.Profile.Student { Name = "原名单" }]));

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "restore.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);
        StampProducerVersion(destination, TestV3ProducerVersion);
        var listPath = ProfileRosterPaths.DatabasePath(ProfileRosterKind.RollCall);
        // 导出即归档内容的真实来源（导出前服务会先落盘当前状态），以其为恢复基准。
        var exportedListContent = File.ReadAllBytes(listPath);

        Assert.True(catalog.ReplaceStudents("class", [new SecRandom.Shared.Models.Profile.Student { Name = "改动后" }]));
        config.Data.General.Backup.AutoBackupIntervalDays = exportedInterval + 3;
        config.Save();

        var result = await archive.ImportAllDataAsync(destination, TestContext.Current.CancellationToken);

        Assert.Equal(exportedListContent, File.ReadAllBytes(listPath));
        Assert.Equal("原名单", Assert.Single(catalog.LoadStudentList("class")!.Students).Name);
        Assert.Equal(exportedInterval, config.Data.General.Backup.AutoBackupIntervalDays);
        Assert.True(result.ImportedFiles > 0);
        Assert.False(string.IsNullOrEmpty(result.SnapshotPath));
        Assert.Contains("pre_import_all_data", Path.GetFileName(result.SnapshotPath));
        Assert.True(File.Exists(result.SnapshotPath));
        Assert.Equal(1, hooks.AllDataCalls);
        Assert.Equal(0, hooks.SettingsCalls);
        Assert.Contains("hook-warning-alldata", result.Warnings);
    }

    [Fact]
    public async Task ImportSettings_CommitsSettingsCreatesSnapshotAndInvokesHooks()
    {
        var hooks = new RecordingHooks();
        using var provider = CreateProvider(hooks);
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Data.General.Backup.AutoBackupIntervalDays = 7;
        config.Save();

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "settings.json");
        await archive.ExportSettingsAsync(destination, TestContext.Current.CancellationToken);
        StampProducerVersion(destination, TestV3ProducerVersion);

        config.Data.General.Backup.AutoBackupIntervalDays = 3;
        config.Save();

        var result = await archive.ImportSettingsAsync(destination, TestContext.Current.CancellationToken);

        Assert.Equal(7, config.Data.General.Backup.AutoBackupIntervalDays);
        Assert.Equal(1, result.ImportedFiles);
        Assert.Contains("pre_import_settings", Path.GetFileName(result.SnapshotPath));
        Assert.True(File.Exists(result.SnapshotPath));
        Assert.Equal(1, hooks.SettingsCalls);
        Assert.Equal(0, hooks.AllDataCalls);
        Assert.Contains("hook-warning-settings", result.Warnings);
    }

    [Theory]
    [InlineData("rollcall", false)]
    [InlineData("quickdraw", false)]
    [InlineData("lottery", false)]
    [InlineData("rollcall", true)]
    [InlineData("quickdraw", true)]
    [InlineData("lottery", true)]
    public async Task Import_RejectsUnsafeProfileDefaultsBeforeSnapshotOrSettingsMutation(string selection, bool zip)
    {
        var hooks = new RecordingHooks();
        using var provider = CreateProvider(hooks);
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Save();
        var archive = provider.GetRequiredService<DataArchiveService>();
        var source = Path.Combine(_exportDirectory, zip ? "unsafe-default.zip" : "unsafe-default.json");
        if (zip)
            await archive.ExportAllDataAsync(source, TestContext.Current.CancellationToken);
        else
            await archive.ExportSettingsAsync(source, TestContext.Current.CancellationToken);
        StampProducerVersion(source, TestV3ProducerVersion);
        var candidate = JsonSerializer.Deserialize<SecRandom.Core.Models.MainConfigModel>(
            JsonSerializer.Serialize(config.Data, ConfigServiceBase.JsonOptions), ConfigServiceBase.JsonOptions)!;
        switch (selection)
        {
            case "rollcall": candidate.RollCallSettings.DefaultClass = "../outside"; break;
            case "quickdraw": candidate.QuickDrawSettings.DefaultClass = "..\\outside"; break;
            case "lottery": candidate.LotterySettings.DefaultPool = "CON"; break;
        }
        var candidateBytes = JsonSerializer.SerializeToUtf8Bytes(candidate, ConfigServiceBase.JsonOptions);
        if (zip)
        {
            ArchiveManifest manifest;
            using (var original = ZipFile.OpenRead(source))
                manifest = ReadManifest(original);
            var settingsEntry = manifest.Files.Single(file => file.Path == "config/settings.json");
            settingsEntry.Length = candidateBytes.LongLength;
            settingsEntry.Sha256 = Convert.ToHexString(SHA256.HashData(candidateBytes));
            RewriteArchive(source, (name, bytes) => name switch
            {
                "config/settings.json" => candidateBytes,
                "manifest.json" => JsonSerializer.SerializeToUtf8Bytes(manifest),
                _ => bytes
            });
        }
        else
        {
            var envelope = JsonNode.Parse(File.ReadAllText(source))!;
            envelope["settings"] = JsonNode.Parse(candidateBytes);
            File.WriteAllText(source, envelope.ToJsonString());
        }
        var settingsPath = config.Data.ConfigFilePath;
        var settingsBefore = File.ReadAllText(settingsPath);
        var profile = provider.GetRequiredService<SecRandom.Core.Abstraction.Services.IProfileService>();
        var studentName = profile.StudentListConfig?.Name;
        var prizeName = profile.PrizeListConfig?.Name;

        await Assert.ThrowsAsync<InvalidDataException>(() => zip
            ? archive.ImportAllDataAsync(source, TestContext.Current.CancellationToken)
            : archive.ImportSettingsAsync(source, TestContext.Current.CancellationToken));

        Assert.Equal(settingsBefore, File.ReadAllText(settingsPath));
        Assert.Equal(studentName, profile.StudentListConfig?.Name);
        Assert.Equal(prizeName, profile.PrizeListConfig?.Name);
        Assert.Equal(0, hooks.SettingsCalls);
        Assert.Equal(0, hooks.AllDataCalls);
        var backup = Path.Combine(_dataRoot, "backup");
        Assert.False(Directory.Exists(backup) && Directory.EnumerateFiles(backup, "*pre_import*.zip").Any());
    }

    [Fact]
    public async Task ImportSettings_RejectsFilesOverTheTransferLimitBeforeStaging()
    {
        using var provider = CreateProvider();
        var archive = provider.GetRequiredService<DataArchiveService>();
        var source = Path.Combine(_exportDirectory, "oversized-settings.json");
        await using (var stream = new FileStream(source, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.SetLength(DataArchiveService.MaxTransferBytes + 1);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => archive.ImportSettingsAsync(source, TestContext.Current.CancellationToken));

        Assert.Contains("16 MiB", exception.Message);
        var staging = Path.Combine(_dataRoot, ".import-staging");
        Assert.False(Directory.Exists(staging) && Directory.EnumerateFiles(staging, "*.source").Any());
    }

    [Theory]
    [InlineData("config/settings.json/child")]
    [InlineData("config/device-uuid.json/child")]
    [InlineData("CONFIG/SETTINGS.JSON/child")]
    [InlineData("config\\device-uuid.json\\child")]
    [InlineData("config/settings.json/child/")]
    public async Task ImportAllData_RejectsDescendantsOfFixedFileRootsBeforeMutation(string entryPath)
    {
        using var provider = CreateProvider();
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Save();
        var identityPath = Utils.GetFilePath("config", "device-uuid.json");
        File.WriteAllText(identityPath, "local-device");
        var archive = provider.GetRequiredService<DataArchiveService>();
        var source = Path.Combine(_exportDirectory, "fixed-file-descendant.zip");
        await archive.ExportAllDataAsync(source, TestContext.Current.CancellationToken);
        StampProducerVersion(source, TestV3ProducerVersion);
        AddArchiveEntry(source, entryPath, Encoding.UTF8.GetBytes("child"));
        var settingsBefore = File.ReadAllBytes(config.Data.ConfigFilePath);

        var inspectionException = await Assert.ThrowsAsync<InvalidDataException>(
            () => archive.InspectAllDataAsync(source, TestContext.Current.CancellationToken));
        var importException = await Assert.ThrowsAsync<InvalidDataException>(
            () => archive.ImportAllDataAsync(source, TestContext.Current.CancellationToken));

        Assert.Contains("非法", inspectionException.Message);
        Assert.Contains("非法", importException.Message);

        Assert.Equal(settingsBefore, File.ReadAllBytes(config.Data.ConfigFilePath));
        Assert.Equal("local-device", File.ReadAllText(identityPath));
        var backup = Path.Combine(_dataRoot, "backup");
        Assert.False(Directory.Exists(backup) && Directory.EnumerateFiles(backup, "*pre_import*.zip").Any());
    }

    [Theory]
    [InlineData("config/settings.json")]
    [InlineData("config/device-uuid.json")]
    public void CommitCandidate_RestoresOldFixedFileWhenDirectoryCandidateCannotBeInstalled(string root)
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        var target = Path.Combine(_dataRoot, root.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(target, "old-file");
        var staging = Path.Combine(_dataRoot, ".import-staging", "directory-candidate");
        var candidate = Path.Combine(staging, root.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(candidate);
        File.WriteAllText(Path.Combine(candidate, "child"), "candidate");
        var archive = provider.GetRequiredService<DataArchiveService>();
        var method = typeof(DataArchiveService).GetMethod("CommitCandidate", BindingFlags.NonPublic | BindingFlags.Instance)!;
        IReadOnlyList<string> roots = [root];

        var exception = Assert.Throws<TargetInvocationException>(
            () => method.Invoke(archive, [staging, roots, false, new List<string>()]));

        Assert.NotNull(exception.InnerException);
        Assert.True(File.Exists(target));
        Assert.False(Directory.Exists(target));
        Assert.Equal("old-file", File.ReadAllText(target));
        Assert.True(Directory.Exists(candidate));
        Assert.False(File.Exists(Path.Combine(staging, "previous", root.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void CommitCandidate_RollsBackCommittedRootsWhenALaterRootFails()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        var keepPath = Utils.GetFilePath("list", "roll_call_list", "keep.json");
        File.WriteAllText(keepPath, "keep");

        var staging = Path.Combine(_dataRoot, ".import-staging", "rollback-test");
        Directory.CreateDirectory(staging);
        var stagedList = Path.Combine(staging, "list", "roll_call_list");
        Directory.CreateDirectory(stagedList);
        File.WriteAllText(Path.Combine(stagedList, "new.json"), "new");

        var archive = provider.GetRequiredService<DataArchiveService>();
        var method = typeof(DataArchiveService).GetMethod("CommitCandidate", BindingFlags.NonPublic | BindingFlags.Instance)!;
        IReadOnlyList<string> roots = new List<string> { "list", "../evil" };

        var exception = Assert.Throws<TargetInvocationException>(
            () => method.Invoke(archive, [staging, roots, false, new List<string>()]));

        Assert.IsType<InvalidDataException>(exception.InnerException);
        Assert.True(File.Exists(keepPath));
        Assert.Equal("keep", File.ReadAllText(keepPath));
        Assert.False(File.Exists(Path.Combine(_dataRoot, "list", "roll_call_list", "new.json")));
    }

    [Fact]
    public async Task ImportArchive_RejectsLegacyCloudBackupArchives()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "legacy-cloud-backup.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);
        StampProducerVersion(destination, TestV3ProducerVersion);
        StampManifestKind(destination, ArchiveKind.CloudBackup);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => archive.ImportAllDataAsync(destination, TestContext.Current.CancellationToken));

        Assert.Contains("云端备份已停止支持", exception.Message);
    }
    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
        if (Directory.Exists(_exportDirectory))
            Directory.Delete(_exportDirectory, recursive: true);
    }

    private static ServiceProvider CreateProvider(
        IArchivePostImportHooks? hooks = null,
        IArchivePreImportGuard? preImportGuard = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        if (hooks is not null)
            services.AddSingleton(hooks);
        if (preImportGuard is not null)
            services.AddSingleton(preImportGuard);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingPreImportGuard(bool allow) : IArchivePreImportGuard
    {
        public int Authorizations { get; private set; }

        public bool AuthorizeImport()
        {
            Authorizations++;
            return allow;
        }
    }

    private static IReadOnlyList<string> ReadManifestPaths(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        return ReadManifest(archive).Files.Select(file => file.Path).ToArray();
    }

    private static ArchiveKind ReadManifestKind(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        return Enum.Parse<ArchiveKind>(ReadManifest(archive).Kind);
    }

    private static ArchiveManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry("manifest.json")
                    ?? throw new InvalidOperationException("manifest.json was not found in the archive.");
        return JsonSerializer.Deserialize<ArchiveManifest>(ReadEntryText(entry))
               ?? throw new InvalidOperationException("manifest.json could not be read.");
    }

    [Fact]
    public async Task ImportSettings_WhenThePreImportGuardDenies_KeepsCurrentData()
    {
        var guard = new RecordingPreImportGuard(allow: false);
        using var provider = CreateProvider(preImportGuard: guard);
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Data.General.Backup.AutoBackupIntervalDays = 7;
        config.Save();

        var archive = provider.GetRequiredService<DataArchiveService>();
        var source = Path.Combine(_exportDirectory, "denied-import.json");
        await archive.ExportSettingsAsync(source, TestContext.Current.CancellationToken);
        StampProducerVersion(source, TestV3ProducerVersion);
        RewriteSettingsEnvelope(source, config, candidate => candidate.General.Backup.AutoBackupIntervalDays = 1);

        var settingsPath = config.Data.ConfigFilePath;
        var settingsBefore = File.ReadAllText(settingsPath);
        var backup = Path.Combine(_dataRoot, "backup");
        var backupsBefore = Directory.Exists(backup) ? Directory.GetFiles(backup).Length : 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => archive.ImportSettingsAsync(source, TestContext.Current.CancellationToken));

        Assert.Contains("安全验证", exception.Message);
        Assert.Equal(settingsBefore, File.ReadAllText(settingsPath));
        Assert.Equal(7, config.Data.General.Backup.AutoBackupIntervalDays);
        // 拒绝发生在快照之前：既不写数据也不留恢复包
        Assert.Equal(backupsBefore, Directory.Exists(backup) ? Directory.GetFiles(backup).Length : 0);
        Assert.Equal(1, guard.Authorizations);
    }

    [Fact]
    public async Task ImportSettings_WhenThePreImportGuardAllows_CommitsIt()
    {
        var guard = new RecordingPreImportGuard(allow: true);
        using var provider = CreateProvider(preImportGuard: guard);
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Data.General.Backup.AutoBackupIntervalDays = 7;
        config.Save();

        var archive = provider.GetRequiredService<DataArchiveService>();
        var source = Path.Combine(_exportDirectory, "allowed-import.json");
        await archive.ExportSettingsAsync(source, TestContext.Current.CancellationToken);
        StampProducerVersion(source, TestV3ProducerVersion);
        config.Data.General.Backup.AutoBackupIntervalDays = 3;
        config.Save();

        var result = await archive.ImportSettingsAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(7, config.Data.General.Backup.AutoBackupIntervalDays);
        Assert.Equal(1, guard.Authorizations);
        Assert.True(File.Exists(result.SnapshotPath));
    }

    [Fact]
    public async Task ImportAllData_AsksThePreImportGuardEvenWhenTheArchiveCarriesNoSettings()
    {
        var guard = new RecordingPreImportGuard(allow: true);
        using var provider = CreateProvider(preImportGuard: guard);
        var archive = provider.GetRequiredService<DataArchiveService>();
        // 只带名单的归档同样是一次整体替换：闸门不能因为"里面没有 settings.json"就跳过
        var source = archive.CreateManualBackup(["list"]);
        StampProducerVersion(source, TestV3ProducerVersion);

        await archive.ImportAllDataAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(1, guard.Authorizations);
    }

    /// <summary>
    ///     用当前的配置做一份副本、按需求改完再写回设置信封，用来构造「会放宽防护」的导入物。
    /// </summary>
    private static void RewriteSettingsEnvelope(
        string source,
        MainConfigHandler config,
        Action<SecRandom.Core.Models.MainConfigModel> mutate)
    {
        var candidate = JsonSerializer.Deserialize<SecRandom.Core.Models.MainConfigModel>(
            JsonSerializer.Serialize(config.Data, ConfigServiceBase.JsonOptions), ConfigServiceBase.JsonOptions)!;
        mutate(candidate);
        var envelope = JsonNode.Parse(File.ReadAllText(source))!;
        envelope["settings"] = JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(candidate, ConfigServiceBase.JsonOptions));
        File.WriteAllText(source, envelope.ToJsonString());
    }

    /// <summary>Appends one file entry to an existing archive and keeps its manifest consistent.</summary>
    private static void AddArchiveEntry(string archivePath, string entryName, byte[] content)
    {
        var temporaryPath = archivePath + ".adding";
        using (var source = ZipFile.OpenRead(archivePath))
        {
            var manifest = ReadManifest(source);
            manifest.Files.Add(new ArchiveFileEntry
            {
                Path = entryName,
                Length = content.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(content))
            });

            using var destination = ZipFile.Open(temporaryPath, ZipArchiveMode.Create);
            foreach (var entry in source.Entries)
            {
                if (entry.FullName == "manifest.json")
                {
                    var updated = destination.CreateEntry("manifest.json", CompressionLevel.SmallestSize);
                    using var updatedStream = updated.Open();
                    updatedStream.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest)));
                    continue;
                }

                using var input = entry.Open();
                using var memory = new MemoryStream();
                input.CopyTo(memory);
                var copy = destination.CreateEntry(entry.FullName, CompressionLevel.SmallestSize);
                using var copyStream = copy.Open();
                copyStream.Write(memory.ToArray());
            }

            var added = destination.CreateEntry(entryName, CompressionLevel.SmallestSize);
            using var addedStream = added.Open();
            addedStream.Write(content);
        }

        File.Delete(archivePath);
        File.Move(temporaryPath, archivePath);
    }

    private static void StampProducerVersion(string path, string producerVersion)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            RewriteArchive(path, (name, bytes) =>
            {
                if (name != "manifest.json")
                    return bytes;
                var node = JsonNode.Parse(Encoding.UTF8.GetString(bytes).TrimStart('﻿'))!;
                node["producer_version"] = producerVersion;
                return Encoding.UTF8.GetBytes(node.ToJsonString());
            });
            return;
        }

        var envelope = JsonNode.Parse(File.ReadAllText(path))!;
        envelope["producer_version"] = producerVersion;
        File.WriteAllText(path, envelope.ToJsonString());
    }

    private static void StampManifestKind(string path, ArchiveKind kind)
    {
        RewriteArchive(path, (name, bytes) =>
        {
            if (name != "manifest.json")
                return bytes;
            var node = JsonNode.Parse(Encoding.UTF8.GetString(bytes).TrimStart('﻿'))!;
            node["kind"] = kind.ToString();
            return Encoding.UTF8.GetBytes(node.ToJsonString());
        });
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void RewriteArchive(string sourcePath, Func<string, byte[], byte[]> transform)
    {
        var temporaryPath = sourcePath + ".rewriting";
        using (var source = ZipFile.OpenRead(sourcePath))
        using (var destination = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                using var input = entry.Open();
                using var memory = new MemoryStream();
                input.CopyTo(memory);
                var bytes = transform(entry.FullName, memory.ToArray());
                var newEntry = destination.CreateEntry(entry.FullName, CompressionLevel.SmallestSize);
                using var output = newEntry.Open();
                output.Write(bytes);
            }
        }
        File.Delete(sourcePath);
        File.Move(temporaryPath, sourcePath);
    }

    private sealed class RecordingHooks : IArchivePostImportHooks
    {
        public int SettingsCalls;
        public int AllDataCalls;

        public IReadOnlyList<string> OnSettingsImported()
        {
            SettingsCalls++;
            return ["hook-warning-settings"];
        }

        public IReadOnlyList<string> OnAllDataImported()
        {
            AllDataCalls++;
            return ["hook-warning-alldata"];
        }
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
