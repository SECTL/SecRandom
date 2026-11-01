namespace SecRandom.Core.Services.Profiles;

/// <summary>
///     Naming rules for a history record group. The group name <b>is</b> the identity (same trade-off as
///     <see cref="SecRandom.Shared.Models.Profile.HistoryItem.CourseName" />), so every comparison in Core and in the
///     app layer is <see cref="StringComparison.Ordinal" /> — a culture-aware comparison would let "abc" and "ABC"
///     behave differently on two machines while both read the same history file.
/// </summary>
/// <remarks>
///     <para>
///         The empty string is not a name: it is the implicit default group that every record written without a group
///         carries, and it is never listed or created. That is why <see cref="Normalize" /> returns an empty string
///         for blank input instead of a fallback name — callers must treat empty as "the default group", never as
///         "an unnamed group".
///     </para>
///     <para>
///         Reserved values: the course linkage uses <c>__break__</c> as its break-period sentinel
///         (<c>SecRandom/Services/Linkage/CourseLinkageService.cs:122</c>, mirrored by
///         <c>HistoryExportService.BreakCourseMarker</c>), and exports translate that value into a "break" label. A
///         group literally named <c>__break__</c> would therefore be indistinguishable from that marker in any export
///         or log a user reads, so names wrapped in <c>__</c> are refused outright rather than only the one literal.
///     </para>
///     <para>
///         Validation errors are stable codes, not sentences: this is Core, and every user-visible string belongs to
///         the app layer's localized resources. The app layer maps these codes to three-language text.
///     </para>
/// </remarks>
public static class HistoryGroupName
{
    /// <summary>Maximum length of a group name after trimming.</summary>
    /// <remarks>
    ///     Short on purpose: the name is rendered in a main-page chip and in an export column, and it is rewritten
    ///     into every record of the group on rename, so a long name would be a long-term tax paid per record.
    /// </remarks>
    public const int MaxLength = 32;

    /// <summary>The name was blank or whitespace only.</summary>
    public const string ErrorNameEmpty = "group_name_empty";

    /// <summary>The trimmed name is longer than <see cref="MaxLength" />.</summary>
    public const string ErrorNameTooLong = "group_name_too_long";

    /// <summary>The name contains a control character.</summary>
    public const string ErrorNameControlChars = "group_name_control_chars";

    /// <summary>The name collides with a reserved sentinel (anything wrapped in <c>__</c>).</summary>
    public const string ErrorNameReserved = "group_name_reserved";

    /// <summary>A group with that name already exists in the profile.</summary>
    public const string ErrorNameExists = "group_already_exists";

    /// <summary>The group to rename or delete does not exist.</summary>
    public const string ErrorNameNotFound = "group_not_found";

    /// <summary>The new name equals the current name.</summary>
    public const string ErrorNameUnchanged = "group_name_unchanged";

    /// <summary>No loaded history profile was available to hold the group list.</summary>
    public const string ErrorProfileUnavailable = "group_profile_unavailable";

    /// <summary>The in-memory change was applied but persisting it failed.</summary>
    public const string ErrorSaveFailed = "group_save_failed";

    /// <summary>Course-linkage break-period sentinel; kept in sync with the app layer's linkage service.</summary>
    internal const string BreakCourseMarker = "__break__";

    private const string ReservedWrapper = "__";

    /// <summary>Trims the name; returns an empty string for null or whitespace-only input.</summary>
    public static string Normalize(string? name) => name?.Trim() ?? string.Empty;

    /// <summary>Validates and normalizes a candidate group name.</summary>
    public static bool TryValidate(string? name, out string normalized) =>
        Validate(name, out normalized, out _);

    /// <summary>Validates a candidate group name and reports the first violated rule as a stable code.</summary>
    public static bool Validate(string? name, out string normalized, out string error)
    {
        normalized = Normalize(name);

        if (normalized.Length == 0)
        {
            error = ErrorNameEmpty;
            return false;
        }

        if (normalized.Length > MaxLength)
        {
            error = ErrorNameTooLong;
            return false;
        }

        foreach (var character in normalized)
        {
            if (char.IsControl(character))
            {
                error = ErrorNameControlChars;
                return false;
            }
        }

        if (IsReserved(normalized))
        {
            error = ErrorNameReserved;
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsReserved(string name) =>
        name.StartsWith(ReservedWrapper, StringComparison.Ordinal)
        || name.EndsWith(ReservedWrapper, StringComparison.Ordinal)
        || string.Equals(name, BreakCourseMarker, StringComparison.Ordinal);
}
