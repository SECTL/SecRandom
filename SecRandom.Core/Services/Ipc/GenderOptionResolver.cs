namespace SecRandom.Core.Services.Ipc;

/// <summary>
///     Resolves the gender token an automation client sends over URL/IPC against the gender values the
///     current list actually contains. Roster values are user data, so the option list may be Chinese
///     ("男"/"女"), Latin words ("Male"/"Female"), initials ("M"/"F"), or anything else the class teacher
///     typed; the router must not assume one of them.
/// </summary>
public static class GenderOptionResolver
{
    private static readonly string[] MaleAliases = ["male", "m", "man", "boy", "男", "男性", "男生"];
    private static readonly string[] FemaleAliases = ["female", "f", "woman", "girl", "女", "女性", "女生"];

    public static string? Resolve(string? value, IReadOnlyList<string> options)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var requested = value.Trim();
        var normalized = requested.ToLowerInvariant();
        if (normalized is "all" or "any" or "*" or "全部" or "所有")
            return options.Count > 0 ? options[0] : null;

        // A literal option always wins, so a roster that stores exactly "male" keeps working, and custom
        // values keep being reachable by their own name.
        var exact = options.FirstOrDefault(option => string.Equals(option, requested, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        var aliases = MatchAliases(normalized);
        if (aliases is null)
            return null;

        return options.FirstOrDefault(option =>
            aliases.Contains(option.Trim().ToLowerInvariant(), StringComparer.Ordinal));
    }

    private static string[]? MatchAliases(string normalized) => normalized switch
    {
        _ when MaleAliases.Contains(normalized, StringComparer.Ordinal) => MaleAliases,
        _ when FemaleAliases.Contains(normalized, StringComparer.Ordinal) => FemaleAliases,
        _ => null
    };
}
