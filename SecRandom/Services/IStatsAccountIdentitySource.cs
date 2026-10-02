namespace SecRandom.Services;

/// <summary>
///     Supplies the SECTL account a statistics report can be attributed to. The version-usage counter counts
///     people, so an account id is the closest thing to one; hosts without an account session keep it empty and
///     their reports fall back to the device identity.
/// </summary>
public interface IStatsAccountIdentitySource
{
    /// <summary>Signed-in SECTL account id, or <see langword="null" /> while no account is active.</summary>
    string? UserId { get; }

    /// <summary>
    ///     Raised when <see cref="UserId" /> starts, stops, or changes — the moment the previous report stops
    ///     describing this installation, so the identity it credited has to be corrected.
    /// </summary>
    event EventHandler? UserIdChanged;
}

/// <summary>
///     Identity source for hosts that have no account session at all (mobile). A missing account is a normal
///     state rather than a failure: reports simply use the device UUID.
/// </summary>
public sealed class NullStatsAccountIdentitySource : IStatsAccountIdentitySource
{
    public string? UserId => null;

    public event EventHandler? UserIdChanged
    {
        add { }
        remove { }
    }
}
