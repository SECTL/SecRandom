namespace SecRandom.Services.Updates;

/// <summary>
///     Compares a signed update-manifest version against the running build. Release tags are SemVer strings such
///     as <c>v3.0.0-beta.1</c>. Desktop and mobile use the same pre-release ordering as the portable launcher.
/// </summary>
internal static class UpdateVersionComparer
{
    public static bool IsNewer(string candidate, string current) =>
        ReleaseVersion.TrySplit(candidate, out _, out _)
        && ReleaseVersion.TrySplit(current, out _, out _)
        && ReleaseVersion.Compare(candidate, current) > 0;

    public static bool TryParse(string text, out Version version)
    {
        var normalized = text.Trim().TrimStart('v', 'V');
        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
            normalized = normalized[..suffixIndex];
        return Version.TryParse(normalized, out version!);
    }
}
