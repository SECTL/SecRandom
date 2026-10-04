using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Config;

namespace SecRandom.Services.ImportExport;

/// <summary>
///     Desktop-only automatic cloud backup. It runs on the configured cadence while the app is open
///     and a SECTL account is signed in, and delegates every storage decision to
///     <see cref="CloudBackupService" />, which keeps the account inside its quota and keeps each
///     signed-in device inside its own retention budget.
/// </summary>
public sealed class CloudAutomaticBackupService(
    MainConfigHandler configHandler,
    CloudBackupService cloudBackupService,
    ILogger<CloudAutomaticBackupService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    /// <summary>
    ///     Logs the "encryption enabled but no passphrase" state once per process instead of every
    ///     hourly check, because it is a standing condition the user has to act on rather than a
    ///     failure that resolves on retry.
    /// </summary>
    private bool _warnedMissingEncryptionKey;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CreateBackupWhenDueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "云端自动备份失败。");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task CreateBackupWhenDueAsync(CancellationToken cancellationToken)
    {
        var settings = configHandler.Data.General.Backup;
        if (!settings.CloudAutoBackupEnabled || !cloudBackupService.IsSignedIn)
            return;

        // Encryption is on but this device has no passphrase yet: nothing may be uploaded, because
        // falling back to plaintext would silently defeat the setting the user turned on.
        if (settings.CloudEncryptionEnabled && !cloudBackupService.HasEncryptionKey)
        {
            if (!_warnedMissingEncryptionKey)
            {
                _warnedMissingEncryptionKey = true;
                logger.LogWarning("云端自动备份已跳过：已开启加密但尚未设置加密口令。");
            }

            return;
        }

        _warnedMissingEncryptionKey = false;

        var intervalDays = Math.Max(1, settings.CloudAutoBackupIntervalDays);
        if (!await IsDueAsync(intervalDays, cancellationToken).ConfigureAwait(false))
            return;

        var descriptor = await cloudBackupService
            .UploadAutomaticAsync(settings.CloudAutoBackupMaxCount, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("已创建云端自动备份：备份={BackupId}。", descriptor.BackupId);
    }

    /// <summary>
    ///     This device's newest cloud backup decides the cadence, so a manual upload also postpones the
    ///     next automatic one instead of stacking a duplicate right after it, while a second signed-in
    ///     machine's uploads neither postpone nor trigger this device.
    /// </summary>
    private async Task<bool> IsDueAsync(int intervalDays, CancellationToken cancellationToken)
    {
        var newest = await cloudBackupService.GetLatestOwnBackupTimeAsync(cancellationToken).ConfigureAwait(false);
        return newest is null || DateTimeOffset.UtcNow - newest.Value >= TimeSpan.FromDays(intervalDays);
    }
}
