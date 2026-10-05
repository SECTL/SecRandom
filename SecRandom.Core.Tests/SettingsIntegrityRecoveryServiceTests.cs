using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Archive;
using SecRandom.Core.Services.Config;
using SecRandom.Services.ImportExport;
using SecRandom.Services.Security;

namespace SecRandom.Core.Tests;

public sealed class SettingsIntegrityRecoveryServiceTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(), "SecRandom", "settings-recovery-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TryRecoverAsync_RestoresTheNewestLocalBackupAndKeepsThePreviousFile()
    {
        var fixture = CreateFixture(SettingsIntegrityRestoreSource.Local);
        var tampered = WriteCurrentSettings(fixture, SettingsIntegrityRestoreSource.CloudThenLocal);
        WriteBackup(fixture, "SecRandom_auto_old.zip", CreateSettings(SettingsIntegrityRestoreSource.Local),
            DateTimeOffset.UtcNow.AddDays(-2));
        var newest = WriteBackup(fixture, "SecRandom_auto_new.zip",
            CreateSettings(SettingsIntegrityRestoreSource.LocalThenCloud), DateTimeOffset.UtcNow.AddDays(-1));

        var result = await fixture.Service.TryRecoverAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.False(result.FromCloud);
        Assert.Equal(newest, result.BackupName);
        Assert.NotNull(result.PreRestorePath);
        Assert.Equal(tampered, File.ReadAllText(result.PreRestorePath!));
        Assert.Equal(BackupMarker(SettingsIntegrityRestoreSource.LocalThenCloud),
            fixture.Handler.Data.General.Backup.AutoBackupIntervalDays);
    }

    [Fact]
    public async Task TryRecoverAsync_SkipsBackupsWithoutASettingsFile()
    {
        var fixture = CreateFixture(SettingsIntegrityRestoreSource.Local);
        WriteCurrentSettings(fixture, SettingsIntegrityRestoreSource.Local);
        WriteBackup(fixture, "SecRandom_manual_other.zip", null, DateTimeOffset.UtcNow);
        var usable = WriteBackup(fixture, "SecRandom_auto_usable.zip",
            CreateSettings(SettingsIntegrityRestoreSource.CloudThenLocal), DateTimeOffset.UtcNow.AddDays(-1));

        var result = await fixture.Service.TryRecoverAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(usable, result.BackupName);
        Assert.Equal(BackupMarker(SettingsIntegrityRestoreSource.CloudThenLocal),
            fixture.Handler.Data.General.Backup.AutoBackupIntervalDays);
    }

    [Fact]
    public async Task TryRecoverAsync_SkipsBackupsWhoseSettingsFileCannotBeLoaded()
    {
        var fixture = CreateFixture(SettingsIntegrityRestoreSource.Local);
        WriteCurrentSettings(fixture, SettingsIntegrityRestoreSource.Local);
        WriteRawBackup(fixture, "SecRandom_auto_broken.zip", "{ not json", DateTimeOffset.UtcNow);
        var usable = WriteBackup(fixture, "SecRandom_auto_usable.zip",
            CreateSettings(SettingsIntegrityRestoreSource.CloudThenLocal), DateTimeOffset.UtcNow.AddDays(-1));

        var result = await fixture.Service.TryRecoverAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(usable, result.BackupName);
        Assert.Equal(BackupMarker(SettingsIntegrityRestoreSource.CloudThenLocal),
            fixture.Handler.Data.General.Backup.AutoBackupIntervalDays);
    }

    [Fact]
    public async Task TryRecoverAsync_WithoutAUsableBackup_KeepsTheCurrentFileAndReportsNoBackup()
    {
        var fixture = CreateFixture(SettingsIntegrityRestoreSource.Local);
        var tampered = WriteCurrentSettings(fixture, SettingsIntegrityRestoreSource.Local);

        var result = await fixture.Service.TryRecoverAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(SettingsIntegrityRecoveryStatus.NoBackupAvailable, result.Status);
        Assert.Equal(tampered, File.ReadAllText(fixture.ConfigPath));
        Assert.False(Directory.Exists(Path.Combine(fixture.BackupDirectory, "integrity")));
    }

    [Fact]
    public async Task TryRecoverAsync_WhenCloudIsPreferredButNoAccountIsSignedIn_ReportsTheCloudReason()
    {
        var fixture = CreateFixture(SettingsIntegrityRestoreSource.CloudThenLocal);
        WriteCurrentSettings(fixture, SettingsIntegrityRestoreSource.CloudThenLocal);

        var result = await fixture.Service.TryRecoverAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(SettingsIntegrityRecoveryStatus.CloudNotSignedIn, result.Status);
    }

    [Fact]
    public async Task TryRecoverAsync_WhenLocalIsPrimary_DoesNotReportTheCloudReason()
    {
        var fixture = CreateFixture(SettingsIntegrityRestoreSource.LocalThenCloud);
        WriteCurrentSettings(fixture, SettingsIntegrityRestoreSource.LocalThenCloud);

        var result = await fixture.Service.TryRecoverAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(SettingsIntegrityRecoveryStatus.NoBackupAvailable, result.Status);
    }

    [Fact]
    public void OrderCloudCandidates_UsesOnlyThisDevicesBackupsEvenWhenAnotherDeviceHasANewerOne()
    {
        var own = CloudBackup("own", "dev-a", DateTimeOffset.UtcNow.AddDays(-5));
        var other = CloudBackup("other", "dev-b", DateTimeOffset.UtcNow);

        var ordered = SettingsIntegrityRecoveryService.OrderCloudCandidates([other, own], "dev-a");

        // Another machine's settings.json describes that machine, so it is never a restore candidate.
        Assert.Equal(["own"], ordered.Select(item => item.BackupId));
    }

    [Fact]
    public void OrderCloudCandidates_OrdersThisDevicesBackupsNewestFirst()
    {
        var ownOlder = CloudBackup("own-old", "dev-a", DateTimeOffset.UtcNow.AddDays(-3));
        var ownNewer = CloudBackup("own-new", "dev-a", DateTimeOffset.UtcNow.AddDays(-1));
        var other = CloudBackup("other", "dev-b", DateTimeOffset.UtcNow);

        var ordered = SettingsIntegrityRecoveryService.OrderCloudCandidates([ownOlder, other, ownNewer], "dev-a");

        Assert.Equal(["own-new", "own-old"], ordered.Select(item => item.BackupId));
    }

    [Fact]
    public void OrderCloudCandidates_WithoutThisDevicesBackups_ReturnsNothing()
    {
        var older = CloudBackup("other-old", "dev-b", DateTimeOffset.UtcNow.AddDays(-2));
        var newer = CloudBackup("other-new", "dev-c", DateTimeOffset.UtcNow);

        var ordered = SettingsIntegrityRecoveryService.OrderCloudCandidates([older, newer], "dev-a");

        // No own backup means no recovery from the cloud at all: the local file stays untouched.
        Assert.Empty(ordered);
    }

    [Fact]
    public void OrderCloudCandidates_SkipsIncompleteBackups()
    {
        var incomplete = CloudBackup("half", "dev-a", DateTimeOffset.UtcNow, complete: false);
        var usable = CloudBackup("usable", "dev-a", DateTimeOffset.UtcNow.AddDays(-1));

        var ordered = SettingsIntegrityRecoveryService.OrderCloudCandidates([incomplete, usable], "dev-a");

        Assert.Equal(["usable"], ordered.Select(item => item.BackupId));
    }

    [Fact]
    public void OrderCloudCandidates_WithoutADeviceTag_ReturnsNothing()
    {
        // A host name that yields no ASCII-safe alias means no backup can be attributed to this device.
        var older = CloudBackup("older", "dev-a", DateTimeOffset.UtcNow.AddDays(-1));
        var newer = CloudBackup("newer", "dev-b", DateTimeOffset.UtcNow);

        var ordered = SettingsIntegrityRecoveryService.OrderCloudCandidates([older, newer], string.Empty);

        Assert.Empty(ordered);
    }

    private static CloudBackupDescriptor CloudBackup(string backupId, string deviceTag, DateTimeOffset createdAt,
        bool complete = true) =>
        new(backupId, $"{backupId} ({deviceTag})", createdAt, 10, 1, complete,
            complete ? $"manifest-{backupId}" : null, deviceTag);

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
            Directory.Delete(_temporaryRoot, recursive: true);
    }

    private RecoveryFixture CreateFixture(SettingsIntegrityRestoreSource source)
    {
        var directory = Path.Combine(_temporaryRoot, Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(directory, "config", "settings.json");
        var backupDirectory = Path.Combine(directory, "backup");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        Directory.CreateDirectory(backupDirectory);

        var handler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new JsonFileConfigService(configPath));
        // 安全设置住在自己的加密文件里，settings.json 的恢复不再碰它
        var securitySettings = new SecuritySettingsStore(
            Path.Combine(directory, "security", "settings.json"),
            handler,
            credentialStore: null,
            NullLogger<SecuritySettingsStore>.Instance);
        securitySettings.Data.SettingsIntegrityRestoreSource = source;
        var service = new SettingsIntegrityRecoveryService(
            handler,
            securitySettings,
            new StubImportExportService(),
            NullLogger<SettingsIntegrityRecoveryService>.Instance,
            configPath,
            backupDirectory);
        return new RecoveryFixture(handler, securitySettings, service, configPath, backupDirectory);
    }

    private static string WriteCurrentSettings(RecoveryFixture fixture, SettingsIntegrityRestoreSource source)
    {
        var content = JsonSerializer.Serialize(CreateSettings(source), ConfigServiceBase.JsonOptions);
        File.WriteAllText(fixture.ConfigPath, content);
        return content;
    }

    private static string WriteBackup(RecoveryFixture fixture, string fileName, MainConfigModel? settings,
        DateTimeOffset createdAt)
    {
        using var archive = ZipFile.Open(Path.Combine(fixture.BackupDirectory, fileName), ZipArchiveMode.Create);
        if (settings is not null)
        {
            using var stream = archive.CreateEntry(SettingsIntegrityRecoveryService.SettingsEntryPath).Open();
            stream.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings, ConfigServiceBase.JsonOptions)));
        }

        return Stamp(fixture, fileName, createdAt);
    }

    private static string WriteRawBackup(RecoveryFixture fixture, string fileName, string settingsJson,
        DateTimeOffset createdAt)
    {
        using var archive = ZipFile.Open(Path.Combine(fixture.BackupDirectory, fileName), ZipArchiveMode.Create);
        using (var stream = archive.CreateEntry(SettingsIntegrityRecoveryService.SettingsEntryPath).Open())
            stream.Write(Encoding.UTF8.GetBytes(settingsJson));

        return Stamp(fixture, fileName, createdAt);
    }

    private static string Stamp(RecoveryFixture fixture, string fileName, DateTimeOffset createdAt)
    {
        var path = Path.Combine(fixture.BackupDirectory, fileName);
        File.SetCreationTimeUtc(path, createdAt.UtcDateTime);
        File.SetLastWriteTimeUtc(path, createdAt.UtcDateTime);
        return fileName;
    }

    private static MainConfigModel CreateSettings(SettingsIntegrityRestoreSource source)
    {
        var settings = new MainConfigModel();
        // 安全设置已经不随备份走，所以这里用一个普通的设置值当"这是哪一份备份"的标记
        settings.General.Backup.AutoBackupIntervalDays = BackupMarker(source);
        return settings;
    }

    /// <summary>每份备份写一个不同的标记值，恢复后必须等于所选用备份里的那个值。</summary>
    private static int BackupMarker(SettingsIntegrityRestoreSource source) => (int)source + 1;

    private sealed record RecoveryFixture(
        MainConfigHandler Handler,
        SecuritySettingsStore SecuritySettings,
        SettingsIntegrityRecoveryService Service,
        string ConfigPath,
        string BackupDirectory);

    private sealed class JsonFileConfigService(string path) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => File.Exists(path);

        public override T LoadConfig<T>(T fallback) =>
            File.Exists(path)
                ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? fallback
                : fallback;

        public override void SaveConfig<T>(T config) =>
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));

        public override void DeleteConfig<T>(T config)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>
    ///     归档校验在恢复流程里只决定「这个候选能不能用」；单元测试关注设置文件的读取与覆盖，
    ///     因此这里固定返回受支持的 v3 归档，真实的归档校验由 Core 归档测试覆盖。
    /// </summary>
    private sealed class StubImportExportService : IImportExportService
    {
        public Task<ImportInspection> InspectAllDataAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Supported());

        public Task<ImportInspection> InspectCloudBackupAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Supported());

        public Task<string> ExportDiagnosticAsync(string destinationPath, bool includeExtendedData = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> ExportSettingsAsync(string destinationPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> ExportAllDataAsync(string destinationPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> ExportCloudBackupAsync(string destinationPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IReadOnlyList<string> GetCloudBackupRoots() => [];

        public Task<ImportInspection> InspectSettingsAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ImportResult> ImportSettingsAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ImportResult> ImportAllDataAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ImportResult> ImportCloudBackupAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public string CreateManualBackup(IReadOnlyCollection<string> roots) => throw new NotSupportedException();

        public string CreateAutomaticBackup(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ImportResult> RestoreBackupAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static ImportInspection Supported() =>
            new(ArchiveFormat.Current, ArchiveKind.AutomaticBackup, "v3.0.0", 1, 1, [], []);
    }
}
