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
