namespace SecRandom.Core.Services.Profiles;

/// <summary>
///     Well-known roster selection values shared by the main draw pages, their draw settings, and the
///     profile defaults. The visible label of <see cref="None" /> is always resolved from the page's own
///     localization resources; only this stable token is ever persisted.
/// </summary>
public static class RosterSelection
{
    /// <summary>
    ///     Stored in <c>RollCallSettings.DefaultClass</c> / <c>LotterySettings.DefaultPool</c> (and the quick
    ///     draw default) when the user picked the "no roster" mode. An empty or missing value keeps its old
    ///     meaning of "not configured yet".
    /// </summary>
    public const string None = "__no_list__";

    /// <summary>Largest inclusive number-range span the number draw accepts, mirroring the settings UI.</summary>
    public const int MaxNumberRangeSpan = 200;

    public static bool IsNone(string? value) => string.Equals(value, None, StringComparison.Ordinal);

    /// <summary>True for a real roster name: non-blank and not the no-roster sentinel.</summary>
    public static bool IsUsable(string? value) => !string.IsNullOrWhiteSpace(value) && !IsNone(value);

    public static bool IsValidRange(int rangeStart, int rangeEnd) =>
        rangeStart >= 0 && rangeEnd >= rangeStart && rangeEnd - rangeStart + 1 <= MaxNumberRangeSpan;

    /// <summary>Clamps a user-entered end value so the inclusive span never exceeds the supported limit.</summary>
    public static int ClampEnd(int rangeStart, int rangeEnd)
    {
        if (rangeStart < 0)
            rangeStart = 0;
        if (rangeEnd < rangeStart)
            return rangeStart;
        var maxEnd = checked(rangeStart + MaxNumberRangeSpan - 1);
        return Math.Min(rangeEnd, maxEnd);
    }
}
