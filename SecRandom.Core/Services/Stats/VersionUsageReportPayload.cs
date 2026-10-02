using System.Text.Json.Serialization;

namespace SecRandom.Core.Services.Stats;

/// <summary>
///     One version-usage report as the SECTL statistics API expects it (`POST /api/stats/version`), which counts
///     *people* per version: the service dedups by identity — here always the pseudo-anonymous device UUID — and
///     keeps one current version per identity, so a version's head count answers "how many are still on this
///     build". Neither other statistics endpoint can: `/api/fields/values` keeps only the last reporter's value and
///     `/api/stats/usage/increment` counts reports, not people.
///     The account identity the API also accepts (`user_id`) is deliberately never sent: a version figure must not
///     be tied to a SECTL account, so the device UUID is the only identity that travels and the only one the
///     service can dedup this installation by. The names are the API's (`platform_id`, `version`, `device_uuid`),
///     not the app's.
/// </summary>
public sealed record VersionUsageReportPayload(
    [property: JsonPropertyName("platform_id")]
    string PlatformId,
    [property: JsonPropertyName("version")]
    string Version,
    [property: JsonPropertyName("device_uuid")]
    string DeviceUuid)
{
    private const int MaxVersionLength = 64;
    private const string VersionSeparators = "._+-() ";

    /// <summary>
    ///     Builds the report for one installation. The device UUID is required: without it the service cannot
    ///     dedup the report into a person, and this client never falls back to an account id.
    /// </summary>
    /// <exception cref="ArgumentException">A version value or a device UUID the API would reject.</exception>
    public static VersionUsageReportPayload Create(string platformId, string version, string? deviceUuid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformId);
        if (!IsSupportedVersion(version))
            throw new ArgumentException($@"Unsupported version value: '{version}'.", nameof(version));

        var device = Normalize(deviceUuid);
        // 设备标识必须是标准 UUID，且与在线状态上报同为小写形态，同一台设备只对应一个身份字符串
        if (device is null || !Guid.TryParse(device, out _))
            throw new ArgumentException($@"Unsupported device UUID: '{deviceUuid}'.", nameof(deviceUuid));

        return new VersionUsageReportPayload(platformId, version, device.ToLowerInvariant());
    }

    /// <summary>
    ///     Mirrors the API's version rule — 1–64 characters, alphanumeric first character, then letters, digits,
    ///     `.`, `_`, `+`, `-`, parentheses and spaces — so a build whose version tag would be rejected is skipped
    ///     locally instead of coming back as `invalid_request` on every launch.
    /// </summary>
    public static bool IsSupportedVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > MaxVersionLength)
            return false;

        if (!char.IsAsciiLetterOrDigit(version[0]))
            return false;

        return version.All(character =>
            char.IsAsciiLetterOrDigit(character) || VersionSeparators.Contains(character, StringComparison.Ordinal));
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
