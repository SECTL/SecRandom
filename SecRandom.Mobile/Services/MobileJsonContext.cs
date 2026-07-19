using System.Text.Json.Serialization;

namespace SecRandom.Mobile.Services;

[JsonSerializable(typeof(DeviceUuidFile))]
[JsonSerializable(typeof(MobileOnlineStatusPayload))]
[JsonSerializable(typeof(MobileIpInfoResponse))]
internal partial class MobileJsonContext : JsonSerializerContext;

internal sealed record DeviceUuidFile(Guid Uuid);

internal sealed record MobileOnlineStatusPayload(
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
    string District);

internal sealed record MobileIpInfoResponse(
    [property: JsonPropertyName("ip")]
    string? Ip,
    [property: JsonPropertyName("region")]
    string? Region,
    [property: JsonPropertyName("district")]
    string? District);
