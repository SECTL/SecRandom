using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;

namespace SecRandom.Services.ImportExport;

public sealed class AutomaticBackupService(
    MainConfigHandler configHandler,
    IImportExportService importExportService,
    ILogger<AutomaticBackupService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private readonly string _backupDirectory = Path.Combine(Utils.DataRoot, "backup");

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
                logger.LogError(ex, "自动备份失败。");
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
        var intervalDays = Math.Max(1, settings.AutoBackupIntervalDays);
        if (!settings.AutoBackupEnabled || !HasBackupContent() || !IsDue(intervalDays))
            return;

        var path = await Task.Run(
            () => importExportService.CreateAutomaticBackup(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        logger.LogInformation("已创建自动备份：文件={FileName}。", Path.GetFileName(path));
    }

    private bool HasBackupContent()
    {
        var settings = configHandler.Data.General.Backup;
        return settings.IncludeConfig || settings.IncludeList || settings.IncludeHistory || settings.IncludeProofs ||
               settings.IncludeAudio || settings.IncludeCses || settings.IncludeImages || settings.IncludeThemes ||
               settings.IncludeLogs;
    }

    private bool IsDue(int intervalDays)
    {
        if (!Directory.Exists(_backupDirectory))
            return true;

        var latest = new DirectoryInfo(_backupDirectory)
            .EnumerateFiles("SecRandom_auto_*.zip")
            .OrderByDescending(file => file.CreationTimeUtc)
            .FirstOrDefault();
        return latest is null || DateTime.UtcNow - latest.CreationTimeUtc >= TimeSpan.FromDays(intervalDays);
    }
}
