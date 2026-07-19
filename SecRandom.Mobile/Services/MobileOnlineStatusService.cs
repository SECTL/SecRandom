using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
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
    private static readonly TimeSpan IpLocationCacheTtl = TimeSpan.FromHours(1);
    private static readonly Uri[] PublicIpServiceUris =
    [
        new("https://321260.xyz/api/ip.php"),
        new("https://ddns.oray.com/checkip"),
        new("http://v4.66666.host:66/ip"),
        new("https://myip.ipip.net"),
        new("https://api64.ipify.org")
    ];
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly object _locationCacheLock = new();
    private readonly SemaphoreSlim _locationRefreshLock = new(1, 1);
    private MobileIpLocationCache? _locationCache;

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
        {
            lock (_locationCacheLock)
                _locationCache = null;

            logger.LogDebug("Mobile online status policy changed.");
        }
    }

    private async Task ReportAsync(OnlineStatusMode mode, CancellationToken cancellationToken)
    {
        MobileIpLocationCache location = mode == OnlineStatusMode.Full
            ? await GetFullIpLocationAsync(cancellationToken).ConfigureAwait(false)
            : MobileIpLocationCache.Anonymous;

        mode = configHandler.Data.General.PrivacySettings.OnlineStatusMode;
        if (mode == OnlineStatusMode.Off)
            return;

        if (mode != OnlineStatusMode.Full)
            location = MobileIpLocationCache.Anonymous;

        var payload = new MobileOnlineStatusPayload(
            "69c8cd6a0012dd3ea10a",
            mode == OnlineStatusMode.Anonymous ? Guid.Empty.ToString() : deviceUuidStore.GetOrCreate().ToString(),
            OnlineStatusDeviceType.Detect(),
            location.IpAddress,
            location.Country,
            location.Province,
            location.City,
            location.District);

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                ReportUri,
                payload,
                MobileJsonContext.Default.MobileOnlineStatusPayload,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogDebug(exception, "Mobile online status report failed.");
        }
    }

    private async Task<MobileIpLocationCache> GetFullIpLocationAsync(CancellationToken cancellationToken)
    {
        lock (_locationCacheLock)
            if (_locationCache is { } cached && DateTimeOffset.UtcNow - cached.UpdatedAt <= IpLocationCacheTtl)
                return cached;

        await _locationRefreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_locationCacheLock)
                if (_locationCache is { } cached && DateTimeOffset.UtcNow - cached.UpdatedAt <= IpLocationCacheTtl)
                    return cached;

            MobileIpLocationCache refreshed = await BuildFullIpLocationAsync(cancellationToken).ConfigureAwait(false);
            lock (_locationCacheLock)
            {
                if (configHandler.Data.General.PrivacySettings.OnlineStatusMode != OnlineStatusMode.Full)
                    return MobileIpLocationCache.Anonymous;

                _locationCache = refreshed;
                return refreshed;
            }
        }
        finally
        {
            _locationRefreshLock.Release();
        }
    }

    private async Task<MobileIpLocationCache> BuildFullIpLocationAsync(CancellationToken cancellationToken)
    {
        string? ipAddress = await GetPublicIpAsync(cancellationToken).ConfigureAwait(false);
        if (ipAddress is null || IsLocalAddress(ipAddress))
        {
            logger.LogDebug("Mobile public IP detection failed or returned a local address.");
            return MobileIpLocationCache.Anonymous;
        }

        MobileIpInfoResponse? response;
        try
        {
            var uri = new Uri($"https://uapis.cn/api/v1/network/ipinfo?ip={Uri.EscapeDataString(ipAddress)}&source=commercial");
            response = await _httpClient.GetFromJsonAsync(uri, MobileJsonContext.Default.MobileIpInfoResponse, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogDebug(exception, "Mobile IP location lookup failed.");
            return MobileIpLocationCache.ForIp(ipAddress);
        }

        if (string.IsNullOrWhiteSpace(response?.Ip))
            return MobileIpLocationCache.ForIp(ipAddress);

        string[] regions = (response.Region ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new MobileIpLocationCache(
            ipAddress,
            RegionPart(regions, 0),
            RegionPart(regions, 1),
            RegionPart(regions, 2),
            string.IsNullOrWhiteSpace(response.District) ? RegionPart(regions, 3) : response.District,
            DateTimeOffset.UtcNow);
    }

    private async Task<string?> GetPublicIpAsync(CancellationToken cancellationToken)
    {
        foreach (Uri uri in PublicIpServiceUris)
        {
            try
            {
                string response = await _httpClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
                if (TryParseIpAddress(response, out IPAddress ipAddress))
                    return ipAddress.ToString();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Mobile public IP lookup failed for {Uri}.", uri);
            }
        }

        return null;
    }

    private static bool IsLocalAddress(string ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out IPAddress? address))
            return true;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal || (bytes[0] & 0xfe) == 0xfc;
        }

        byte[] octets = address.GetAddressBytes();
        return octets[0] == 10
               || octets[0] == 127
               || (octets[0] == 172 && octets[1] is >= 16 and <= 31)
               || (octets[0] == 192 && octets[1] == 168);
    }

    private static bool TryParseIpAddress(string text, out IPAddress ipAddress)
    {
        if (IPAddress.TryParse(text.Trim(), out ipAddress!))
            return true;

        foreach (string token in text.Split([' ', '\t', '\r', '\n', ',', ';', '[', ']'], StringSplitOptions.RemoveEmptyEntries))
            if (IPAddress.TryParse(token, out ipAddress!))
                return true;

        ipAddress = IPAddress.None;
        return false;
    }

    private static string RegionPart(IReadOnlyList<string> regions, int index) => index < regions.Count ? regions[index] : "未知";

    private sealed record MobileIpLocationCache(
        string IpAddress,
        string Country,
        string Province,
        string City,
        string District,
        DateTimeOffset UpdatedAt)
    {
        public static MobileIpLocationCache Anonymous { get; } = new("0.0.0.0", "未知", "未知", "未知", "未知", DateTimeOffset.MinValue);

        public static MobileIpLocationCache ForIp(string ipAddress) => new(ipAddress, "未知", "未知", "未知", "未知", DateTimeOffset.UtcNow);
    }
}
