namespace SecRandom.Services.Updates;

internal static class ReleaseVersion
{
    public static int Compare(string left, string right)
    {
        if (!TrySplit(left, out var leftCore, out var leftPre) || !TrySplit(right, out var rightCore, out var rightPre))
            throw new FormatException("Invalid release version.");
        var core = leftCore.CompareTo(rightCore);
        if (core != 0) return core;
        if (leftPre is null) return rightPre is null ? 0 : 1;
        if (rightPre is null) return -1;
        var a = leftPre.Split('.');
        var b = rightPre.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var an = long.TryParse(a[i], out var av);
            var bn = long.TryParse(b[i], out var bv);
            var comparison = an && bn ? av.CompareTo(bv) : an ? -1 : bn ? 1 : string.CompareOrdinal(a[i], b[i]);
            if (comparison != 0) return comparison;
        }
        return a.Length.CompareTo(b.Length);
    }

    public static bool TrySplit(string text, out Version core, out string? prerelease)
    {
        core = new Version();
        prerelease = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = text.Trim().TrimStart('v', 'V');
        var build = value.IndexOf('+');
        if (build >= 0) value = value[..build];
        var dash = value.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = value[(dash + 1)..];
            value = value[..dash];
            if (prerelease.Split('.').Any(part => part.Length == 0 || part.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))) return false;
        }
        if (!Version.TryParse(value, out var parsed)) return false;
        core = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
        return true;
    }
}
