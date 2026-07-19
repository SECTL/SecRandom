using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services;
using SecRandom.Services.Config;

namespace SecRandom.Services;

public sealed partial class OnlineStatusService : BackgroundService
{
    // 在线状态上报是 SECTL 自有服务，和 Sentry SDK 遥测通道相互独立。
    // 隐私判断必须留在本服务内显式执行，避免未来接入 SDK 时误开此上报。
    private const string ApiBaseUrl = "https://appwrite.sectl.cn";
    private const string PlatformId = "69c8cd6a0012dd3ea10a";
    private const string AnonymousIpAddress = "0.0.0.0";
    private const string UnknownRegion = "未知";
    private const string LocalRegion = "本地";
    private const string ErrorDisabled = "disabled";
    private const string ErrorRequestFailed = "request_failed";

    private static readonly TimeSpan ReportInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    // Full 模式可能上报粗略地区，但公网 IP/地区查询依赖外部服务且较慢。
    // 短期缓存可避免重复扇出请求，同时仍能在网络变化后自然刷新。
    private static readonly TimeSpan IpLocationCacheTtl = TimeSpan.FromHours(1);
    private static readonly Uri OnlineReportUri = new($"{ApiBaseUrl}/api/stats/online");
    private static readonly Uri OnlineStatsUri = new($"{ApiBaseUrl}/api/stats/platform/{PlatformId}");
    private static readonly Uri[] PublicIpServiceUris =
    [
        new("https://321260.xyz/api/ip.php"),
        new("https://ddns.oray.com/checkip"),
        new("http://v4.66666.host:66/ip"),
        new("https://myip.ipip.net"),
        new("https://api64.ipify.org")
    ];

    private readonly DeviceUuidStore _deviceUuidStore;
    private readonly MainConfigHandler _configHandler;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OnlineStatusService> _logger;
    private PrivacySettingsConfig _privacySettings;
    private readonly object _cacheLock = new();
    // 统计读取和周期上报可能并发触发；同一时间只允许一个调用刷新公网 IP/地区。
    private readonly SemaphoreSlim _locationRefreshLock = new(1, 1);

    private IpLocationCache? _ipLocationCache;
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

        _privacySettings.PropertyChanged += PrivacySettingsOnPropertyChanged;
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
                    await ReportOnceAsync(policy, stoppingToken).ConfigureAwait(false);
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
        _privacySettings.PropertyChanged -= PrivacySettingsOnPropertyChanged;
        _locationRefreshLock.Dispose();
        _httpClient.Dispose();
        base.Dispose();
    }

    public void Refresh()
    {
        var privacySettings = _configHandler.Data.General.PrivacySettings;
        if (ReferenceEquals(_privacySettings, privacySettings))
            return;

        _privacySettings.PropertyChanged -= PrivacySettingsOnPropertyChanged;
        _privacySettings = privacySettings;
        _privacySettings.PropertyChanged += PrivacySettingsOnPropertyChanged;
        ClearIpLocationCache();
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

    private async Task ReportOnceAsync(OnlineStatusPolicy policy, CancellationToken cancellationToken)
    {
        Guid deviceId = EnsureDeviceUuid();
        IpLocationCache locationCache = policy.IncludeIpLocation
            ? await GetFullIpLocationCacheAsync(cancellationToken).ConfigureAwait(false)
            : IpLocationCache.Anonymous;

        // IP/地区查询过程中隐私设置可能变化；构造 payload 前必须重新读取策略。
        policy = ResolvePolicy();
        if (!policy.IsEnabled)
            return;

        if (!policy.IncludeIpLocation)
            locationCache = IpLocationCache.Anonymous;

        OnlineStatusPayload payload = CreatePayload(deviceId, policy, locationCache);

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

            LogReportSuccess(policy, result?.OnlineCount);
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

    private static OnlineStatusPayload CreatePayload(Guid deviceId, OnlineStatusPolicy policy, IpLocationCache locationCache)
    {
        return OnlineStatusPayload.Create(
            PlatformId,
            deviceId,
            DetectDeviceType(),
            policy.IncludeIpLocation ? locationCache : IpLocationCache.Anonymous);
    }

    private async Task<IpLocationCache> GetFullIpLocationCacheAsync(CancellationToken cancellationToken)
    {
        IpLocationCache? cached = GetFreshIpLocationCache();
        if (cached is not null)
            return cached;

        await _locationRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = GetFreshIpLocationCache();
            if (cached is not null)
                return cached;

            IpLocationCache refreshed = await BuildFullIpLocationCacheAsync(cancellationToken).ConfigureAwait(false);
            return CacheFullIpLocationIfAllowed(refreshed);
        }
        finally
        {
            _locationRefreshLock.Release();
        }
    }

    private IpLocationCache CacheFullIpLocationIfAllowed(IpLocationCache refreshed)
    {
        lock (_cacheLock)
        {
            // The user may switch away from Full mode while the IP/location lookup is in flight.
            // Re-read the policy before persisting IP-derived data so a stale lookup cannot undo
            // the cache clear performed by the privacy-change handler.
            if (!ResolvePolicy().IncludeIpLocation)
                return IpLocationCache.Anonymous;

            _ipLocationCache = refreshed;
            return refreshed;
        }
    }

    private IpLocationCache? GetFreshIpLocationCache()
    {
        lock (_cacheLock)
        {
            IpLocationCache? cache = _ipLocationCache;
            return cache is not null && cache.IsFresh(DateTimeOffset.UtcNow, IpLocationCacheTtl)
                ? cache
                : null;
        }
    }

    private async Task<IpLocationCache> BuildFullIpLocationCacheAsync(CancellationToken cancellationToken)
    {
        string? ipAddress = await GetPublicIpAsync(cancellationToken).ConfigureAwait(false);
        if (ipAddress is null || IsLocalAddress(ipAddress))
        {
            _logger.LogWarning("Public IP detection failed or returned a local address; online status will be reported without location.");
            return IpLocationCache.Anonymous;
        }

        IpLocation location = await GetIpLocationAsync(ipAddress, cancellationToken).ConfigureAwait(false);

        return new IpLocationCache(
            ipAddress,
            location.Country,
            location.Province,
            location.City,
            location.District,
            DateTimeOffset.UtcNow);
    }

    private async Task<string?> GetPublicIpAsync(CancellationToken cancellationToken)
    {
        foreach (Uri uri in PublicIpServiceUris)
        {
            try
            {
                string text = await _httpClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
                if (TryParseIpAddress(text, out var ipAddress))
                    return ipAddress.ToString();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to query public IP from {Uri}.", uri);
            }
        }

        _logger.LogWarning("All public IP services failed.");
        return null;
    }

    private async Task<IpLocation> GetIpLocationAsync(string ipAddress, CancellationToken cancellationToken)
    {
        if (IsLocalAddress(ipAddress))
            return IpLocation.Local;

        try
        {
            Uri uri = new($"https://uapis.cn/api/v1/network/ipinfo?ip={Uri.EscapeDataString(ipAddress)}&source=commercial");
            using HttpResponseMessage response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return IpLocation.Unknown;

            IpInfoResponse? data = await response.Content
                .ReadFromJsonAsync<IpInfoResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(data?.Ip))
                return IpLocation.Unknown;

            string[] regionParts = (data.Region ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return new IpLocation(
                GetRegionPart(regionParts, 0),
                GetRegionPart(regionParts, 1),
                GetRegionPart(regionParts, 2),
                string.IsNullOrWhiteSpace(data.District) ? GetRegionPart(regionParts, 3) : data.District);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query IP location for {IpAddress}.", ipAddress);
            return IpLocation.Unknown;
        }
    }

    private Guid EnsureDeviceUuid()
    {
        return _deviceUuidStore.GetOrCreate();
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

    private void LogReportSuccess(OnlineStatusPolicy policy, int? onlineCount)
    {
        if (policy.IncludeIpLocation)
        {
            _logger.LogInformation(
                "Online status reported. Current online count: {OnlineCount}",
                onlineCount ?? 0);
            return;
        }

        _logger.LogInformation("Online status reported anonymously.");
    }

    private void UpdateOnlineCountCache(int count)
    {
        Volatile.Write(ref _cachedOnlineCount, count);
        Volatile.Write(
            ref _cachedOnlineCountUpdatedAtUnixMilliseconds,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private void PrivacySettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(PrivacySettingsConfig.OnlineStatusMode))
            return;

        // 从 Full 降到 Anonymous/Off 时，必须立刻丢弃已缓存的 IP 派生地区。
        ClearIpLocationCache();
    }

    private void ClearIpLocationCache()
    {
        lock (_cacheLock)
        {
            _ipLocationCache = null;
        }
    }

    private static bool IsLocalAddress(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return true;

        if (ipAddress.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            || ipAddress.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(ipAddress, out var address))
            return true;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal || (bytes[0] & 0xfe) == 0xfc;
        }

        var octets = address.GetAddressBytes();
        return octets[0] == 10
               || octets[0] == 127
               || (octets[0] == 172 && octets[1] is >= 16 and <= 31)
               || (octets[0] == 192 && octets[1] == 168);
    }

    private static bool TryParseIpAddress(string text, out IPAddress ipAddress)
    {
        if (IPAddress.TryParse(text.Trim(), out ipAddress!))
            return true;

        foreach (var token in text.Split([' ', '\t', '\r', '\n', ',', ';', '[', ']'], StringSplitOptions.RemoveEmptyEntries))
            if (IPAddress.TryParse(token, out ipAddress!))
                return true;

        ipAddress = IPAddress.None;
        return false;
    }

    private static string DetectDeviceType()
    {
        return OnlineStatusDeviceType.Detect();
    }

    private static string GetRegionPart(IReadOnlyList<string> parts, int index)
    {
        return index < parts.Count ? parts[index] : UnknownRegion;
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);


    public sealed record OnlineStatusPolicy(
        OnlineStatusMode Mode,
        bool IsEnabled,
        bool IncludeIpLocation)
    {
        // Anonymous 仍允许参与在线计数，但绝不发送 IP 派生地区字段。
        public static OnlineStatusPolicy From(OnlineStatusMode mode)
        {
            return new OnlineStatusPolicy(
                mode,
                mode != OnlineStatusMode.Off,
                mode == OnlineStatusMode.Full);
        }
    }

    public sealed record OnlineStatusPayload(
        [property: JsonPropertyName("platform_id")]
        string PlatformId,
        [property: JsonPropertyName("device_uuid")]
        string DeviceUuid,
        [property: JsonPropertyName("device_type")]
        string DeviceType,
        [property: JsonPropertyName("ip_address")]
        string IpAddress,
        [property: JsonPropertyName("country")]
        string Country,
        [property: JsonPropertyName("province")]
        string Province,
        [property: JsonPropertyName("city")]
        string City,
        [property: JsonPropertyName("district")]
        string District)
    {
        public static OnlineStatusPayload Create(
            string platformId,
            Guid deviceId,
            string deviceType,
            IpLocationCache locationCache)
        {
            return new OnlineStatusPayload(
                platformId,
                deviceId.ToString("D").ToLowerInvariant(),
                deviceType,
                locationCache.IpAddress,
                locationCache.Country,
                locationCache.Province,
                locationCache.City,
                locationCache.District);
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

    private sealed record IpInfoResponse(
        [property: JsonPropertyName("ip")]
        string? Ip,
        [property: JsonPropertyName("region")]
        string? Region,
        [property: JsonPropertyName("district")]
        string? District,
        [property: JsonPropertyName("msg")]
        string? Message);

    private sealed record IpLocation(string Country, string Province, string City, string District)
    {
        public static IpLocation Local { get; } = new(LocalRegion, LocalRegion, LocalRegion, LocalRegion);
        public static IpLocation Unknown { get; } = new(UnknownRegion, UnknownRegion, UnknownRegion, UnknownRegion);
    }

    public sealed record IpLocationCache(
        string IpAddress,
        string Country,
        string Province,
        string City,
        string District,
        DateTimeOffset UpdatedAt)
    {
        public static IpLocationCache Anonymous =>
            new(AnonymousIpAddress, UnknownRegion, UnknownRegion, UnknownRegion, UnknownRegion, DateTimeOffset.MinValue);

        public bool IsFresh(DateTimeOffset now, TimeSpan ttl)
        {
            return now - UpdatedAt <= ttl;
        }
    }
}
