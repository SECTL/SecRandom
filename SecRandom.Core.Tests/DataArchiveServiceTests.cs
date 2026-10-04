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
using SecRandom.Shared;

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
        Assert.Contains("list/roll_call_list/class.json", paths);
        Assert.DoesNotContain(paths, path => path.StartsWith("theme", StringComparison.OrdinalIgnoreCase));
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
            Assert.Contains(manifest.Files, file => file.Path == "list/roll_call_list/class.json");
            Assert.Contains(manifest.Files, file => file.Path == "history/roll_call_history/class.json");

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
            if (name != "list/roll_call_list/class.json")
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
        var listPath = Utils.GetFilePath("list", "roll_call_list", "class.json");
        File.WriteAllText(listPath, "{}");

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "restore.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);
        StampProducerVersion(destination, TestV3ProducerVersion);
        // 导出即归档内容的真实来源（导出前服务会先落盘当前状态），以其为恢复基准。
        var exportedListContent = File.ReadAllText(listPath);

        File.WriteAllText(listPath, "{\"changed\":true}");
        config.Data.General.Backup.AutoBackupIntervalDays = exportedInterval + 3;
        config.Save();

        var result = await archive.ImportAllDataAsync(destination, TestContext.Current.CancellationToken);

        Assert.Equal(exportedListContent, File.ReadAllText(listPath));
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
        var studentName = profile.StudentListConfig!.Name;
        var prizeName = profile.PrizeListConfig!.Name;

        await Assert.ThrowsAsync<InvalidDataException>(() => zip
            ? archive.ImportAllDataAsync(source, TestContext.Current.CancellationToken)
            : archive.ImportSettingsAsync(source, TestContext.Current.CancellationToken));

        Assert.Equal(settingsBefore, File.ReadAllText(settingsPath));
        Assert.Equal(studentName, profile.StudentListConfig.Name);
        Assert.Equal(prizeName, profile.PrizeListConfig.Name);
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
    public async Task ExportCloudBackup_ExcludesDeviceIdentityVoiceCacheLogsAndPlugins()
    {
        using var provider = CreateProvider();
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Data.General.Backup.CloudIncludeAudio = true;
        // The local selection may include logs, audio, and configuration with the device identity;
        // none of those may leak into a cloud archive.
        config.Data.General.Backup.IncludeLogs = true;
        config.Data.General.Backup.IncludeAudio = true;
        config.Data.General.Backup.IncludeConfig = true;
        config.Save();
        File.WriteAllText(Utils.GetFilePath("config", "device-uuid.json"), "{\"device_uuid\":\"device-1\"}");
        File.WriteAllText(Utils.GetFilePath("list", "roll_call_list", "class.json"), "{}");
        File.WriteAllText(Utils.GetFilePath("history", "roll_call_history", "class.json"), "{}");
        File.WriteAllText(Utils.GetFilePath("audio", "music", "track.mp3"), "music");
        File.WriteAllText(Utils.GetFilePath("audio", "voice", "cache.mp3"), "voice-cache");
        File.WriteAllText(Utils.GetFilePath("logs", "app.log"), "log");

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "cloud.zip");
        await archive.ExportCloudBackupAsync(destination, TestContext.Current.CancellationToken);

        var paths = ReadManifestPaths(destination);
        Assert.Equal(ArchiveKind.CloudBackup, ReadManifestKind(destination));
        Assert.Contains("config/settings.json", paths);
        Assert.Contains("list/roll_call_list/class.json", paths);
        Assert.Contains("history/roll_call_history/class.json", paths);
        Assert.Contains("audio/music/track.mp3", paths);
        Assert.DoesNotContain("config/device-uuid.json", paths);
        Assert.DoesNotContain("audio/voice/cache.mp3", paths);
        Assert.DoesNotContain("logs/app.log", paths);
        Assert.DoesNotContain(paths, path => path.StartsWith("plugins", StringComparison.OrdinalIgnoreCase));

        StampProducerVersion(destination, TestV3ProducerVersion);
        var inspection = await archive.InspectCloudBackupAsync(destination, TestContext.Current.CancellationToken);
        Assert.True(inspection.IsSupportedV3);
        Assert.Equal(ArchiveKind.CloudBackup, inspection.Kind);
    }

    [Fact]
    public async Task ExportCloudBackup_FollowsTheCloudContentSelection()
    {
        using var provider = CreateProvider();
        var config = provider.GetRequiredService<MainConfigHandler>();
        // The cloud selection is independent: keep settings only, and a checked log option still
        // never reaches the cloud.
        config.Data.General.Backup.CloudIncludeConfig = true;
        config.Data.General.Backup.CloudIncludeList = false;
        config.Data.General.Backup.CloudIncludeHistory = false;
        config.Data.General.Backup.CloudIncludeAudio = false;
        config.Data.General.Backup.CloudIncludeCses = false;
        config.Data.General.Backup.CloudIncludeImages = false;
        config.Data.General.Backup.IncludeLogs = true;
        config.Save();
        File.WriteAllText(Utils.GetFilePath("list", "roll_call_list", "class.json"), "{}");
        File.WriteAllText(Utils.GetFilePath("proofs", "draw.srproof.json"), "{}");
        File.WriteAllText(Utils.GetFilePath("theme", "theme.json"), "{}");
        File.WriteAllText(Utils.GetFilePath("logs", "app.log"), "log");

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "cloud-selection.zip");
        await archive.ExportCloudBackupAsync(destination, TestContext.Current.CancellationToken);

        var paths = ReadManifestPaths(destination);
        Assert.Contains("config/settings.json", paths);
        Assert.DoesNotContain("list/roll_call_list/class.json", paths);
        Assert.DoesNotContain("proofs/draw.srproof.json", paths);
        Assert.DoesNotContain("theme/theme.json", paths);
        Assert.DoesNotContain("logs/app.log", paths);

        var roots = archive.GetCloudBackupRoots();
        Assert.Contains("config/settings.json", roots);
        Assert.DoesNotContain("list", roots);
        Assert.DoesNotContain("proofs", roots);
        Assert.DoesNotContain("themes", roots);
        Assert.DoesNotContain("logs", roots);
        Assert.DoesNotContain("config/device-uuid.json", roots);
    }

    [Fact]
    public async Task CloudBackupCarriesProofsOnlyWhenTheSelectionAsksForThem()
    {
        using var provider = CreateProvider();
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Data.General.Backup.CloudIncludeConfig = false;
        config.Data.General.Backup.CloudIncludeList = false;
        config.Data.General.Backup.CloudIncludeHistory = false;
        config.Data.General.Backup.CloudIncludeCses = false;
        config.Data.General.Backup.CloudIncludeProofs = true;
        config.Save();
        File.WriteAllText(Utils.GetFilePath("proofs", "draw.srproof.json"), "{}");

        var archive = provider.GetRequiredService<DataArchiveService>();
        Assert.Contains("proofs", archive.GetCloudBackupRoots());

        var destination = Path.Combine(_exportDirectory, "cloud-proofs.zip");
        await archive.ExportCloudBackupAsync(destination, TestContext.Current.CancellationToken);

        Assert.Contains("proofs/draw.srproof.json", ReadManifestPaths(destination));
    }

    [Fact]
    public async Task InspectCloudBackup_RejectsArchivesOfOtherKinds()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "all-data.zip");
        await archive.ExportAllDataAsync(destination, TestContext.Current.CancellationToken);
        StampProducerVersion(destination, TestV3ProducerVersion);

        var inspection = await archive.InspectCloudBackupAsync(destination, TestContext.Current.CancellationToken);

        Assert.False(inspection.IsSupportedV3);
        Assert.Contains(inspection.Warnings, warning => warning.Contains("云端备份"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => archive.ImportCloudBackupAsync(destination, TestContext.Current.CancellationToken));
        Assert.Contains("云端备份", exception.Message);
    }

    [Fact]
    public async Task ImportCloudBackup_RestoresDataAndKeepsDeviceIdentity()
    {
        var hooks = new RecordingHooks();
        using var provider = CreateProvider(hooks);
        var config = provider.GetRequiredService<MainConfigHandler>();
        var exportedInterval = config.Data.General.Backup.AutoBackupIntervalDays;
        config.Save();
        var listPath = Utils.GetFilePath("list", "roll_call_list", "class.json");
        File.WriteAllText(listPath, "{}");

        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "cloud-restore.zip");
        await archive.ExportCloudBackupAsync(destination, TestContext.Current.CancellationToken);
        StampProducerVersion(destination, TestV3ProducerVersion);
        var exportedListContent = File.ReadAllText(listPath);

        var identity = Utils.GetFilePath("config", "device-uuid.json");
        File.WriteAllText(identity, "{\"device_uuid\":\"local-device\"}");
        File.WriteAllText(listPath, "{\"changed\":true}");
        config.Data.General.Backup.AutoBackupIntervalDays = exportedInterval + 3;
        config.Save();

        var result = await archive.ImportCloudBackupAsync(destination, TestContext.Current.CancellationToken);

        Assert.Equal(exportedListContent, File.ReadAllText(listPath));
        Assert.Equal(exportedInterval, config.Data.General.Backup.AutoBackupIntervalDays);
        Assert.Equal("{\"device_uuid\":\"local-device\"}", File.ReadAllText(identity));
        Assert.True(result.ImportedFiles > 0);
        Assert.Contains("pre_import_all_data", Path.GetFileName(result.SnapshotPath));
        Assert.Equal(1, hooks.AllDataCalls);
    }

    [Fact]
    public async Task ImportCloudBackup_NeverCommitsACraftedDeviceIdentityEntry()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<MainConfigHandler>().Save();
        var archive = provider.GetRequiredService<DataArchiveService>();
        var destination = Path.Combine(_exportDirectory, "cloud-identity.zip");
        await archive.ExportCloudBackupAsync(destination, TestContext.Current.CancellationToken);
        StampProducerVersion(destination, TestV3ProducerVersion);

        var identity = Utils.GetFilePath("config", "device-uuid.json");
        File.WriteAllText(identity, "{\"device_uuid\":\"local-device\"}");
        AddArchiveEntry(destination, "config/device-uuid.json", Encoding.UTF8.GetBytes("{\"device_uuid\":\"other-device\"}"));

        var inspection = await archive.InspectCloudBackupAsync(destination, TestContext.Current.CancellationToken);
        Assert.Contains("config/device-uuid.json", inspection.Roots);

        await archive.ImportCloudBackupAsync(destination, TestContext.Current.CancellationToken);

        Assert.Equal("{\"device_uuid\":\"local-device\"}", File.ReadAllText(identity));
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
        public List<SecRandom.Core.Models.SubConfigs.SecuritySettingsConfig> Candidates { get; } = [];

        public bool AuthorizeSecuritySettings(SecRandom.Core.Models.SubConfigs.SecuritySettingsConfig candidate)
        {
            Candidates.Add(candidate);
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
    public async Task ImportSettings_WhenThePreImportGuardDeniesTheLoosenedConfiguration_KeepsCurrentData()
    {
        var guard = new RecordingPreImportGuard(allow: false);
        using var provider = CreateProvider(preImportGuard: guard);
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Data.SecuritySettings.SecurityEnabled = true;
        config.Data.SecuritySettings.ProtectExit = true;
        config.Save();

        var archive = provider.GetRequiredService<DataArchiveService>();
        var source = Path.Combine(_exportDirectory, "denied-downgrade.json");
        await archive.ExportSettingsAsync(source, TestContext.Current.CancellationToken);
        StampProducerVersion(source, TestV3ProducerVersion);
        RewriteSettingsEnvelope(source, config, candidate =>
        {
            candidate.SecuritySettings.SecurityEnabled = false;
            candidate.SecuritySettings.ProtectExit = false;
        });

        var settingsPath = config.Data.ConfigFilePath;
        var settingsBefore = File.ReadAllText(settingsPath);
        var backup = Path.Combine(_dataRoot, "backup");
        var backupsBefore = Directory.Exists(backup) ? Directory.GetFiles(backup).Length : 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => archive.ImportSettingsAsync(source, TestContext.Current.CancellationToken));

        Assert.Contains("安全验证", exception.Message);
        Assert.Equal(settingsBefore, File.ReadAllText(settingsPath));
        Assert.True(config.Data.SecuritySettings.SecurityEnabled);
        Assert.True(config.Data.SecuritySettings.ProtectExit);
        // 拒绝发生在快照之前：既不写数据也不留恢复包
        Assert.Equal(backupsBefore, Directory.Exists(backup) ? Directory.GetFiles(backup).Length : 0);

        var candidate = Assert.Single(guard.Candidates);
        Assert.False(candidate.SecurityEnabled);
    }

    [Fact]
    public async Task ImportSettings_WhenThePreImportGuardAllowsTheConfiguration_CommitsIt()
    {
        var guard = new RecordingPreImportGuard(allow: true);
        using var provider = CreateProvider(preImportGuard: guard);
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Data.SecuritySettings.SecurityEnabled = true;
        config.Data.General.Backup.AutoBackupIntervalDays = 7;
        config.Save();

        var archive = provider.GetRequiredService<DataArchiveService>();
        var source = Path.Combine(_exportDirectory, "allowed-downgrade.json");
        await archive.ExportSettingsAsync(source, TestContext.Current.CancellationToken);
        StampProducerVersion(source, TestV3ProducerVersion);
        RewriteSettingsEnvelope(source, config, candidate =>
            candidate.SecuritySettings.SecurityEnabled = false);
        config.Data.General.Backup.AutoBackupIntervalDays = 3;
        config.Save();

        var result = await archive.ImportSettingsAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(7, config.Data.General.Backup.AutoBackupIntervalDays);
        Assert.False(config.Data.SecuritySettings.SecurityEnabled);
        Assert.Single(guard.Candidates);
        Assert.True(File.Exists(result.SnapshotPath));
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
