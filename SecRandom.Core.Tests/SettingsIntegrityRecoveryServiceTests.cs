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
        Assert.Equal(SettingsIntegrityRestoreSource.LocalThenCloud,
            fixture.Handler.Data.SecuritySettings.SettingsIntegrityRestoreSource);
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
        Assert.Equal(SettingsIntegrityRestoreSource.CloudThenLocal,
            fixture.Handler.Data.SecuritySettings.SettingsIntegrityRestoreSource);
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
        Assert.Equal(SettingsIntegrityRestoreSource.CloudThenLocal,
            fixture.Handler.Data.SecuritySettings.SettingsIntegrityRestoreSource);
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
        handler.Data.SecuritySettings.SettingsIntegrityRestoreSource = source;
        var service = new SettingsIntegrityRecoveryService(
            handler,
            new StubImportExportService(),
            NullLogger<SettingsIntegrityRecoveryService>.Instance,
            configPath,
            backupDirectory);
        return new RecoveryFixture(handler, service, configPath, backupDirectory);
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
        settings.SecuritySettings.SettingsIntegrityRestoreSource = source;
        return settings;
    }

    private sealed record RecoveryFixture(
        MainConfigHandler Handler,
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
