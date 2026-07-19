using System.ComponentModel;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;

namespace SecRandom.Mobile.Services;

/// <summary>
/// Mobile-only online counter reporter. It never performs network work while disabled.
/// </summary>
internal sealed class MobileOnlineStatusService(
    MainConfigHandler configHandler,
    MobileDeviceUuidStore deviceUuidStore,
    ILogger<MobileOnlineStatusService> logger) : BackgroundService
{
    private static readonly Uri ReportUri = new("https://appwrite.sectl.cn/api/stats/online");
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var privacy = configHandler.Data.General.PrivacySettings;
        privacy.PropertyChanged += PrivacyChanged;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (privacy.OnlineStatusMode != OnlineStatusMode.Off)
                    await ReportAsync(privacy.OnlineStatusMode, stoppingToken).ConfigureAwait(false);

                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            privacy.PropertyChanged -= PrivacyChanged;
            _httpClient.Dispose();
        }
    }

    private void PrivacyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(configHandler.Data.General.PrivacySettings.OnlineStatusMode))
            logger.LogDebug("Mobile online status policy changed.");
    }

    private async Task ReportAsync(OnlineStatusMode mode, CancellationToken cancellationToken)
    {
        var anonymous = mode == OnlineStatusMode.Anonymous;
        var payload = new
        {
            platform_id = "69c8cd6a0012dd3ea10a",
            device_uuid = anonymous ? Guid.Empty.ToString() : deviceUuidStore.GetOrCreate().ToString(),
            device_type = OnlineStatusDeviceType.Detect(),
            ip_address = "0.0.0.0",
            country = "未知",
            province = "未知",
            city = "未知",
            district = "未知"
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(ReportUri, payload, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogDebug(exception, "Mobile online status report failed.");
        }
    }
}
