using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SecRandom.Services.Plugins;

public sealed class PluginHostedService(
    IEnumerable<LoadedPluginRegistration> plugins,
    ILogger<PluginHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var registration in plugins)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            try
            {
                await registration.Plugin.OnLoadedAsync(registration.RuntimeContext).ConfigureAwait(false);
                PluginManagerService.SetStartupResult(registration.Plugin.Id, Core.Plugins.PluginStatus.Loaded, null);
                logger.LogInformation("插件运行时已启动：插件={PluginId}。", registration.Plugin.Id);
            }
            catch (Exception ex)
            {
                PluginManagerService.SetStartupResult(registration.Plugin.Id, Core.Plugins.PluginStatus.LoadFailed, ex.Message);
                logger.LogError(ex, "插件运行时启动失败：插件={PluginId}。", registration.Plugin.Id);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
