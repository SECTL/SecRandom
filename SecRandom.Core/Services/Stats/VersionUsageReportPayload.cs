using System.Text.Json.Serialization;

namespace SecRandom.Core.Services.Stats;

/// <summary>
///     One version-usage report as the SECTL statistics API expects it (`POST /api/stats/version`), which counts
///     *people* per version: the service dedups by identity — the signed-in account id when there is one,
///     otherwise the pseudo-anonymous device UUID — and keeps one current version per identity, so a version's
///     head count answers "how many are still on this build". Neither other statistics endpoint can:
///     `/api/fields/values` keeps only the last reporter's value and `/api/stats/usage/increment` counts reports,
///     not people.
///     Exactly one identity field travels, because sending both would let the service see two identities for one
///     installation and inflate the very figure this endpoint exists to measure. The names are the API's
///     (`platform_id`, `version`, `user_id`, `device_uuid`), not the app's, and the identity that does not apply
///     is omitted instead of sent as null.
/// </summary>
public sealed record VersionUsageReportPayload(
    [property: JsonPropertyName("platform_id")]
    string PlatformId,
    [property: JsonPropertyName("version")]
    string Version,
    [property: JsonPropertyName("user_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? UserId,
    [property: JsonPropertyName("device_uuid"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DeviceUuid)
{
    private const int MaxVersionLength = 64;
    private const string VersionSeparators = "._+-() ";

    /// <summary>
    ///     The single identity the service dedups this report by. <see cref="Create" /> guarantees exactly one
    ///     of the two identity fields is present, so this is never null there.
    /// </summary>
    [JsonIgnore]
    public string? Identity => UserId ?? DeviceUuid;

    /// <summary>
    ///     Builds the report for one installation state. A signed-in account id wins over the device UUID, and the
    ///     device UUID is then left out entirely: the API treats `user_id` and `device_uuid` as two different
    ///     identities, so sending both would count one installation twice.
    /// </summary>
    /// <exception cref="ArgumentException">No usable identity, or a version value the API would reject.</exception>
    public static VersionUsageReportPayload Create(
        string platformId,
        string version,
        string? userId,
        string? deviceUuid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformId);
        if (!IsSupportedVersion(version))
            throw new ArgumentException($@"Unsupported version value: '{version}'.", nameof(version));

        var account = Normalize(userId);
        // 与在线状态上报保持同一种小写形态，同一台设备不会产生两个设备身份字符串
        var device = Normalize(deviceUuid)?.ToLowerInvariant();
        if (account is null && device is null)
            throw new ArgumentException("A version report needs a user id or a device UUID.", nameof(userId));

        return new VersionUsageReportPayload(platformId, version, account, account is null ? device : null);
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
