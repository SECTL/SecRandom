using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Services.ImportExport;
using SecRandom.Shared;

namespace SecRandom.Services.Security;

internal enum SettingsIntegrityRecoveryStatus
{
    Succeeded,
    NoBackupAvailable,
    CloudNotSignedIn,
    Failed
}

/// <summary>
///     自动恢复的结果。<see cref="Detail" /> 只承载技术原因（文件名、异常消息等），
///     面向用户的文案由调用方按状态选择本地化资源。
/// </summary>
internal sealed record SettingsIntegrityRecoveryResult(
    SettingsIntegrityRecoveryStatus Status,
    bool FromCloud,
    string? BackupName,
    string? PreRestorePath,
    string? Detail)
{
    public bool Succeeded => Status == SettingsIntegrityRecoveryStatus.Succeeded;
}

/// <summary>
///     设置文件防篡改校验发现改动后的自动恢复。按配置的来源顺序找到最近一次包含
///     <c>config/settings.json</c> 的备份，先把当前文件另存为「恢复前」副本，再用备份内容
///     原子替换，最后重载配置以刷新指纹记录。只处理设置文件本身，名单、历史等其他数据不变；
///     任何一步失败都保留原文件，让启动流程回退到安全密码确认闸门。
/// </summary>
internal sealed class SettingsIntegrityRecoveryService
{
    internal const string SettingsEntryPath = "config/settings.json";
    private const string PreRestoreDirectoryName = "integrity";

    private readonly MainConfigHandler _configHandler;
    private readonly IImportExportService _importExportService;
    private readonly ILogger<SettingsIntegrityRecoveryService> _logger;
    private readonly string? _configFilePath;
    private readonly string? _backupDirectory;

    public SettingsIntegrityRecoveryService(
        MainConfigHandler configHandler,
        IImportExportService importExportService,
        ILogger<SettingsIntegrityRecoveryService> logger,
        string? configFilePath = null,
        string? backupDirectory = null)
    {
        _configHandler = configHandler;
        _importExportService = importExportService;
        _logger = logger;
        _configFilePath = configFilePath;
        _backupDirectory = backupDirectory;
    }

    private string ConfigFilePath => _configFilePath ?? _configHandler.Data.ConfigFilePath;
    private string BackupDirectory => _backupDirectory ?? Utils.GetDirectoryPath("backup");

    public async Task<SettingsIntegrityRecoveryResult> TryRecoverAsync(CancellationToken cancellationToken = default)
    {
        var source = _configHandler.Data.SecuritySettings.SettingsIntegrityRestoreSource;
        var cloudPreferred = source == SettingsIntegrityRestoreSource.CloudThenLocal;
        SettingsIntegrityRecoveryResult? failure = null;

        try
        {
            foreach (var attempt in ResolveAttemptOrder(source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = attempt == RecoverySourceKind.Cloud
                    ? await TryRecoverFromCloudAsync(cancellationToken).ConfigureAwait(false)
                    : await TryRecoverFromLocalAsync(cancellationToken).ConfigureAwait(false);
                if (result.Succeeded)
                    return result;

                failure = PreferredFailure(failure, result, cloudPreferred);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 启动闸门不能让恢复流程本身的异常冒泡，否则用户既没恢复也没得到确认入口。
            _logger.LogError(exception, "设置文件自动恢复时发生未预期的错误。");
            return Failed(false, Describe(exception));
        }

        var resolved = failure ?? NoBackup();
        _logger.LogWarning("设置文件自动恢复未完成：状态={Status}，原因={Detail}。", resolved.Status, resolved.Detail);
        return resolved;
    }

    /// <summary>
    ///     来源顺序与设置卡一一对应：仅本地、本地优先云端补全、云端优先本地兜底。
    /// </summary>
    private static RecoverySourceKind[] ResolveAttemptOrder(SettingsIntegrityRestoreSource source) =>
        source switch
        {
            SettingsIntegrityRestoreSource.LocalThenCloud => [RecoverySourceKind.Local, RecoverySourceKind.Cloud],
            SettingsIntegrityRestoreSource.CloudThenLocal => [RecoverySourceKind.Cloud, RecoverySourceKind.Local],
            _ => [RecoverySourceKind.Local]
        };

    /// <summary>
    ///     失败信息展示最有用的那条：真实的文件/恢复错误优先于「没有可用备份」，而「未登录账号」
    ///     只在用户明确选择云端优先时才有意义。
    /// </summary>
    private static SettingsIntegrityRecoveryResult PreferredFailure(
        SettingsIntegrityRecoveryResult? current,
        SettingsIntegrityRecoveryResult candidate,
        bool cloudPreferred)
    {
        if (current is null)
            return candidate;

        var currentPriority = FailurePriority(current.Status, cloudPreferred);
        var candidatePriority = FailurePriority(candidate.Status, cloudPreferred);
        return candidatePriority > currentPriority ? candidate : current;
    }

    private static int FailurePriority(SettingsIntegrityRecoveryStatus status, bool cloudPreferred) =>
        status switch
        {
            SettingsIntegrityRecoveryStatus.Failed => 3,
            SettingsIntegrityRecoveryStatus.CloudNotSignedIn => cloudPreferred ? 2 : 1,
            _ => 1
        };

    private async Task<SettingsIntegrityRecoveryResult> TryRecoverFromLocalAsync(CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(BackupDirectory);
        if (!directory.Exists)
            return NoBackup();

        string? lastDetail = null;
        // 备份页只展示 *.zip；恢复只认同一批归档，并按创建时间从新到旧尝试。
        foreach (var file in directory.EnumerateFiles("*.zip").OrderByDescending(file => file.CreationTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await IsSupportedArchiveAsync(file.FullName, false, cancellationToken).ConfigureAwait(false))
            {
                lastDetail = file.Name;
                continue;
            }

            if (!TryReadSettingsEntry(file.FullName, out var payload, out var readError))
            {
                lastDetail = readError;
                continue;
            }

            if (!IsLoadableSettingsPayload(payload))
            {
                // 坏备份只跳过这一个候选，不能让最近一次备份的损坏内容阻断更早的可用备份。
                lastDetail = file.Name;
                continue;
            }

            if (TryCommitRestoredSettings(payload, out var preRestorePath, out var commitError))
                return Succeeded(false, file.Name, preRestorePath);

            return Failed(false, commitError);
        }

        return NoBackup(lastDetail);
    }

    private async Task<SettingsIntegrityRecoveryResult> TryRecoverFromCloudAsync(CancellationToken cancellationToken)
    {
        var cloud = IAppHost.TryGetService<CloudBackupService>();
        if (cloud is null || !cloud.IsSignedIn)
            return new SettingsIntegrityRecoveryResult(
                SettingsIntegrityRecoveryStatus.CloudNotSignedIn, true, null, null, null);

        IReadOnlyList<CloudBackupDescriptor> backups;
        try
        {
            backups = await cloud.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "列出云端备份失败，尝试其它来源。");
            return Failed(true, Describe(exception));
        }

        string? lastDetail = null;
        foreach (var descriptor in backups.Where(item => item.CanRestore)
                     .OrderByDescending(item => item.CreatedAt ?? DateTimeOffset.MinValue))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string archivePath;
            try
            {
                archivePath = await cloud.DownloadAsync(descriptor, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "下载云端备份失败：备份={BackupId}。", descriptor.BackupId);
                lastDetail = Describe(exception);
                continue;
            }

            try
            {
                if (!await IsSupportedArchiveAsync(archivePath, true, cancellationToken).ConfigureAwait(false))
                {
                    lastDetail = descriptor.DisplayName;
                    continue;
                }

                if (!TryReadSettingsEntry(archivePath, out var payload, out var readError))
                {
                    lastDetail = readError;
                    continue;
                }

                if (!IsLoadableSettingsPayload(payload))
                {
                    lastDetail = descriptor.DisplayName;
                    continue;
                }

                if (TryCommitRestoredSettings(payload, out var preRestorePath, out var commitError))
                    return Succeeded(true, descriptor.DisplayName, preRestorePath);

                return Failed(true, commitError);
            }
            finally
            {
                TryDeleteFile(archivePath);
            }
        }

        return NoBackup(lastDetail);
    }

    /// <summary>
    ///     备份必须先是受支持的 v3 归档；data/backup 里被替换成任意 ZIP 的文件不会被当作恢复来源。
    /// </summary>
    private async Task<bool> IsSupportedArchiveAsync(string archivePath, bool cloudArchive,
        CancellationToken cancellationToken)
    {
        try
        {
            var inspection = cloudArchive
                ? await _importExportService.InspectCloudBackupAsync(archivePath, cancellationToken).ConfigureAwait(false)
                : await _importExportService.InspectAllDataAsync(archivePath, cancellationToken).ConfigureAwait(false);
            return inspection.IsSupportedV3;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "备份归档校验失败：文件={FileName}。", Path.GetFileName(archivePath));
            return false;
        }
    }

    private bool TryReadSettingsEntry(string archivePath, out byte[] payload, out string? error)
    {
        payload = [];
        error = null;
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var entry = archive.Entries.FirstOrDefault(item =>
                string.Equals(item.FullName.Replace('\\', '/'), SettingsEntryPath, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                error = Path.GetFileName(archivePath);
                return false;
            }

            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            payload = buffer.ToArray();
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "读取备份中的设置文件失败：文件={FileName}。", Path.GetFileName(archivePath));
            error = Describe(exception);
            return false;
        }
    }

    /// <summary>
    ///     用备份内容替换设置文件：先写恢复前副本，再原子替换并重载配置；重载失败时回滚原文件，
    ///     保证「恢复失败」不会让用户同时失去当前文件和旧指纹。
    /// </summary>
    private bool TryCommitRestoredSettings(byte[] payload, out string? preRestorePath, out string? error)
    {
        preRestorePath = null;
        error = null;
        var configPath = ConfigFilePath;
        byte[]? previous = null;
        try
        {
            if (File.Exists(configPath))
            {
                previous = File.ReadAllBytes(configPath);
                preRestorePath = CreatePreRestorePath();
                WriteAtomic(preRestorePath, previous);
            }

            WriteAtomic(configPath, payload);
            try
            {
                _configHandler.Reload();
            }
            catch
            {
                if (previous is not null)
                    WriteAtomic(configPath, previous);
                throw;
            }

            _logger.LogInformation("设置文件已从备份恢复：恢复前副本={PreRestorePath}。", preRestorePath ?? string.Empty);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "写入恢复后的设置文件失败。");
            error = Describe(exception);
            preRestorePath = null;
            return false;
        }
    }

    /// <summary>
    ///     恢复内容必须仍是可用的设置文件；空对象、非对象或无法反序列化的内容一律拒绝，
    ///     否则一次坏备份会静默把用户配置重置成默认值。
    /// </summary>
    private static bool IsLoadableSettingsPayload(byte[] payload)
    {
        if (payload.Length == 0)
            return false;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.EnumerateObject().Any())
                return false;

            return JsonSerializer.Deserialize<MainConfigModel>(payload, ConfigServiceBase.JsonOptions) is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private string CreatePreRestorePath()
    {
        var directory = Path.Combine(BackupDirectory, PreRestoreDirectoryName);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"settings_before_restore_{DateTime.Now:yyyyMMdd_HHmmss}.json");
    }

    private static void WriteAtomic(string path, byte[] content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temporaryPath, content);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static SettingsIntegrityRecoveryResult Succeeded(bool fromCloud, string backupName,
        string? preRestorePath) =>
        new(SettingsIntegrityRecoveryStatus.Succeeded, fromCloud, backupName, preRestorePath, null);

    private static SettingsIntegrityRecoveryResult NoBackup(string? detail = null) =>
        new(SettingsIntegrityRecoveryStatus.NoBackupAvailable, false, null, null, detail);

    private static SettingsIntegrityRecoveryResult Failed(bool fromCloud, string? detail) =>
        new(SettingsIntegrityRecoveryStatus.Failed, fromCloud, null, null, detail);

    private static string Describe(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 缓存文件残留不影响恢复结果。
        }
    }

    private enum RecoverySourceKind
    {
        Local,
        Cloud
    }
}
