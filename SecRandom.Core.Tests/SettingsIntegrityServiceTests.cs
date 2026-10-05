using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Security;

namespace SecRandom.Core.Tests;

public sealed class SettingsIntegrityServiceTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "settings-integrity-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_WhenTheCheckIsEnabled_RecordsTheWholeFileFingerprint()
    {
        var fixture = CreateFixture();
        fixture.SecuritySettings.SettingsIntegrityCheckEnabled = true;

        fixture.Handler.Save();

        var record = Assert.IsType<SettingsIntegrityRecord>(fixture.Store.LoadMetadata().SettingsIntegrity);
        Assert.Equal(ComputeDigest(fixture.ConfigPath), record.Digest);
        Assert.False(string.IsNullOrWhiteSpace(record.RecordedAppVersion));
        Assert.Equal(1, record.Generation);
        Assert.False(fixture.Service.GetPendingMismatch().HasValue);
    }

    [Fact]
    public void GetPendingMismatch_WhenTheFileChangesOutsideTheApp_ReportsTheRecordedTime()
    {
        var fixture = CreateFixture();
        fixture.SecuritySettings.SettingsIntegrityCheckEnabled = true;
        fixture.Handler.Save();
        var before = fixture.Store.LoadMetadata().SettingsIntegrity;

        File.AppendAllText(fixture.ConfigPath, Environment.NewLine);

        var mismatch = fixture.Service.GetPendingMismatch();
        Assert.True(mismatch.HasValue);
        Assert.Equal(before!.RecordedAtUtc, mismatch.Value.RecordedAtUtc);
        Assert.NotNull(mismatch.Value.FileModifiedAtUtc);
    }

    [Fact]
    public void AcceptCurrentFile_AfterAnExternalChange_AdoptsTheCurrentFile()
    {
        var fixture = CreateFixture();
        fixture.SecuritySettings.SettingsIntegrityCheckEnabled = true;
        fixture.Handler.Save();
        var original = fixture.Store.LoadMetadata().SettingsIntegrity;
        File.AppendAllText(fixture.ConfigPath, Environment.NewLine);
        Assert.True(fixture.Service.GetPendingMismatch().HasValue);

        fixture.Service.AcceptCurrentFile();

        var record = Assert.IsType<SettingsIntegrityRecord>(fixture.Store.LoadMetadata().SettingsIntegrity);
        Assert.Equal(ComputeDigest(fixture.ConfigPath), record.Digest);
        Assert.Equal(original!.Generation + 1, record.Generation);
        Assert.False(fixture.Service.GetPendingMismatch().HasValue);
    }

    [Fact]
    public void Save_WhenTheCheckIsDisabled_ClearsTheFingerprint()
    {
        var fixture = CreateFixture();
        fixture.SecuritySettings.SettingsIntegrityCheckEnabled = true;
        fixture.Handler.Save();
        Assert.True(fixture.Service.IsEnabled);

        fixture.SecuritySettings.SettingsIntegrityCheckEnabled = false;
        fixture.Handler.Save();

        Assert.False(fixture.Service.IsEnabled);
        Assert.Null(fixture.Store.LoadMetadata().SettingsIntegrity);
        Assert.False(fixture.Service.GetPendingMismatch().HasValue);
    }

    [Fact]
    public void GetPendingMismatch_WhenTheFileIsMissing_ReportsAMismatchInsteadOfPassing()
    {
        var fixture = CreateFixture();
        fixture.SecuritySettings.SettingsIntegrityCheckEnabled = true;
        fixture.Handler.Save();

        File.Delete(fixture.ConfigPath);

        Assert.True(fixture.Service.GetPendingMismatch().HasValue);
    }

    [Fact]
    public void Save_WhenNoSecurityPasswordExists_KeepsTheCheckUnavailable()
    {
        var fixture = CreateFixture(createCredentials: false);
        fixture.SecuritySettings.SettingsIntegrityCheckEnabled = true;

        fixture.Handler.Save();

        Assert.False(fixture.Service.IsEnabled);
        Assert.Null(fixture.Store.LoadMetadata().SettingsIntegrity);
    }

    [Fact]
    public void SaveSecuritySettings_WhenTheCheckIsEnabled_RecordsTheWholeFileFingerprint()
    {
        var fixture = CreateFixture();
        fixture.Handler.Save();

        // 开关自己住在加密的安全设置里：单独改它也要刷新指纹，否则开关打开后根本不会有记录，
        // 启动时 GetPendingMismatch 永远返回 null，校验等于没开。
        fixture.SecuritySettings.SettingsIntegrityCheckEnabled = true;
        fixture.SettingsStore.Save();

        var record = Assert.IsType<SettingsIntegrityRecord>(fixture.Store.LoadMetadata().SettingsIntegrity);
        Assert.Equal(ComputeDigest(fixture.ConfigPath), record.Digest);
        Assert.True(fixture.Service.IsEnabled);
    }

    [Fact]
    public void SaveSecuritySettings_WhenTheCheckIsDisabled_ClearsTheFingerprint()
    {
        var fixture = CreateFixture();
        fixture.Handler.Save();
        fixture.SecuritySettings.SettingsIntegrityCheckEnabled = true;
        fixture.SettingsStore.Save();
        Assert.True(fixture.Service.IsEnabled);

        fixture.SecuritySettings.SettingsIntegrityCheckEnabled = false;
        fixture.SettingsStore.Save();

        Assert.False(fixture.Service.IsEnabled);
        Assert.Null(fixture.Store.LoadMetadata().SettingsIntegrity);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
            Directory.Delete(_temporaryRoot, recursive: true);
    }

    private SettingsIntegrityFixture CreateFixture(bool createCredentials = true)
    {
        Directory.CreateDirectory(_temporaryRoot);
        var directory = Path.Combine(_temporaryRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "settings.json");
        var handler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new FileBackedConfigService(configPath));
        var store = new SecurityCredentialStore(
            Path.Combine(directory, "credentials.json"),
            CredentialKdfParameters.Test);
        if (createCredentials)
        {
            using var context = store.Create("secret1");
            store.Save(context);
        }

        var securitySettings = new SecuritySettingsStore(
            Path.Combine(directory, "security-settings", "settings.json"),
            handler,
            store,
            NullLogger<SecuritySettingsStore>.Instance);
        var service = new SettingsIntegrityService(
            handler,
            securitySettings,
            store,
            NullLogger<SettingsIntegrityService>.Instance,
            configPath);
        return new SettingsIntegrityFixture(handler, store, securitySettings, service, configPath);
    }

    private static string ComputeDigest(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed record SettingsIntegrityFixture(
        MainConfigHandler Handler,
        SecurityCredentialStore Store,
        SecuritySettingsStore SettingsStore,
        SettingsIntegrityService Service,
        string ConfigPath)
    {
        public SecuritySettingsConfig SecuritySettings => SettingsStore.Data;
    }

    private sealed class FileBackedConfigService(string path) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => File.Exists(path);

        public override T LoadConfig<T>(T fallback) => fallback;

        public override void SaveConfig<T>(T config) =>
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));

        public override void DeleteConfig<T>(T config)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
