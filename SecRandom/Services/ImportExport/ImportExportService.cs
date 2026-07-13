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
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Desktop;
using SecRandom.Services.Plugins;
using SecRandom.Shared.Models.Profile;

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
        "theme", "Language", "plugins", "configs/plugins", "logs", "legacy/v2-history"
    ];

    private static readonly string[] LegacyRoots =
    ["config", "list", "history", "Language", "audio", "CSES", "images", "theme", "themes", "logs"];

    private readonly string _dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
    private readonly string _backupDirectory = Path.Combine(AppContext.BaseDirectory, "data", "backup");
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
                var inspection = InspectSettings(sourcePath, cancellationToken);
                if (inspection.Format == ArchiveFormat.Unknown)
                    throw new InvalidDataException("不支持的设置文件格式。");

                var warnings = new List<string>(inspection.Warnings);
                var candidate = ReadSettingsCandidate(sourcePath, inspection.Format, warnings);
                ValidateSettingsCandidate(candidate);

                SaveCurrentState();
                var snapshot = CreateArchive(CreateBackupPath("pre_import_settings"), ArchiveKind.PreImportSettings,
                    ["config/settings.json"], cancellationToken);

                AtomicWriteSettings(candidate);
                configHandler.Reload();
                SynchronizeDesktopIntegrations(warnings);
                return new ImportResult(snapshot, 1, warnings, []);
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
            var inspection = InspectArchive(sourcePath, cancellationToken);
            if (inspection.Kind == ArchiveKind.Diagnostic)
                throw new InvalidDataException("诊断数据不能导入。");
            if (inspection.Format == ArchiveFormat.Unknown)
                throw new InvalidDataException("不支持的备份文件格式。");

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
                var preserved = new List<string>();
                var importedFiles = inspection.Format == ArchiveFormat.V2
                    ? MigrateV2Archive(sourcePath, staging, warnings, preserved, cancellationToken)
                    : ExtractCurrentArchive(sourcePath, staging, inspection.Format, cancellationToken);

                ValidateCandidate(staging);
                var rootsToCommit = inspection.Kind == ArchiveKind.AllData
                    ? AllDataRoots
                    : [.. inspection.Roots, "legacy/v2-history"];
                CommitCandidate(staging, rootsToCommit, inspection.Kind == ArchiveKind.AllData, warnings);
                configHandler.Reload();
                ReloadCurrentProfiles();
                pluginManager.Refresh();
                SynchronizeDesktopIntegrations(warnings);
                return new ImportResult(snapshot, importedFiles, warnings, preserved);
            }
            catch
            {
                logger.LogWarning("导入未完成，当前数据保持不变。来源文件={FileName}", Path.GetFileName(sourcePath));
                throw;
            }
            finally
            {
                TryDeleteDirectory(staging);
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
        if (root.TryGetProperty("format", out var format) && format.GetString() == "secrandom-settings" &&
            root.TryGetProperty("settings", out _))
            return new ImportInspection(ArchiveFormat.Current, null, root.TryGetProperty("producer_version", out var version) ? version.GetString() ?? string.Empty : string.Empty, 1, new FileInfo(sourcePath).Length, ["config/settings.json"], []);

        if (root.TryGetProperty("basic_settings", out _))
            return new ImportInspection(ArchiveFormat.V2, null, "v2", 1, new FileInfo(sourcePath).Length, ["config/settings.json"], []);

        if (root.TryGetProperty("general", out _) || root.TryGetProperty("basic", out _))
            return new ImportInspection(ArchiveFormat.CurrentLegacy, null, string.Empty, 1, new FileInfo(sourcePath).Length, ["config/settings.json"], []);

        return new ImportInspection(ArchiveFormat.Unknown, null, string.Empty, 0, 0, [], ["无法识别设置文件格式。"]);
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
            return new ImportInspection(ArchiveFormat.Current, kind, manifest.ProducerVersion, manifest.Files.Count,
                manifest.Files.Sum(item => item.Length), GetRoots(manifest.Files.Select(item => item.Path)), []);
        }

        var versionEntry = archive.GetEntry("version.json");
        if (versionEntry is not null)
        {
            using var document = JsonDocument.Parse(ReadEntryText(versionEntry));
            var root = document.RootElement;
            var software = root.TryGetProperty("software_name", out var name) ? name.GetString() : null;
            var version = root.TryGetProperty("version", out var number) ? number.GetString() : null;
            if (software == "SecRandom" && version?.StartsWith("v2", StringComparison.OrdinalIgnoreCase) == true)
                return new ImportInspection(ArchiveFormat.V2, ArchiveKind.AllData, version, archive.Entries.Count,
                    archive.Entries.Sum(item => item.Length), GetRoots(archive.Entries.Select(item => item.FullName)), []);
        }

        var legacyEntries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToList();
        if (legacyEntries.Count > 0 && legacyEntries.All(entry => IsAllowedLegacyEntry(entry.FullName)))
            return new ImportInspection(ArchiveFormat.CurrentLegacy, ArchiveKind.ManualBackup, string.Empty,
                legacyEntries.Count, legacyEntries.Sum(item => item.Length), GetRoots(legacyEntries.Select(item => item.FullName)),
                ["该备份没有清单，已按旧版 C# 备份格式处理。"]);

        return new ImportInspection(ArchiveFormat.Unknown, null, string.Empty, 0, 0, [], ["无法识别备份格式。"]);
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

    private int ExtractCurrentArchive(string sourcePath, string staging, ArchiveFormat format, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(sourcePath);
        var count = 0;
        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
        {
            if (entry.FullName == "manifest.json" || entry.FullName == "version.json")
                continue;
            var path = NormalizeLegacyPath(NormalizePath(entry.FullName));
            if (format == ArchiveFormat.CurrentLegacy)
                path = NormalizeLegacyPath(path);
            if (!IsManagedPath(path))
                continue;
            ExtractEntry(entry, Path.Combine(staging, path.Replace('/', Path.DirectorySeparatorChar)), cancellationToken);
            count++;
        }
        return count;
    }

    private int MigrateV2Archive(string sourcePath, string staging, List<string> warnings, List<string> preserved, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(sourcePath);
        var entries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToList();
        var studentMaps = new Dictionary<string, Dictionary<string, Student?>>(StringComparer.OrdinalIgnoreCase);
        var prizeMaps = new Dictionary<string, Dictionary<string, Prize?>>(StringComparer.OrdinalIgnoreCase);
        var count = 0;

        foreach (var entry in entries.Where(entry => NormalizePath(entry.FullName).StartsWith("list/", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = NormalizeLegacyPath(NormalizePath(entry.FullName));
            if (path.StartsWith("list/roll_call_list/", StringComparison.OrdinalIgnoreCase))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var list = MigrateV2Students(ReadEntryText(entry), out var map, warnings);
                studentMaps[name] = map;
                WriteJson(Path.Combine(staging, "list", "roll_call_list", $"{name}.json"), list);
                count++;
            }
            else if (path.StartsWith("list/lottery_list/", StringComparison.OrdinalIgnoreCase))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var list = MigrateV2Prizes(ReadEntryText(entry), out var map, warnings);
                prizeMaps[name] = map;
                WriteJson(Path.Combine(staging, "list", "lottery_list", $"{name}.json"), list);
                count++;
            }
        }

        foreach (var entry in entries.Where(entry => NormalizePath(entry.FullName).StartsWith("history/", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = NormalizePath(entry.FullName);
            var name = Path.GetFileNameWithoutExtension(path);
            var rawPath = path.StartsWith("history/roll_call_history/", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(staging, "legacy", "v2-history", "roll-call", $"{name}.json")
                : Path.Combine(staging, "legacy", "v2-history", "lottery", $"{name}.json");
            WriteText(rawPath, ReadEntryText(entry));
            preserved.Add(Path.Combine("legacy", "v2-history", Path.GetFileName(rawPath)));

            if (path.StartsWith("history/roll_call_history/", StringComparison.OrdinalIgnoreCase) && studentMaps.TryGetValue(name, out var students))
            {
                WriteJson(Path.Combine(staging, "history", "roll_call_history", $"{name}.json"), MigrateV2StudentHistory(ReadEntryText(entry), students, warnings));
                count++;
            }
            else if (path.StartsWith("history/lottery_history/", StringComparison.OrdinalIgnoreCase) && prizeMaps.TryGetValue(name, out var prizes))
            {
                WriteJson(Path.Combine(staging, "history", "lottery_history", $"{name}.json"), MigrateV2PrizeHistory(ReadEntryText(entry), prizes, warnings));
                count++;
            }
        }

        var settings = entries.FirstOrDefault(entry => NormalizePath(entry.FullName).Equals("config/settings.json", StringComparison.OrdinalIgnoreCase));
        if (settings is not null)
        {
            var candidate = MigrateV2Settings(ReadEntryText(settings), warnings);
            WriteJson(Path.Combine(staging, "config", "settings.json"), candidate);
            count++;
        }

        foreach (var entry in entries)
        {
            var path = NormalizePath(entry.FullName);
            if (path is "version.json" or "config/settings.json" || path.StartsWith("list/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("history/", StringComparison.OrdinalIgnoreCase))
                continue;
            var target = NormalizeLegacyPath(path);
            if (IsManagedPath(target) && !target.StartsWith("config/security/", StringComparison.OrdinalIgnoreCase))
            {
                ExtractEntry(entry, Path.Combine(staging, target.Replace('/', Path.DirectorySeparatorChar)), cancellationToken);
                count++;
            }
        }

        WriteJson(Path.Combine(staging, "legacy", "v2-history", "manifest.json"), new
        {
            source_version = "v2",
            migrated_utc = DateTime.UtcNow,
            preserved_files = preserved,
            warnings
        });
        return count;
    }

    private MainConfigModel ReadSettingsCandidate(string sourcePath, ArchiveFormat format, List<string> warnings)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(sourcePath));
        var root = document.RootElement;
        if (format == ArchiveFormat.Current && root.TryGetProperty("settings", out var settings))
            return DeserializeSettings(settings.GetRawText());
        if (format == ArchiveFormat.V2)
            return MigrateV2Settings(root.GetRawText(), warnings);
        return DeserializeSettings(root.GetRawText());
    }

    private static MainConfigModel DeserializeSettings(string json)
    {
        return JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions)
               ?? throw new InvalidDataException("设置文件为空或无法读取。");
    }

    private MainConfigModel MigrateV2Settings(string json, List<string> warnings)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var result = new MainConfigModel();

        if (root.TryGetProperty("window", out var window))
        {
            result.General.Basic.MainWindowWidth = GetDouble(window, "pre_maximized_width", GetDouble(window, "width", result.General.Basic.MainWindowWidth));
            result.General.Basic.MainWindowHeight = GetDouble(window, "pre_maximized_height", GetDouble(window, "height", result.General.Basic.MainWindowHeight));
            result.General.Basic.MainWindowMaximized = GetBool(window, "is_maximized", false);
        }
        if (root.TryGetProperty("settings", out var settingsWindow))
        {
            result.General.Basic.SettingsWindowWidth = GetDouble(settingsWindow, "pre_maximized_width", GetDouble(settingsWindow, "width", result.General.Basic.SettingsWindowWidth));
            result.General.Basic.SettingsWindowHeight = GetDouble(settingsWindow, "pre_maximized_height", GetDouble(settingsWindow, "height", result.General.Basic.SettingsWindowHeight));
            result.General.Basic.SettingsWindowMaximized = GetBool(settingsWindow, "is_maximized", false);
        }
        if (root.TryGetProperty("float_position", out var floatPosition))
        {
            result.FloatPosition.X = GetInt(floatPosition, "x", result.FloatPosition.X);
            result.FloatPosition.Y = GetInt(floatPosition, "y", result.FloatPosition.Y);
        }
        if (root.TryGetProperty("basic_settings", out var basic))
        {
            result.General.Basic.Autostart = GetBool(basic, "autostart", result.General.Basic.Autostart);
            result.General.Basic.ShowStartupWindow = GetBool(basic, "show_startup_window", result.General.Basic.ShowStartupWindow);
            result.General.Basic.AutoSaveWindowSize = GetBool(basic, "auto_save_window_size", result.General.Basic.AutoSaveWindowSize);
            result.General.Basic.BackgroundResident = GetBool(basic, "background_resident", result.General.Basic.BackgroundResident);
            result.General.Basic.UrlProtocol = GetBool(basic, "url_protocol", result.General.Basic.UrlProtocol);
            result.General.Basic.GuideCompleted = GetBool(basic, "guide_completed", result.General.Basic.GuideCompleted);
            result.General.Basic.ShowVersionNotice = GetBool(basic, "show_version_notice", result.General.Basic.ShowVersionNotice);
            result.General.Basic.MainWindowTopmostMode = (TopmostMode)Math.Clamp(
                GetInt(basic, "main_window_topmost_mode", (int)result.General.Basic.MainWindowTopmostMode),
                (int)TopmostMode.None,
                (int)TopmostMode.UiAccess);
            if (basic.TryGetProperty("language", out var language))
                result.General.Basic.Language = ParseV2Language(language.GetString());
            if (basic.TryGetProperty("offline_user_id", out var offline) && Guid.TryParse(offline.GetString(), out var offlineId))
                result.General.Basic.OfflineUserId = offlineId;
            if (basic.TryGetProperty("telemetry_enabled", out var enabled))
                result.General.PrivacySettings.SentryTelemetryEnabled = enabled.GetBoolean();
            if (basic.TryGetProperty("telemetry_mode", out var telemetryMode))
                result.General.PrivacySettings.OnlineStatusMode = ParseOnlineStatus(telemetryMode.GetString());
        }

        if (root.TryGetProperty("backup", out var backup))
            result.General.Backup = JsonSerializer.Deserialize<SecRandom.Core.Models.SubConfigs.General.BackupConfig>(backup.GetRawText(), ConfigServiceBase.JsonOptions) ?? result.General.Backup;
        if (root.TryGetProperty("shortcut_settings", out var shortcuts))
        {
            result.MoreSettings.EnableShortcut = GetBool(shortcuts, "enable_shortcut", result.MoreSettings.EnableShortcut);
            result.MoreSettings.OpenRollCallPageShortcut = GetString(shortcuts, "open_roll_call_page");
            result.MoreSettings.QuickDrawShortcut = GetString(shortcuts, "use_quick_draw");
            result.MoreSettings.OpenLotteryPageShortcut = GetString(shortcuts, "open_lottery_page");
            result.MoreSettings.IncreaseRollCallCountShortcut = GetString(shortcuts, "increase_roll_call_count");
            result.MoreSettings.DecreaseRollCallCountShortcut = GetString(shortcuts, "decrease_roll_call_count");
            result.MoreSettings.IncreaseLotteryCountShortcut = GetString(shortcuts, "increase_lottery_count");
            result.MoreSettings.DecreaseLotteryCountShortcut = GetString(shortcuts, "decrease_lottery_count");
            result.MoreSettings.StartRollCallShortcut = GetString(shortcuts, "start_roll_call");
            result.MoreSettings.StartLotteryShortcut = GetString(shortcuts, "start_lottery");
        }
        CopyCompatibleSection(root, "roll_call_settings", result.RollCallSettings, warnings);
        CopyCompatibleSection(root, "quick_draw_settings", result.QuickDrawSettings, warnings);
        CopyCompatibleSection(root, "lottery_settings", result.LotterySettings, warnings);
        CopyCompatibleSection(root, "fair_draw_settings", result.FairDrawSettings, warnings);
        warnings.Add("v2 设置已迁移；当前版本不支持的字段已保留为默认值。");
        return result;
    }

    private static void CopyCompatibleSection<T>(JsonElement root, string property, T target, List<string> warnings)
    {
        if (!root.TryGetProperty(property, out var source))
            return;
        try
        {
            var converted = JsonSerializer.Deserialize<T>(source.GetRawText(), ConfigServiceBase.JsonOptions);
            if (converted is null)
                return;
            foreach (var sourceProperty in typeof(T).GetProperties().Where(propertyInfo => propertyInfo.CanRead && propertyInfo.CanWrite))
                sourceProperty.SetValue(target, sourceProperty.GetValue(converted));
        }
        catch (JsonException)
        {
            warnings.Add($"v2 字段“{property}”包含无法转换的值，已使用默认设置。");
        }
    }

    private static StudentList MigrateV2Students(string json, out Dictionary<string, Student?> map, List<string> warnings)
    {
        using var document = JsonDocument.Parse(json);
        var list = new StudentList();
        map = new Dictionary<string, Student?>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return list;
        foreach (var item in document.RootElement.EnumerateObject())
        {
            var value = item.Value;
            var student = new Student
            {
                Name = item.Name,
                Id = GetString(value, "id"),
                Gender = GetString(value, "gender"),
                Group = GetString(value, "group"),
                Exists = GetBool(value, "exist", true),
                Tags = JoinTags(value),
                RecordId = Guid.NewGuid()
            };
            list.Students.Add(student);
            AddLegacyKeys(map, student.Id, student.Name, student);
        }
        return list;
    }

    private static PrizeList MigrateV2Prizes(string json, out Dictionary<string, Prize?> map, List<string> warnings)
    {
        using var document = JsonDocument.Parse(json);
        var list = new PrizeList();
        map = new Dictionary<string, Prize?>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return list;
        foreach (var item in document.RootElement.EnumerateObject())
        {
            var value = item.Value;
            var prize = new Prize
            {
                Name = item.Name,
                Id = GetString(value, "id"),
                Exists = GetBool(value, "exist", true),
                Tags = JoinTags(value),
                Weight = GetDouble(value, "weight", 1),
                Count = Math.Max(0, GetInt(value, "count", 1)),
                RecordId = Guid.NewGuid()
            };
            list.Prizes.Add(prize);
            AddLegacyKeys(map, prize.Id, prize.Name, prize);
        }
        return list;
    }

    private static StudentHistory MigrateV2StudentHistory(string json, Dictionary<string, Student?> records, List<string> warnings)
    {
        using var document = JsonDocument.Parse(json);
        var result = new StudentHistory();
        var root = document.RootElement;
        result.TotalRounds = GetInt(root, "total_rounds", 0);
        result.TotalStats = GetInt(root, "total_stats", 0);
        if (!root.TryGetProperty("students", out var students) || students.ValueKind != JsonValueKind.Object)
            return result;
        foreach (var item in students.EnumerateObject())
        {
            if (!records.TryGetValue(item.Name, out var student) || student is null)
            {
                warnings.Add($"v2 点名历史“{item.Name}”无法唯一关联到名单，已保留原始数据。");
                continue;
            }
            result.Students[student.RecordId.ToString("N")] = MigrateV2History(item.Value, student.RecordId, student.Id, student.Name, student.Gender, student.Group);
        }
        return result;
    }

    private static PrizeHistory MigrateV2PrizeHistory(string json, Dictionary<string, Prize?> records, List<string> warnings)
    {
        using var document = JsonDocument.Parse(json);
        var result = new PrizeHistory();
        var root = document.RootElement;
        result.TotalRounds = GetInt(root, "total_rounds", 0);
        result.TotalStats = GetInt(root, "total_stats", 0);
        if (!root.TryGetProperty("lotterys", out var prizes) || prizes.ValueKind != JsonValueKind.Object)
            return result;
        foreach (var item in prizes.EnumerateObject())
        {
            if (!records.TryGetValue(item.Name, out var prize) || prize is null)
            {
                warnings.Add($"v2 抽奖历史“{item.Name}”无法唯一关联到奖品池，已保留原始数据。");
                continue;
            }
            result.Prizes[prize.RecordId.ToString("N")] = MigrateV2History(item.Value, prize.RecordId, prize.Id, prize.Name, string.Empty, string.Empty);
        }
        return result;
    }

    private static History MigrateV2History(JsonElement source, Guid recordId, string number, string name, string gender, string group)
    {
        var history = new History
        {
            TotalCount = GetInt(source, "total_count", 0),
            RoundsMissed = GetInt(source, "rounds_missed", 0),
            LastDrawnTime = ParseV2Date(GetString(source, "last_drawn_time"))
        };
        if (!source.TryGetProperty("history", out var items) || items.ValueKind != JsonValueKind.Array)
            return history;
        foreach (var item in items.EnumerateArray())
        {
            history.Histories.Add(new HistoryItem
            {
                RecordId = recordId.ToString("N"),
                RecordNumber = number,
                RecordName = name,
                RecordGender = gender,
                RecordGroup = group,
                DrawTime = ParseV2Date(GetString(item, "draw_time")),
                DrawNumbers = GetInt(item, "draw_people_numbers", GetInt(item, "draw_lottery_numbers", 1)),
                DrawGroup = GetString(item, "draw_group"),
                DrawGender = GetString(item, "draw_gender"),
                DrawMethod = GetInt(item, "draw_method", 0),
                Weight = GetDouble(item, "weight", 1)
            });
        }
        return history;
    }

    private void CommitCandidate(string staging, IReadOnlyList<string> roots, bool replaceMissingRoots, List<string> warnings)
    {
        var previous = Path.Combine(staging, "previous");
        Directory.CreateDirectory(previous);
        var committed = new List<(string Target, string Previous)>();
        try
        {
            foreach (var root in roots.Select(NormalizeLegacyPath).Distinct(StringComparer.OrdinalIgnoreCase))
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

    private void ReloadCurrentProfiles()
    {
        if (profileService.StudentListConfig is not null)
            profileService.LoadStudentProfile(profileService.StudentListConfig.Name, saveCurrent: false);
        if (profileService.PrizeListConfig is not null)
            profileService.LoadPrizeProfile(profileService.PrizeListConfig.Name, saveCurrent: false);
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

    private static string NormalizeLegacyPath(string path)
    {
        path = NormalizePath(path);
        return path switch
        {
            "config" => "config/settings.json",
            _ when path.StartsWith("config/", StringComparison.OrdinalIgnoreCase) => path,
            _ when path.StartsWith("data/", StringComparison.OrdinalIgnoreCase) => path[5..],
            _ => path
        };
    }

    private static bool IsAllowedLegacyEntry(string path)
    {
        var normalized = NormalizePath(path);
        return normalized == "version.json" || LegacyRoots.Any(root => normalized.Equals(root, StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));
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
        return paths.Select(NormalizeLegacyPath).Select(path => path.StartsWith("config/settings.json", StringComparison.OrdinalIgnoreCase)
                ? "config/settings.json"
                : path.StartsWith("configs/plugins", StringComparison.OrdinalIgnoreCase)
                    ? "configs/plugins"
                    : path.StartsWith("legacy/v2-history", StringComparison.OrdinalIgnoreCase)
                        ? "legacy/v2-history"
                        : path.Split('/')[0])
            .Where(IsManagedPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') || path.Contains('\0'))
            throw new InvalidDataException("归档包含非法路径。");
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or ".." || part.Any(char.IsControl)))
            throw new InvalidDataException("归档包含非法路径。");
        return string.Join('/', parts);
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

    private static void WriteJson(string path, object value) => WriteText(path, JsonSerializer.Serialize(value, ConfigServiceBase.JsonOptions));
    private static void WriteText(string path, string text)
    {
        EnsureParents(path);
        File.WriteAllText(path, text, Encoding.UTF8);
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
    private static void AddLegacyKeys<T>(Dictionary<string, T?> map, string id, string name, T value) where T : class
    {
        AddLegacyKey(map, id, value);
        AddLegacyKey(map, name, value);
    }
    private static void AddLegacyKey<T>(Dictionary<string, T?> map, string key, T value) where T : class
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (map.TryGetValue(key, out var existing))
        {
            if (!ReferenceEquals(existing, value)) map[key] = null;
            return;
        }
        map[key] = value;
    }
    private static string JoinTags(JsonElement value) => value.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array ? string.Join(' ', tags.EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item))) : string.Empty;
    private static string GetString(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.ToString() : string.Empty;
    private static bool GetBool(JsonElement element, string name, bool fallback) => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    private static int GetInt(JsonElement element, string name, int fallback) => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;
    private static double GetDouble(JsonElement element, string name, double fallback) => element.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) ? result : fallback;
    private static DateTime ParseV2Date(string value) => DateTime.TryParse(value, out var parsed) ? parsed : DateTime.MinValue;
    private static OnlineStatusMode ParseOnlineStatus(string? value) => value?.ToLowerInvariant() switch { "off" => OnlineStatusMode.Off, "anonymous" => OnlineStatusMode.Anonymous, _ => OnlineStatusMode.Full };
    private static LanguageMode ParseV2Language(string? value) => value?.ToUpperInvariant() switch
    {
        "EN_US" or "ENGLISH" => LanguageMode.English,
        "JA_JP" or "JAPANESE" => LanguageMode.Japanese,
        _ => LanguageMode.ChineseSimplified
    };
}
