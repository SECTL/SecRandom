namespace SecRandom.Services.Auth;

/// <summary>
///     Bridges the desktop OAuth session into the shared statistics identity contract: the account id a
///     version report is attributed to, plus a notification when that identity really changes.
///     <see cref="SectlAuthService.StateChanged" /> also fires for background profile refreshes, so only a
///     changed id is forwarded. The persisted token id is preferred over the freshly fetched profile id: the
///     profile arrives later over the network, and an id that flapped between the two sources would report one
///     person as two identities and inflate the version head count.
/// </summary>
public sealed class SectlStatsAccountIdentitySource : IStatsAccountIdentitySource, IDisposable
{
    private readonly SectlAuthService _authService;
    private string? _lastUserId;

    public SectlStatsAccountIdentitySource(SectlAuthService authService)
    {
        _authService = authService;
        _lastUserId = ResolveUserId();
        _authService.StateChanged += AuthServiceOnStateChanged;
    }

    public string? UserId => ResolveUserId();

    public event EventHandler? UserIdChanged;

    public void Dispose() => _authService.StateChanged -= AuthServiceOnStateChanged;

    private string? ResolveUserId() =>
        FirstNonBlank(_authService.Token?.UserId, _authService.User?.ResolvedUserId);

    private void AuthServiceOnStateChanged(object? sender, EventArgs e)
    {
        var userId = ResolveUserId();
        if (string.Equals(userId, _lastUserId, StringComparison.Ordinal))
            return;

        _lastUserId = userId;
        UserIdChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
