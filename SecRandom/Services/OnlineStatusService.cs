using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Config;

namespace SecRandom.Services;

/// <summary>
///     Reports this installation's online status to the SECTL platform statistics API.
///     Only what the service cannot observe by itself is sent: the platform id, the pseudo-anonymous
///     device UUID, and the device type. The public IP and its coarse region used to be resolved on the
///     client — including calls to third-party IP lookup services — and are deliberately gone: the
///     service sees the request address already, so shipping it from the client only leaked the address
///     to extra parties without adding information.
/// </summary>
public sealed class OnlineStatusService : BackgroundService
{
    private const string ErrorDisabled = "disabled";
    private const string ErrorRequestFailed = "request_failed";

    private static readonly TimeSpan ReportInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly Uri OnlineReportUri = new($"{ApiBaseUrl}/api/stats/online");
    private static readonly Uri OnlineStatsUri = new($"{ApiBaseUrl}/api/stats/platform/{PlatformId}");

    private const string ApiBaseUrl = "https://appwrite.sectl.cn";
    private const string PlatformId = "69c8cd6a0012dd3ea10a";

    private readonly DeviceUuidStore _deviceUuidStore;
    private readonly MainConfigHandler _configHandler;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OnlineStatusService> _logger;
    private PrivacySettingsConfig _privacySettings;
    private int _cachedOnlineCount;
    private long _cachedOnlineCountUpdatedAtUnixMilliseconds;

    public OnlineStatusService(
        MainConfigHandler configHandler,
        DeviceUuidStore deviceUuidStore,
        ILogger<OnlineStatusService> logger)
    {
        _configHandler = configHandler;
        _deviceUuidStore = deviceUuidStore;
        _logger = logger;
        _privacySettings = configHandler.Data.General.PrivacySettings;
        _httpClient = new HttpClient { Timeout = RequestTimeout };
    }

    public int CachedOnlineCount => Volatile.Read(ref _cachedOnlineCount);

    public DateTimeOffset CachedOnlineCountUpdatedAt
    {
        get
        {
            long unixMilliseconds = Volatile.Read(ref _cachedOnlineCountUpdatedAtUnixMilliseconds);
            return unixMilliseconds == 0
                ? default
                : DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Online status reporter started.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                OnlineStatusPolicy policy = ResolvePolicy();

                if (policy.IsEnabled)
                    await ReportOnceAsync(stoppingToken).ConfigureAwait(false);
                else
                    _logger.LogDebug("Online status reporting is disabled by privacy settings.");

                await Task.Delay(ReportInterval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _logger.LogDebug("Online status reporter stopped.");
        }
    }

    public override void Dispose()
    {
        _httpClient.Dispose();
        base.Dispose();
    }

    public void Refresh()
    {
        // Only the config object can be replaced (settings import); the mode itself is read on every cycle.
        _privacySettings = _configHandler.Data.General.PrivacySettings;
    }

    public async Task<OnlineStatsResult> GetOnlineStatsAsync(CancellationToken cancellationToken = default)
    {
        if (!ResolvePolicy().IsEnabled)
            return OnlineStatsResult.Failed(ErrorDisabled);

        try
        {
            using HttpResponseMessage response = await _httpClient
                .GetAsync(OnlineStatsUri, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return OnlineStatsResult.Failed(ErrorRequestFailed);

            OnlineStatsResult? stats = await response.Content
                .ReadFromJsonAsync<OnlineStatsResult>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return stats ?? OnlineStatsResult.Failed(ErrorRequestFailed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch online stats.");
            return OnlineStatsResult.Failed(ErrorRequestFailed);
        }
    }

    private async Task ReportOnceAsync(CancellationToken cancellationToken)
    {
        OnlineStatusPayload payload = OnlineStatusPayload.Create(
            PlatformId,
            _deviceUuidStore.GetOrCreate(),
            OnlineStatusDeviceType.Detect());

        try
        {
            using HttpResponseMessage response = await _httpClient
                .PostAsJsonAsync(OnlineReportUri, payload, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                await LogReportFailureAsync(response, cancellationToken).ConfigureAwait(false);
                return;
            }

            OnlineReportResult? result = await response.Content
                .ReadFromJsonAsync<OnlineReportResult>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (result?.OnlineCount is int onlineCount)
                UpdateOnlineCountCache(onlineCount);

            _logger.LogInformation(
                "Online status reported. Current online count: {OnlineCount}",
                result?.OnlineCount ?? 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Online status report timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Online status report connection failed.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Online status report failed.");
        }
    }

    private OnlineStatusPolicy ResolvePolicy()
    {
        return OnlineStatusPolicy.From(_privacySettings.OnlineStatusMode);
    }

    private async Task LogReportFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            ApiErrorResponse? error = await response.Content
                .ReadFromJsonAsync<ApiErrorResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            string message = error?.ErrorDescription ?? error?.Error ?? $"HTTP {(int)response.StatusCode}";
            _logger.LogWarning("Online status report failed: {Message}", message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Online status report failed: HTTP {StatusCode}", (int)response.StatusCode);
        }
    }

    private void UpdateOnlineCountCache(int count)
    {
        Volatile.Write(ref _cachedOnlineCount, count);
        Volatile.Write(
            ref _cachedOnlineCountUpdatedAtUnixMilliseconds,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    public sealed record OnlineStatusPolicy(OnlineStatusMode Mode, bool IsEnabled)
    {
        // Both reporting modes now carry the same fields: the service derives the address and its
        // region from the connection, so there is no client-side location payload left to mask.
        public static OnlineStatusPolicy From(OnlineStatusMode mode) =>
            new(mode, mode != OnlineStatusMode.Off);
    }

    public sealed record OnlineStatusPayload(
        [property: JsonPropertyName("platform_id")]
        string PlatformId,
        [property: JsonPropertyName("device_uuid")]
        string DeviceUuid,
        [property: JsonPropertyName("device_type")]
        string DeviceType)
    {
        public static OnlineStatusPayload Create(string platformId, Guid deviceId, string deviceType)
        {
            return new OnlineStatusPayload(
                platformId,
                deviceId.ToString("D").ToLowerInvariant(),
                deviceType);
        }
    }

    private sealed record OnlineReportResult(
        [property: JsonPropertyName("online_count")]
        int? OnlineCount,
        [property: JsonPropertyName("success")]
        bool? Success,
        [property: JsonPropertyName("error")]
        string? Error);

    public sealed record OnlineStatsResult(
        [property: JsonPropertyName("success")]
        bool? Success,
        [property: JsonPropertyName("online_count")]
        int? OnlineCount,
        [property: JsonPropertyName("error")]
        string? Error)
    {
        public static OnlineStatsResult Failed(string error) => new(false, null, error);
    }

    private sealed record ApiErrorResponse(
        [property: JsonPropertyName("error")]
        string? Error,
        [property: JsonPropertyName("error_description")]
        string? ErrorDescription);
}
