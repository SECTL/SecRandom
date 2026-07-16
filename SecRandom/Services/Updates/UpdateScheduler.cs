using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Config;

namespace SecRandom.Services.Updates;

public sealed class UpdateScheduler(
    MainConfigHandler configHandler,
    UpdateCenterService updateCenter,
    ILogger<UpdateScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = configHandler.Data.UpdateSettings;
                var interval = settings.AutoUpdateMode switch
                {
                    1 => TimeSpan.FromDays(1),
                    2 => TimeSpan.FromDays(7),
                    _ => TimeSpan.Zero
                };
                if (interval > TimeSpan.Zero && DateTime.Now - settings.LastCheckTime >= interval)
                    await Dispatcher.UIThread.InvokeAsync(
                        () => updateCenter.CheckAsync(), DispatcherPriority.Normal).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "自动检查更新失败。");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
