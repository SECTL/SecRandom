namespace SecRandom.Services.Updates;

/// <summary>
///     Compares a signed update-manifest version against the running build. Release tags are SemVer strings such
///     as <c>v3.0.0-beta.1</c>, which <see cref="Version" /> cannot parse, so the numeric core is compared after
///     dropping the optional v prefix and any pre-release/build suffix. That keeps the current pre-release train
///     able to see newer releases and keeps desktop and mobile update checks on one rule.
/// </summary>
internal static class UpdateVersionComparer
{
    public static bool IsNewer(string candidate, string current) =>
        TryParse(candidate, out var candidateVersion)
        && TryParse(current, out var currentVersion)
        && candidateVersion > currentVersion;

    public static bool TryParse(string text, out Version version)
    {
        var normalized = text.Trim().TrimStart('v', 'V');
        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
            normalized = normalized[..suffixIndex];
        return Version.TryParse(normalized, out version!);
    }
}
