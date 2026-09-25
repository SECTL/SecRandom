using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Services.Archive;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Auth;
using SecRandom.Shared;

namespace SecRandom.Services.ImportExport;

public sealed record CloudBackupDescriptor(
    string BackupId,
    string DisplayName,
    DateTimeOffset? CreatedAt,
    long TotalBytes,
    int PartCount,
    bool IsComplete,
    string? ManifestFileId,
    string DeviceTag = "")
{
    public bool CanRestore => IsComplete && !string.IsNullOrWhiteSpace(ManifestFileId);
}

public sealed record CloudBackupProgress(string Stage, int Completed, int Total)
{
    public const string PartsStage = "parts";
    public const string DoneStage = "done";
}

/// <summary>
///     Account cloud-sync orchestration for the signed-in SECTL account: packages a cloud backup
///     archive, uploads it as verified parts plus a trailing manifest, lists the backups that belong
///     to this application scope, rebuilds one after per-part and whole-archive verification, and
///     deletes backups. Every upload stamps a device alias into its id, so the list tells several
///     signed-in devices apart and retention stays per device. The anonymous device-transfer channel
///     stays on SecRandom Sync; this service only ever talks to the authenticated SECTL personal
///     cloud API.
/// </summary>
public sealed class CloudBackupService(
    SectlAuthService authService,
    SectlCloudStorageClient cloudClient,
    MainConfigHandler configHandler,
    IImportExportService importExportService,
    ILogger<CloudBackupService> logger)
{
    public bool IsSignedIn => authService.IsSignedIn;

    /// <summary>
    ///     Raised after an automatic upload lands, so an open backup page can reload the list and the
    ///     (possibly reduced) account quota instead of polling. Manual uploads report through the
    ///     calling page's own refresh path.
    /// </summary>
    public event EventHandler? AutomaticBackupUploaded;

    public Task<SectlCloudQuota> GetQuotaAsync(CancellationToken cancellationToken = default) =>
        cloudClient.GetUsageAsync(cancellationToken);

    public async Task<IReadOnlyList<CloudBackupDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        var files = await cloudClient.ListFilesAsync(cancellationToken).ConfigureAwait(false);
        return GroupBackups(files);
    }

    /// <summary>
    ///     Packages the current cloud root set and uploads it. Parts are uploaded first and the
    ///     manifest last, so an interrupted upload can never look like a complete backup; a failure
    ///     rolls back the parts of this attempt.
    /// </summary>
    public async Task<CloudBackupDescriptor> UploadAsync(IProgress<CloudBackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var deviceTag = ResolveDeviceTag();
        var backupId = CloudBackupPackage.CreateBackupId(DateTime.UtcNow, deviceTag);
        var archivePath = GetCloudCachePath($"{backupId}.zip");
        var uploaded = new List<SectlCloudFile>();
        try
        {
            // The manifest must describe the roots that were actually packed, so the set is resolved
            // from the cloud backup-content selection before the archive is written.
            var roots = importExportService.GetCloudBackupRoots();
            await importExportService.ExportCloudBackupAsync(archivePath, cancellationToken).ConfigureAwait(false);
            var archive = await File.ReadAllBytesAsync(archivePath, cancellationToken).ConfigureAwait(false);
            var parts = CloudBackupPackage.Slice(archive, CloudBackupPackage.DefaultPartBytes);
            var manifest = CloudBackupPackage.BuildManifest(backupId, parts, CloudBackupPackage.DefaultPartBytes,
                roots, DateTime.UtcNow, GlobalConstants.Version, archive);
            CloudBackupPackage.ValidateManifest(manifest);

            for (var index = 0; index < parts.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uploaded.Add(await cloudClient
                    .UploadAsync(manifest.Parts[index].Name, "application/octet-stream", parts[index], cancellationToken)
                    .ConfigureAwait(false));
                progress?.Report(new CloudBackupProgress(CloudBackupProgress.PartsStage, index + 1, parts.Count + 1));
            }

            var manifestFile = await cloudClient
                .UploadAsync(CloudBackupPackage.BuildManifestName(backupId), "application/json",
                    CloudBackupPackage.SerializeManifest(manifest), cancellationToken)
                .ConfigureAwait(false);
            uploaded.Add(manifestFile);
            progress?.Report(new CloudBackupProgress(CloudBackupProgress.DoneStage, parts.Count + 1, parts.Count + 1));
            logger.LogInformation("云端备份上传完成：备份={BackupId}，分片={Parts}，大小={Bytes}。",
                backupId, parts.Count, archive.LongLength);
            return new CloudBackupDescriptor(backupId, manifest.ArchiveName, DateTimeOffset.UtcNow,
                archive.LongLength, parts.Count, true, manifestFile.FileId, deviceTag);
        }
        catch
        {
            await RollbackAsync(uploaded).ConfigureAwait(false);
            throw;
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    /// <summary>
    ///     Automatic cloud backup. It uploads exactly like the manual path, but it also owns the
    ///     account's storage budget: an exhausted quota removes the account-wide oldest backup and
    ///     retries, while the configured retention limit only drops this device's own surplus so
    ///     another signed-in machine keeps its backups. A manual upload never deletes anything
    ///     implicitly, so a failure there still surfaces as a quota error.
    /// </summary>
    public async Task<CloudBackupDescriptor> UploadAutomaticAsync(int maximumBackups,
        IProgress<CloudBackupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            CloudBackupDescriptor descriptor;
            try
            {
                descriptor = await UploadAsync(progress, cancellationToken).ConfigureAwait(false);
            }
            catch (SectlCloudStorageException exception) when (exception.Code == "storage_exceeded")
            {
                var oldest = await FindOldestAsync(cancellationToken).ConfigureAwait(false);
                if (oldest is null)
                    throw;

                logger.LogWarning("云端空间不足，删除最早的云端备份后重试：备份={BackupId}。", oldest.BackupId);
                await DeleteAsync(oldest, cancellationToken).ConfigureAwait(false);
                continue;
            }

            await TrimAsync(maximumBackups, cancellationToken).ConfigureAwait(false);
            AutomaticBackupUploaded?.Invoke(this, EventArgs.Empty);
            return descriptor;
        }
    }

    /// <summary>
    ///     Downloads one backup, verifies every part and the rebuilt archive, and returns the local
    ///     verified archive path. The caller owns the restore step so the existing inspection,
    ///     confirmation, snapshot, and restart flow stays unchanged.
    /// </summary>
    public async Task<string> DownloadAsync(CloudBackupDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!descriptor.CanRestore)
            throw new InvalidDataException("云端备份不完整，无法下载恢复。");

        var manifestBytes = await cloudClient.DownloadAsync(descriptor.ManifestFileId!, cancellationToken)
            .ConfigureAwait(false);
        var manifest = CloudBackupPackage.DeserializeManifest(manifestBytes);
        if (!string.Equals(manifest.BackupId, descriptor.BackupId, StringComparison.Ordinal))
            throw new InvalidDataException("云端备份清单与备份标识不匹配。");
        CloudBackupPackage.ValidateManifest(manifest);

        var files = await cloudClient.ListFilesAsync(cancellationToken).ConfigureAwait(false);
        var partsByName = new Dictionary<string, SectlCloudFile>(StringComparer.Ordinal);
        foreach (var file in files)
            partsByName[file.FileName] = file;

        var parts = new byte[manifest.Parts.Count][];
        for (var index = 0; index < manifest.Parts.Count; index++)
        {
            var expected = manifest.Parts[index];
            if (!partsByName.TryGetValue(expected.Name, out var partFile))
                throw new InvalidDataException($"云端备份缺少第 {expected.Index} 个分片。");
            parts[index] = await cloudClient.DownloadAsync(partFile.FileId, cancellationToken).ConfigureAwait(false);
        }

        var archive = CloudBackupPackage.MergeAndVerify(manifest, parts);
        var archivePath = GetCloudCachePath($"{descriptor.BackupId}.zip");
        await File.WriteAllBytesAsync(archivePath, archive, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("云端备份已下载并校验：备份={BackupId}，分片={Parts}，大小={Bytes}。",
            descriptor.BackupId, manifest.Parts.Count, archive.LongLength);
        return archivePath;
    }

    /// <summary>
    ///     Deletes every part of one backup and the manifest only after all parts are gone, so an
    ///     interrupted delete stays visible as an incomplete backup that can be retried.
    /// </summary>
    public async Task DeleteAsync(CloudBackupDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var owned = await FindBackupFilesAsync(descriptor.BackupId, cancellationToken).ConfigureAwait(false);
        var failures = 0;
        foreach (var part in owned.Where(file => !IsManifest(file.FileName)))
            failures += await TryDeleteAsync(part, cancellationToken).ConfigureAwait(false);

        if (failures == 0)
        {
            foreach (var manifest in owned.Where(file => IsManifest(file.FileName)))
                failures += await TryDeleteAsync(manifest, cancellationToken).ConfigureAwait(false);
        }

        if (failures > 0)
            throw new SectlCloudStorageException("delete_failed", "部分云端备份文件删除失败，请稍后重试。");
        logger.LogInformation("云端备份已删除：备份={BackupId}，文件数={Files}。", descriptor.BackupId, owned.Count);
    }

    /// <summary>Removes every backup group that has no manifest or is missing parts.</summary>
    public async Task<int> PurgeIncompleteAsync(CancellationToken cancellationToken = default)
    {
        var descriptors = await ListAsync(cancellationToken).ConfigureAwait(false);
        var removed = 0;
        foreach (var descriptor in descriptors.Where(item => !item.IsComplete))
        {
            await DeleteAsync(descriptor, cancellationToken).ConfigureAwait(false);
            removed++;
        }

        return removed;
    }

    /// <summary>
    ///     Newest upload time among this device's own backups, so the automatic cadence is per device
    ///     and another signed-in machine's uploads do not postpone this one. Null means this device has
    ///     not uploaded yet.
    /// </summary>
    public async Task<DateTimeOffset?> GetLatestOwnBackupTimeAsync(CancellationToken cancellationToken = default)
    {
        var deviceTag = ResolveDeviceTag();
        if (deviceTag.Length == 0)
            return null;

        var backups = await ListAsync(cancellationToken).ConfigureAwait(false);
        return backups
            .Where(backup => string.Equals(backup.DeviceTag, deviceTag, StringComparison.OrdinalIgnoreCase))
            .Select(backup => backup.CreatedAt)
            .Max();
    }

    /// <summary>
    ///     Cloud state is never cached, so the oldest backup is rediscovered from the account on
    ///     every attempt; <see cref="ListAsync" /> orders newest first. Quota pressure is the one
    ///     deletion that deliberately crosses device boundaries.
    /// </summary>
    private async Task<CloudBackupDescriptor?> FindOldestAsync(CancellationToken cancellationToken)
    {
        var backups = await ListAsync(cancellationToken).ConfigureAwait(false);
        return backups.Count == 0 ? null : backups[^1];
    }

    /// <summary>Keeps at most <paramref name="maximumBackups" /> of this device's own backups.</summary>
    private async Task TrimAsync(int maximumBackups, CancellationToken cancellationToken)
    {
        if (maximumBackups <= 0)
            return;

        var deviceTag = ResolveDeviceTag();
        if (deviceTag.Length == 0)
            return;

        var backups = await ListAsync(cancellationToken).ConfigureAwait(false);
        var own = backups
            .Where(backup => string.Equals(backup.DeviceTag, deviceTag, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var expired in own.Skip(maximumBackups))
        {
            logger.LogInformation("云端备份超过保留上限，删除本设备最早的备份：备份={BackupId}。", expired.BackupId);
            await DeleteAsync(expired, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<SectlCloudFile>> FindBackupFilesAsync(string backupId,
        CancellationToken cancellationToken)
    {
        var files = await cloudClient.ListFilesAsync(cancellationToken).ConfigureAwait(false);
        return files.Where(file =>
                CloudBackupPackage.TryParseFileName(file.FileName, out var fileBackupId, out _, out _, out _) &&
                string.Equals(fileBackupId, backupId, StringComparison.Ordinal))
            .ToArray();
    }

    private async Task<int> TryDeleteAsync(SectlCloudFile file, CancellationToken cancellationToken)
    {
        try
        {
            await cloudClient.DeleteAsync(file.FileId, file.FileDocumentId, cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "删除云端备份文件失败：文件={FileName}", file.FileName);
            return 1;
        }
    }

    private async Task RollbackAsync(IReadOnlyList<SectlCloudFile> uploaded)
    {
        if (uploaded.Count == 0)
            return;

        logger.LogWarning("云端备份上传未完成，正在清理已上传的分片：数量={Count}", uploaded.Count);
        foreach (var file in uploaded)
        {
            // Cleanup must not be cancelled by the failure that triggered it.
            await TryDeleteAsync(file, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<CloudBackupDescriptor> GroupBackups(IReadOnlyList<SectlCloudFile> files)
    {
        var groups = new Dictionary<string, BackupGroup>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (!CloudBackupPackage.TryParseFileName(file.FileName, out var backupId, out var partIndex, out var partCount, out var isManifest))
                continue;
            if (!groups.TryGetValue(backupId, out var group))
            {
                group = new BackupGroup(backupId);
                groups[backupId] = group;
            }

            if (isManifest)
                group.Manifest = file;
            else
                group.Parts[partIndex] = file;
            group.DeclaredPartCount = Math.Max(group.DeclaredPartCount, partCount);
        }

        return groups.Values
            .Select(group => group.ToDescriptor())
            .OrderByDescending(descriptor => descriptor.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(descriptor => descriptor.BackupId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsManifest(string fileName) =>
        CloudBackupPackage.TryParseFileName(fileName, out _, out _, out _, out var isManifest) && isManifest;

    /// <summary>
    ///     Alias stamped on this device's uploads: the configured alias when set, otherwise the host
    ///     name. It is a user-visible display label, not a device identity — the device UUID stays out
    ///     of every cloud archive and restore.
    /// </summary>
    private string ResolveDeviceTag()
    {
        var configured = configHandler.Data.General.Backup.CloudDeviceAlias;
        var deviceName = string.IsNullOrWhiteSpace(configured) ? Environment.MachineName : configured;
        return CloudBackupPackage.BuildDeviceTag(deviceName);
    }

    /// <summary>Backup list label: the id, plus the device alias when the id carries one.</summary>
    private static string BuildDisplayName(string backupId, string deviceTag) =>
        deviceTag.Length == 0 ? backupId : $"{backupId} ({deviceTag})";

    private static string GetCloudCachePath(string fileName) =>
        Utils.GetFilePath("cache", "cloud-backup", fileName);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover staging archive is harmless and stays outside every archive root.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class BackupGroup(string backupId)
    {
        public string BackupId { get; } = backupId;
        public SectlCloudFile? Manifest { get; set; }
        public SortedDictionary<int, SectlCloudFile> Parts { get; } = new();
        public int DeclaredPartCount { get; set; }

        public CloudBackupDescriptor ToDescriptor()
        {
            var isComplete = Manifest is not null && Parts.Count > 0 && DeclaredPartCount > 0 &&
                             Parts.Count == DeclaredPartCount &&
                             Enumerable.Range(1, DeclaredPartCount).All(Parts.ContainsKey);
            var size = Parts.Values.Sum(part => part.Size);
            var created = Manifest?.CreatedAt ?? Parts.Values
                .Select(part => part.CreatedAt)
                .Where(timestamp => timestamp.HasValue)
                .OrderByDescending(timestamp => timestamp)
                .FirstOrDefault();
            var deviceTag = CloudBackupPackage.TryGetDeviceTag(BackupId, out var tag) ? tag : string.Empty;
            return new CloudBackupDescriptor(BackupId, BuildDisplayName(BackupId, deviceTag), created,
                size, Parts.Count, isComplete, Manifest?.FileId, deviceTag);
        }
    }
}
