using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SecRandom.Services.SecAgent;

/// <summary>
/// Quietly asks a running local SecAgent to install the SecRandom connector.
/// This is deliberately best-effort: SecRandom remains fully usable without SecAgent.
/// </summary>
public sealed class SecAgentPluginBootstrapHostedService(
    IHttpClientFactory httpClientFactory,
    ILogger<SecAgentPluginBootstrapHostedService> logger) : BackgroundService
{
    private const string ConnectorPluginId = "secrandom";
    private const string ConnectorPluginVersion = "0.1.1";
    private static readonly Uri BaseUri = new("http://127.0.0.1:42189/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the desktop host time to finish its own startup, and never hold up the UI.
        try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnsurePluginAsync(stoppingToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (HttpRequestException)
            {
                // SecAgent is optional and may simply not be installed/running.
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "SecAgent connector bootstrap was skipped.");
                return;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task EnsurePluginAsync(CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient();
        client.BaseAddress = BaseUri;
        client.Timeout = TimeSpan.FromSeconds(2);

        using var health = await client.GetAsync("health", cancellationToken).ConfigureAwait(false);
        if (!health.IsSuccessStatusCode) return;

        var installed = await client.GetFromJsonAsync<PluginListResponse>("plugins", JsonOptions, cancellationToken).ConfigureAwait(false);
        var current = installed?.Plugins?.FirstOrDefault(plugin =>
            string.Equals(plugin.Id, ConnectorPluginId, StringComparison.OrdinalIgnoreCase));
        if (current is not null && !IsOlderVersion(current.Version, ConnectorPluginVersion))
            return;

        var request = current is null
            ? new { pluginId = ConnectorPluginId, version = (string?)null }
            : new { pluginId = ConnectorPluginId, version = (string?)ConnectorPluginVersion };
        using var response = await client.PostAsJsonAsync("plugins/install", request, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            logger.LogInformation("Requested local SecAgent to install/update the SecRandom connector plugin to {Version}.", ConnectorPluginVersion);
        else
            logger.LogDebug("Local SecAgent declined SecRandom connector installation with HTTP {StatusCode}.", response.StatusCode);
    }

    private sealed class PluginListResponse
    {
        public List<PluginInfo>? Plugins { get; init; }
    }

    private sealed class PluginInfo
    {
        public string? Id { get; init; }
        public string? Version { get; init; }
    }

    private static bool IsOlderVersion(string? current, string desired)
    {
        if (Version.TryParse(current, out var currentVersion) && Version.TryParse(desired, out var desiredVersion))
            return currentVersion < desiredVersion;

        return !string.Equals(current, desired, StringComparison.OrdinalIgnoreCase);
    }
}
