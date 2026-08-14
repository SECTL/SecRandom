using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.PluginSdk;

namespace SecRandom.Services.Plugins;

/// <summary>
///     Forwards the application start/stop lifecycle to every loaded plugin entrance. Plugins override
///     <see cref="PluginBase.OnAppStarted"/> / <see cref="PluginBase.OnAppStopping"/> instead of subscribing
///     to <see cref="IAppLifecycleService"/> themselves. The bridge is a HostedService so it subscribes
///     before the host's own events fire.
/// </summary>
public sealed class PluginLifecycleBridge(
    IEnumerable<PluginBase> plugins,
    IAppLifecycleService appLifecycle,
    ILogger<PluginLifecycleBridge> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        appLifecycle.AppStarted += OnAppStarted;
        appLifecycle.AppStopping += OnAppStopping;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        appLifecycle.AppStarted -= OnAppStarted;
        appLifecycle.AppStopping -= OnAppStopping;
        return Task.CompletedTask;
    }

    private void OnAppStarted(object? sender, EventArgs e)
    {
        foreach (var plugin in plugins)
        {
            try
            {
                plugin.OnAppStarted();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Plugin {PluginId} OnAppStarted failed.", GetPluginId(plugin));
            }
        }
    }

    private void OnAppStopping(object? sender, EventArgs e)
    {
        foreach (var plugin in plugins)
        {
            try
            {
                plugin.OnAppStopping();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Plugin {PluginId} OnAppStopping failed.", GetPluginId(plugin));
            }
        }
    }

    private static string GetPluginId(PluginBase plugin)
    {
        return plugin.Info?.Manifest.Id ?? plugin.GetType().Name;
    }
}
