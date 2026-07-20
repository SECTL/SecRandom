using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
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

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
        if (Directory.Exists(_exportDirectory))
            Directory.Delete(_exportDirectory, recursive: true);
    }

    private static ServiceProvider CreateProvider(IArchivePostImportHooks? hooks = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        if (hooks is not null)
            services.AddSingleton(hooks);
        return services.BuildServiceProvider();
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
