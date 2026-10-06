using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Abstraction.Services.Diagnostics;
using SecRandom.Core.Abstraction.Services.Messaging;
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
    ILogger<PluginLifecycleBridge> logger,
    IPluginDiagnosticsService? diagnostics = null,
    IPluginEventBus? eventBus = null) : IHostedService
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
                PublishPluginLoaded(plugin);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Plugin {PluginId} OnAppStarted failed.", GetPluginId(plugin));
                ReportFailure(plugin, "OnAppStarted", exception);
            }
        }

        // 插件已经在 Initialize 里订阅好了，这里再广播"全部启动完毕"。
        Publish(new HostEvents.AppStarted());
    }

    private void OnAppStopping(object? sender, EventArgs e)
    {
        Publish(new HostEvents.AppStopping());

        foreach (var plugin in plugins)
        {
            try
            {
                plugin.OnAppStopping();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Plugin {PluginId} OnAppStopping failed.", GetPluginId(plugin));
                ReportFailure(plugin, "OnAppStopping", exception);
            }
        }
    }

    private void PublishPluginLoaded(PluginBase plugin)
    {
        var manifest = plugin.Info?.Manifest;
        if (manifest is null)
            return;

        Publish(new HostEvents.PluginLoaded(manifest.Id, manifest.Name, manifest.Version));
    }

    /// <summary>事件总线不是每个 shell 都有，发布失败也不该影响启动/退出流程。</summary>
    private void Publish<TEvent>(TEvent payload) where TEvent : class
    {
        try
        {
            eventBus?.Publish(payload);
        }
        catch (Exception publishException)
        {
            logger.LogDebug(publishException, "Publishing {EventType} failed.", typeof(TEvent).Name);
        }
    }

    /// <summary>把生命周期钩子里的异常也留一条诊断记录，插件详情页就能看到"它为什么没反应"。</summary>
    private void ReportFailure(PluginBase plugin, string area, Exception exception)
    {
        try
        {
            diagnostics?.Report(
                GetPluginId(plugin),
                area,
                exception.Message,
                DiagnosticLevel.Error);
        }
        catch (Exception reportException)
        {
            // 诊断本身出错不该影响退出流程。
            logger.LogDebug(reportException, "Reporting the plugin lifecycle failure failed.");
        }
    }

    private static string GetPluginId(PluginBase plugin)
    {
        return plugin.Info?.Manifest.Id ?? plugin.GetType().Name;
    }
}
