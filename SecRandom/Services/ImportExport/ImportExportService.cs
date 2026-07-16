using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Desktop;
using SecRandom.Services.Plugins;
using SecRandom.Services.Linkage;
using SecRandom.Services.Telemetry;
using SecRandom.Shared;

namespace SecRandom.Services.ImportExport;

public sealed class ImportExportService(
    MainConfigHandler configHandler,
    IProfileService profileService,
    DesktopIntegrationService desktopIntegrationService,
    IPluginManager pluginManager,
    ILogger<ImportExportService> logger) : IImportExportService
{
    private const string ArchiveFormatName = "secrandom-archive";
    private const int MaxEntries = 20_000;
    private const long MaxEntryBytes = 1L * 1024 * 1024 * 1024;
    private const long MaxTotalBytes = 4L * 1024 * 1024 * 1024;

    private static readonly string[] AllDataRoots =
    [
        "config/settings.json", "list", "history", "TEMP", "proofs", "audio", "CSES", "images", "themes",
        "theme", "Language", "plugins", "configs/plugins", "logs"
    ];

    private readonly string _dataDirectory = Utils.DataRoot;
    private readonly string _backupDirectory = Path.Combine(Utils.DataRoot, "backup");
    private readonly object _archiveOperationLock = new();

    public Task<string> ExportDiagnosticAsync(string destinationPath, bool includeExtendedData = false,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            EnsureParents(destinationPath);
            using var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
            var entries = new List<ArchiveFileEntry>();

            WriteTextEntry(archive, "diagnostic/runtime.json", JsonSerializer.Serialize(new
            {
                software = "SecRandom",
                version = GlobalConstants.Version,
                runtime = Environment.Version.ToString(),
                operating_system = Environment.OSVersion.Platform.ToString(),
                architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                exported_utc = DateTime.UtcNow
            }, ConfigServiceBase.JsonOptions), entries);

            AddDiagnosticLogs(archive, entries, cancellationToken);
            if (includeExtendedData)
                AddExtendedDiagnosticData(archive, entries, cancellationToken);

            WriteManifest(archive, ArchiveKind.Diagnostic, entries);
            return destinationPath;
        }, cancellationToken);
    }

    private void AddDiagnosticLogs(ZipArchive archive, List<ArchiveFileEntry> entries, CancellationToken cancellationToken)
    {
        var logsDirectory = Path.Combine(_dataDirectory, "logs");
        if (!Directory.Exists(logsDirectory))
            return;

        foreach (var path in Directory.EnumerateFiles(logsDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = ReadLogText(path);
            if (text is null)
                continue;
            var relativePath = Path.GetRelativePath(logsDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
            WriteTextEntry(archive, $"logs/{relativePath}", RedactDiagnosticText(text), entries);
        }
    }

    private void AddExtendedDiagnosticData(ZipArchive archive, List<ArchiveFileEntry> entries, CancellationToken cancellationToken)
    {
        var settings = JsonSerializer.Serialize(configHandler.Data, ConfigServiceBase.JsonOptions);
        WriteTextEntry(archive, "diagnostic/settings.redacted.json", RedactDiagnosticText(settings), entries);

        WriteTextEntry(archive, "diagnostic/summary.json", JsonSerializer.Serialize(new
        {
            roll_call_lists = CountFiles("list", "roll_call_list"),
            lottery_pools = CountFiles("list", "lottery_list"),
            roll_call_histories = CountFiles("history", "roll_call_history"),
            lottery_histories = CountFiles("history", "lottery_history"),
            plugins = pluginManager.Plugins.Select(plugin => new
            {
                id = plugin.Manifest.Id,
                version = plugin.Manifest.Version,
                enabled = plugin.IsEnabled,
                status = plugin.Status.ToString()
            })
        }, ConfigServiceBase.JsonOptions), entries);

        var crashesDirectory = Path.Combine(_dataDirectory, "crashes");
        if (!Directory.Exists(crashesDirectory))
            return;
        foreach (var path in Directory.EnumerateFiles(crashesDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = ReadLogText(path);
            if (text is not null)
                WriteTextEntry(archive, $"crashes/{Path.GetFileName(path)}", RedactDiagnosticText(text), entries);
        }
    }

    public Task<string> ExportSettingsAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            SaveCurrentState();
            EnsureParents(destinationPath);
            var envelope = new SettingsEnvelope
            {
                CreatedUtc = DateTime.UtcNow,
                ProducerVersion = GlobalConstants.Version,
                Settings = JsonSerializer.SerializeToElement(configHandler.Data, ConfigServiceBase.JsonOptions)
            };
            File.WriteAllText(destinationPath, JsonSerializer.Serialize(envelope, ConfigServiceBase.JsonOptions), Encoding.UTF8);
            return destinationPath;
        }, cancellationToken);
    }

    public Task<string> ExportAllDataAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => CreateArchive(destinationPath, ArchiveKind.AllData, AllDataRoots, cancellationToken), cancellationToken);
    }

    public Task<ImportInspection> InspectSettingsAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => InspectSettings(sourcePath, cancellationToken), cancellationToken);
    }

    public Task<ImportInspection> InspectAllDataAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => InspectArchive(sourcePath, cancellationToken), cancellationToken);
    }

    public Task<ImportResult> ImportSettingsAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_archiveOperationLock)
            {
                var sourceCopy = CreateImportSourceCopy(sourcePath, cancellationToken);
                try
                {
                    var inspection = InspectSettings(sourceCopy, cancellationToken);
                    if (!inspection.IsSupportedV3)
                        throw CreateUnsupportedVersionException(inspection);

                    var warnings = new List<string>(inspection.Warnings);
                    var candidate = ReadSettingsCandidate(sourceCopy);
                    ValidateSettingsCandidate(candidate);

                    SaveCurrentState();
                    var snapshot = CreateArchive(CreateBackupPath("pre_import_settings"), ArchiveKind.PreImportSettings,
                        ["config/settings.json"], cancellationToken);

                    AtomicWriteSettings(candidate);
                    ReloadRuntimeConfiguration();
                    ReloadConfiguredProfiles();
                    _ = IAppHost.TryGetService<CourseLinkageService>()?.RefreshAsync();
                    SynchronizeDesktopIntegrations(warnings);
                    return new ImportResult(snapshot, 1, warnings, []);
                }
                finally
                {
                    TryDeletePath(sourceCopy);
                }
            }
        }, cancellationToken);
    }

    public Task<ImportResult> ImportAllDataAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ImportArchive(sourcePath, createSnapshot: true, cancellationToken), cancellationToken);
    }

    public string CreateManualBackup(IReadOnlyCollection<string> roots)
    {
        if (roots.Count == 0)
            throw new InvalidOperationException("请至少选择一项备份内容。");
        return CreateArchive(CreateBackupPath("manual"), ArchiveKind.ManualBackup, roots, CancellationToken.None);
    }

    public string CreateAutomaticBackup(CancellationToken cancellationToken = default)
    {
        var backup = configHandler.Data.General.Backup;
        var roots = new List<string>();
        if (backup.IncludeConfig) roots.Add("config/settings.json");
        if (backup.IncludeList) roots.Add("list");
        if (backup.IncludeHistory) roots.Add("history");
        if (backup.IncludeProofs) roots.Add("proofs");
        if (backup.IncludeAudio) roots.Add("audio");
        if (backup.IncludeCses) roots.Add("CSES");
        if (backup.IncludeImages) roots.Add("images");
        if (backup.IncludeThemes)
        {
            roots.Add("theme");
            roots.Add("themes");
        }
        if (backup.IncludeLogs) roots.Add("logs");

        if (roots.Count == 0)
            throw new InvalidOperationException("请至少选择一项备份内容。");

        return CreateArchive(CreateBackupPath("auto"), ArchiveKind.AutomaticBackup, roots, cancellationToken);
    }

    public Task<ImportResult> RestoreBackupAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ImportArchive(sourcePath, createSnapshot: true, cancellationToken), cancellationToken);
    }

    private ImportResult ImportArchive(string sourcePath, bool createSnapshot, CancellationToken cancellationToken)
    {
        lock (_archiveOperationLock)
        {
            var sourceCopy = CreateImportSourceCopy(sourcePath, cancellationToken);
            try
            {
                var inspection = InspectArchive(sourceCopy, cancellationToken);
                if (inspection.Kind == ArchiveKind.Diagnostic)
                    throw new InvalidDataException("诊断数据不能导入。");
                if (!inspection.IsSupportedV3)
                    throw CreateUnsupportedVersionException(inspection);

                SaveCurrentState();
                var snapshot = string.Empty;
                if (createSnapshot)
                    snapshot = CreateArchive(CreateBackupPath("pre_import_all_data"), ArchiveKind.PreImportAllData,
                        AllDataRoots, cancellationToken);

                var staging = Path.Combine(_dataDirectory, ".import-staging", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                try
                {
                    var warnings = new List<string>(inspection.Warnings);
                    var importedFiles = ExtractCurrentArchive(sourceCopy, staging, cancellationToken);

                    ValidateCandidate(staging);
                    var rootsToCommit = inspection.Kind == ArchiveKind.AllData
                        ? AllDataRoots
                        : inspection.Roots;
                    CommitCandidate(staging, rootsToCommit, inspection.Kind == ArchiveKind.AllData, warnings);
                    ReloadRuntimeConfiguration();
                    ReloadConfiguredProfiles();
                    _ = IAppHost.TryGetService<CourseLinkageService>()?.RefreshAsync();
                    pluginManager.Refresh();
                    SynchronizeDesktopIntegrations(warnings);
                    return new ImportResult(snapshot, importedFiles, warnings, []);
                }
                finally
                {
                    TryDeleteDirectory(staging);
                }
            }
            catch
            {
                logger.LogWarning("导入未完成，当前数据保持不变。来源文件={FileName}", Path.GetFileName(sourcePath));
                throw;
            }
            finally
            {
                TryDeletePath(sourceCopy);
            }
        }
    }

    private ImportInspection InspectSettings(string sourcePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(File.ReadAllText(sourcePath));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return new ImportInspection(ArchiveFormat.Unknown, null, string.Empty, 0, 0, [], ["设置文件根节点必须是对象。"]);

        var root = document.RootElement;
        var producerVersion = root.TryGetProperty("producer_version", out var version)
            ? version.GetString() ?? string.Empty
            : string.Empty;
        if (!root.TryGetProperty("format", out var format) || format.GetString() != "secrandom-settings" ||
            !root.TryGetProperty("schema_version", out var schemaVersion) || !schemaVersion.TryGetInt32(out var schema) || schema != 1 ||
            !root.TryGetProperty("settings", out _))
            return new ImportInspection(ArchiveFormat.Unknown, null, producerVersion, 0, 0, [], ["设置文件不是受支持的 SecRandom v3 导出格式。"]);

        return IsSupportedV3ProducerVersion(producerVersion)
            ? new ImportInspection(ArchiveFormat.Current, null, producerVersion, 1, new FileInfo(sourcePath).Length, ["config/settings.json"], [])
            : new ImportInspection(ArchiveFormat.Unknown, null, producerVersion, 0, 0, [], ["仅支持由 SecRandom v3 导出的设置文件。"]);
    }

    private ImportInspection InspectArchive(string sourcePath, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(sourcePath);
        ValidateArchiveEntries(archive, cancellationToken);
        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry is not null)
        {
            var manifest = ReadJsonEntry<ArchiveManifest>(manifestEntry);
            if (manifest is null || manifest.Format != ArchiveFormatName || manifest.SchemaVersion != 1 ||
                !Enum.TryParse<ArchiveKind>(manifest.Kind, out var kind))
                throw new InvalidDataException("备份清单无效。");
            ValidateManifest(archive, manifest);
            return IsSupportedV3ProducerVersion(manifest.ProducerVersion)
                ? new ImportInspection(ArchiveFormat.Current, kind, manifest.ProducerVersion, manifest.Files.Count,
                    manifest.Files.Sum(item => item.Length), GetRoots(manifest.Files.Select(item => item.Path)), [])
                : new ImportInspection(ArchiveFormat.Unknown, kind, manifest.ProducerVersion, manifest.Files.Count,
                    manifest.Files.Sum(item => item.Length), [], ["仅支持由 SecRandom v3 导出的数据归档。"]);
        }

        return new ImportInspection(ArchiveFormat.Unknown, null, string.Empty, 0, 0, [], ["数据归档缺少 SecRandom v3 清单。"]);
    }

    private static bool IsSupportedV3ProducerVersion(string producerVersion)
    {
        return Version.TryParse(producerVersion.TrimStart('v', 'V'), out var version) && version.Major == 3;
    }

    private static InvalidDataException CreateUnsupportedVersionException(ImportInspection inspection)
    {
        var detectedVersion = string.IsNullOrWhiteSpace(inspection.ProducerVersion)
            ? "未识别"
            : inspection.ProducerVersion;
        var detail = inspection.Warnings.FirstOrDefault();
        return new InvalidDataException(string.IsNullOrWhiteSpace(detail)
            ? $"仅支持 SecRandom v3 导出文件。检测到的版本：{detectedVersion}。"
            : $"{detail} 检测到的版本：{detectedVersion}。");
    }

    private string CreateArchive(string destinationPath, ArchiveKind kind, IEnumerable<string> roots, CancellationToken cancellationToken)
    {
        lock (_archiveOperationLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var temporaryPath = destinationPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                SaveCurrentState();
                EnsureParents(temporaryPath);
                var files = new List<ArchiveFileEntry>();
                using (var archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
                {
                    foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        AddRootToArchive(archive, root, files, cancellationToken);
                    }
                    WriteManifest(archive, kind, files);
                }

                InspectArchive(temporaryPath, cancellationToken);
                File.Move(temporaryPath, destinationPath);
                if (Path.GetDirectoryName(Path.GetFullPath(destinationPath))?.Equals(
                        Path.GetFullPath(_backupDirectory), StringComparison.OrdinalIgnoreCase) == true)
                    TrimBackups();
                logger.LogInformation("已创建数据归档：类型={Kind}，文件={FileName}，文件数={Count}", kind, Path.GetFileName(destinationPath), files.Count);
                return destinationPath;
            }
            catch
            {
                TryDeletePath(temporaryPath);
                throw;
            }
        }
    }

    private void AddRootToArchive(ZipArchive archive, string root, List<ArchiveFileEntry> files, CancellationToken cancellationToken)
    {
        var normalized = NormalizePath(root);
        if (normalized == "config")
            normalized = "config/settings.json";
        if (!IsManagedPath(normalized))
            return;

        var source = Path.Combine(_dataDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(source))
        {
            AddFileToArchive(archive, source, normalized, files, cancellationToken);
            return;
        }
        if (!Directory.Exists(source))
            return;

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizePath(Path.Combine(normalized, Path.GetRelativePath(source, file)));
            if (IsManagedPath(relative))
                AddFileToArchive(archive, file, relative, files, cancellationToken);
        }
    }

    private static void AddFileToArchive(ZipArchive archive, string source, string entryPath, List<ArchiveFileEntry> files, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.SmallestSize);
        using var input = File.OpenRead(source);
        using var output = entry.Open();
        var hash = CopyAndHash(input, output, cancellationToken, out var length);
        files.Add(new ArchiveFileEntry { Path = entryPath, Length = length, Sha256 = hash });
    }

    private int ExtractCurrentArchive(string sourcePath, string staging, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(sourcePath);
        var count = 0;
        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
        {
            if (entry.FullName == "manifest.json")
                continue;
            var path = NormalizePath(entry.FullName);
            if (!IsManagedPath(path))
                continue;
            ExtractEntry(entry, Path.Combine(staging, path.Replace('/', Path.DirectorySeparatorChar)), cancellationToken);
            count++;
        }
        return count;
    }

    private MainConfigModel ReadSettingsCandidate(string sourcePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(sourcePath));
        var root = document.RootElement;
        if (root.TryGetProperty("settings", out var settings))
            return DeserializeSettings(settings.GetRawText());
        throw new InvalidDataException("设置文件缺少设置内容。");
    }

    private static MainConfigModel DeserializeSettings(string json)
    {
        return JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions)
               ?? throw new InvalidDataException("设置文件为空或无法读取。");
    }

    private void CommitCandidate(string staging, IReadOnlyList<string> roots, bool replaceMissingRoots, List<string> warnings)
    {
        var previous = Path.Combine(staging, "previous");
        Directory.CreateDirectory(previous);
        var committed = new List<(string Target, string Previous)>();
        try
        {
            foreach (var root in roots.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!IsManagedPath(root) || root.StartsWith("logs", StringComparison.OrdinalIgnoreCase))
                    continue;
                var candidate = Path.Combine(staging, root.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    if (!replaceMissingRoots || root.Equals("config/settings.json", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var emptyTarget = Path.Combine(_dataDirectory, root.Replace('/', Path.DirectorySeparatorChar));
                    var emptyOld = Path.Combine(previous, root.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(emptyTarget))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(emptyOld)!);
                        File.Move(emptyTarget, emptyOld, true);
                        committed.Add((emptyTarget, emptyOld));
                    }
                    else if (Directory.Exists(emptyTarget))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(emptyOld)!);
                        Directory.Move(emptyTarget, emptyOld);
                        committed.Add((emptyTarget, emptyOld));
                    }
                    continue;
                }
                var target = Path.Combine(_dataDirectory, root.Replace('/', Path.DirectorySeparatorChar));
                var old = Path.Combine(previous, root.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(old)!);
                if (root.Equals("config/settings.json", StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(target)) File.Move(target, old, true);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(candidate, target, true);
                }
                else
                {
                    if (File.Exists(target)) File.Move(target, old, true);
                    else if (Directory.Exists(target)) Directory.Move(target, old);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (File.Exists(candidate)) File.Move(candidate, target, true);
                    else Directory.Move(candidate, target);
                }
                committed.Add((target, old));
            }

            var logs = Path.Combine(staging, "logs");
            if (Directory.Exists(logs))
            {
                var importedLogs = Path.Combine(_dataDirectory, "logs", $"imported-{DateTime.UtcNow:yyyyMMddHHmmss}");
                Directory.CreateDirectory(Path.GetDirectoryName(importedLogs)!);
                Directory.Move(logs, importedLogs);
                warnings.Add("导入日志已保存在独立的 imported 目录，未覆盖正在写入的日志。");
            }
        }
        catch
        {
            foreach (var (target, old) in committed.AsEnumerable().Reverse())
            {
                TryDeletePath(target);
                if (File.Exists(old)) File.Move(old, target, true);
                else if (Directory.Exists(old)) Directory.Move(old, target);
            }
            throw;
        }
    }

    private void ReloadConfiguredProfiles()
    {
        profileService.LoadStudentProfile(configHandler.Data.RollCallSettings.DefaultClass, saveCurrent: false);
        profileService.LoadPrizeProfile(configHandler.Data.LotterySettings.DefaultPool, saveCurrent: false);
    }

    private void ReloadRuntimeConfiguration()
    {
        configHandler.Reload();
        IAppHost.TryGetService<FeatureAvailabilityService>()?.Refresh();
        IAppHost.TryGetService<GlobalShortcutService>()?.Refresh();
        IAppHost.TryGetService<ShortcutService>()?.Refresh();
        _ = IAppHost.TryGetService<TelemetryRuntimeService>()?.RefreshAsync();
        IAppHost.TryGetService<OnlineStatusService>()?.Refresh();
    }

    private string CreateImportSourceCopy(string sourcePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("找不到导入文件。", sourcePath);

        var sourceCopy = Path.Combine(_dataDirectory, ".import-staging", $"{Guid.NewGuid():N}.source");
        EnsureParents(sourceCopy);
        try
        {
            using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = new FileStream(sourceCopy, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            CopyAndHash(input, output, cancellationToken, out _);
            return sourceCopy;
        }
        catch
        {
            TryDeletePath(sourceCopy);
            throw;
        }
    }

    private void AtomicWriteSettings(MainConfigModel model)
    {
        var path = model.ConfigFilePath;
        var temporary = path + ".importing";
        EnsureParents(path);
        File.WriteAllText(temporary, JsonSerializer.Serialize(model, ConfigServiceBase.JsonOptions), Encoding.UTF8);
        File.Move(temporary, path, true);
    }

    private void SynchronizeDesktopIntegrations(List<string> warnings)
    {
        desktopIntegrationService.EnsureConfiguredIntegrations();
        if (!configHandler.Data.General.Basic.Autostart && !desktopIntegrationService.TrySetAutostart(false, out var autostartError))
            warnings.Add($"未能移除开机启动注册：{autostartError}");
        if (!configHandler.Data.General.Basic.UrlProtocol && !desktopIntegrationService.TrySetUrlProtocol(false, out var protocolError))
            warnings.Add($"未能移除 URL 协议注册：{protocolError}");
    }

    private void SaveCurrentState()
    {
        configHandler.Save();
        profileService.SaveProfile();
    }

    private void ValidateCandidate(string staging)
    {
        var settings = Path.Combine(staging, "config", "settings.json");
        if (File.Exists(settings))
            ValidateSettingsCandidate(DeserializeSettings(File.ReadAllText(settings)));
        foreach (var path in Directory.Exists(Path.Combine(staging, "list"))
                     ? Directory.EnumerateFiles(Path.Combine(staging, "list"), "*.json", SearchOption.AllDirectories)
                     : [])
            using (JsonDocument.Parse(File.ReadAllText(path))) { }
        foreach (var path in Directory.Exists(Path.Combine(staging, "history"))
                     ? Directory.EnumerateFiles(Path.Combine(staging, "history"), "*.json", SearchOption.AllDirectories)
                     : [])
            using (JsonDocument.Parse(File.ReadAllText(path))) { }
    }

    private static void ValidateSettingsCandidate(MainConfigModel candidate)
    {
        if (candidate.General.Basic.MainWindowWidth <= 0 || candidate.General.Basic.MainWindowHeight <= 0 ||
            candidate.General.Basic.SettingsWindowWidth <= 0 || candidate.General.Basic.SettingsWindowHeight <= 0)
            throw new InvalidDataException("设置中的窗口尺寸无效。");
    }

    private void ValidateArchiveEntries(ZipArchive archive, CancellationToken cancellationToken)
    {
        if (archive.Entries.Count > MaxEntries)
            throw new InvalidDataException("归档文件数量超过限制。");
        long total = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            var path = NormalizePath(entry.FullName);
            if (path.Length == 0 || entry.Length > MaxEntryBytes || !paths.Add(path))
                throw new InvalidDataException("归档包含非法、过大或重复的文件路径。");
            total = checked(total + entry.Length);
            if (total > MaxTotalBytes || (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > 200))
                throw new InvalidDataException("归档解压大小或压缩比超过限制。");
        }
    }

    private static void ValidateManifest(ZipArchive archive, ArchiveManifest manifest)
    {
        var listed = manifest.Files.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name) && entry.FullName != "manifest.json"))
        {
            var path = NormalizePath(entry.FullName);
            if (!listed.TryGetValue(path, out var expected) || expected.Length != entry.Length)
                throw new InvalidDataException("备份清单与归档内容不一致。");
            using var input = entry.Open();
            var hash = Hash(input);
            if (!hash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("备份文件校验失败。");
        }
        if (listed.Count != archive.Entries.Count(entry => !string.IsNullOrEmpty(entry.Name) && entry.FullName != "manifest.json"))
            throw new InvalidDataException("备份清单包含不存在的文件。");
    }

    private void WriteManifest(ZipArchive archive, ArchiveKind kind, List<ArchiveFileEntry> files)
    {
        var manifest = new ArchiveManifest
        {
            Kind = kind.ToString(),
            CreatedUtc = DateTime.UtcNow,
            ProducerVersion = GlobalConstants.Version,
            Files = files
        };
        var entry = archive.CreateEntry("manifest.json", CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
        writer.Write(JsonSerializer.Serialize(manifest, ConfigServiceBase.JsonOptions));
    }

    private static void ExtractEntry(ZipArchiveEntry entry, string destination, CancellationToken cancellationToken)
    {
        EnsureParents(destination);
        using var input = entry.Open();
        using var output = File.Create(destination);
        CopyAndHash(input, output, cancellationToken, out _);
    }

    private static void WriteTextEntry(ZipArchive archive, string path, string text, List<ArchiveFileEntry> files)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var entry = archive.CreateEntry(path, CompressionLevel.SmallestSize);
        using (var stream = entry.Open()) stream.Write(bytes);
        files.Add(new ArchiveFileEntry { Path = path, Length = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
    }

    private string CreateBackupPath(string kind)
    {
        Directory.CreateDirectory(_backupDirectory);
        var stem = $"SecRandom_{kind}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        for (var index = 1; ; index++)
        {
            var suffix = index == 1 ? string.Empty : $"_{index}";
            var path = Path.Combine(_backupDirectory, $"{stem}{suffix}.zip");
            if (!File.Exists(path)) return path;
        }
    }

    private void TrimBackups()
    {
        var maximum = configHandler.Data.General.Backup.AutoBackupMaxCount;
        if (maximum <= 0 || !Directory.Exists(_backupDirectory)) return;
        foreach (var file in new DirectoryInfo(_backupDirectory).EnumerateFiles("SecRandom_auto_*.zip")
                     .OrderByDescending(file => file.LastWriteTimeUtc).ThenByDescending(file => file.Name).Skip(maximum))
            file.Delete();
    }

    private static bool IsManagedPath(string path)
    {
        path = NormalizePath(path);
        if (path.StartsWith("config/security/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("backup/", StringComparison.OrdinalIgnoreCase) || path.StartsWith(".import-staging/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("crashes/", StringComparison.OrdinalIgnoreCase))
            return false;
        return AllDataRoots.Any(root => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> GetRoots(IEnumerable<string> paths)
    {
        return paths.Select(NormalizePath).Select(path => path.StartsWith("config/settings.json", StringComparison.OrdinalIgnoreCase)
                ? "config/settings.json"
                : path.StartsWith("configs/plugins", StringComparison.OrdinalIgnoreCase)
                    ? "configs/plugins"
                    : path.Split('/')[0])
            .Where(IsManagedPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') || path.Contains('\0'))
            throw new InvalidDataException("归档包含非法路径。");
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(IsUnsafePathComponent))
            throw new InvalidDataException("归档包含非法路径。");
        return string.Join('/', parts);
    }

    private static bool IsUnsafePathComponent(string part)
    {
        return part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
               part.Any(character => char.IsControl(character) || "<>:\"|?*".Contains(character)) ||
               IsReservedDeviceName(part);
    }

    private static bool IsReservedDeviceName(string part)
    {
        var name = part.Split('.', 2)[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase)
               || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
               || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
               || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
               || (name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                                         || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                   && name[3] is >= '1' and <= '9');
    }

    private static void EnsureParents(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
    }

    private static string CopyAndHash(Stream input, Stream output, CancellationToken cancellationToken, out long length)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        length = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
            hash.AppendData(buffer, 0, read);
            length += read;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string Hash(Stream input)
    {
        using var hash = SHA256.Create();
        return Convert.ToHexString(hash.ComputeHash(input));
    }

    private static T? ReadJsonEntry<T>(ZipArchiveEntry entry)
    {
        return JsonSerializer.Deserialize<T>(ReadEntryText(entry), ConfigServiceBase.JsonOptions);
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private int CountFiles(params string[] path)
    {
        var directory = Path.Combine([_dataDirectory, .. path]);
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly).Count() : 0;
    }

    private static string? ReadLogText(string path)
    {
        try
        {
            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using var file = File.OpenRead(path);
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    private static string RedactDiagnosticText(string text)
    {
        text = System.Text.RegularExpressions.Regex.Replace(text,
            "(?i)\\bBearer\\s+[A-Za-z0-9._~+/-]+=*", "Bearer <redacted>");
        text = System.Text.RegularExpressions.Regex.Replace(text,
            "(?i)(\\\"?(?:password|secret|token)\\\"?\\s*[:=]\\s*)(\\\"[^\\\"]*\\\"|[^\\s,;}\\]]+)", "$1<redacted>");
        text = System.Text.RegularExpressions.Regex.Replace(text,
            "(?i)(authorization\\s*[:=]\\s*)(\\\"?Bearer\\s+)?(\\\"[^\\\"]*\\\"|[^\\s,;}\\]]+)", "$1$2<redacted>");
        text = System.Text.RegularExpressions.Regex.Replace(text, "[A-Za-z]:\\\\[^\\s\"]+|/[^\\s\"]+", "<path>");
        text = System.Text.RegularExpressions.Regex.Replace(text, "[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}", "<email>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return text;
    }

    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
    private static void TryDeletePath(string path) { try { if (File.Exists(path)) File.Delete(path); else if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
}
