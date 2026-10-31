namespace SecRandom.Services.Auth;

/// <summary>
///     One OAuth authorization attempt: what the authorization endpoint has to be told, and the PKCE
///     verifier needed to exchange the code that comes back. <paramref name="Nonce" /> is the OIDC
///     replay guard; it is only sent when the attempt requests the <c>openid</c> scope.
/// </summary>
public sealed record AuthRedirectAttempt(string RedirectUri, string State, string CodeVerifier,
    string? Nonce = null);

/// <summary>
///     The redirect the authorization endpoint handed back, together with everything the token
///     exchange needs. <paramref name="Error" /> carries a service-provided failure description.
/// </summary>
public sealed record AuthRedirectResult(
    string RedirectUri,
    string State,
    string CodeVerifier,
    string? Code,
    string? Error,
    string? Nonce = null);

/// <summary>
///     Obtains the authorization redirect for one sign-in attempt, so the flow around it (PKCE, state
///     validation, token exchange, refresh policy) stays platform-neutral: the desktop broker listens
///     on a loopback HTTP port, while a mobile broker hands the redirect back through the app's custom
///     URL scheme.
/// </summary>
public interface IAuthRedirectBroker : IDisposable
{
    /// <summary>
    ///     How long this broker can wait for the user to finish authorizing. The loopback listener only
    ///     has to survive one browser round trip, while a phone user may switch apps and sign in.
    /// </summary>
    TimeSpan WaitTimeout { get; }

    /// <summary>Redirect URI to hand to the authorization endpoint for this attempt.</summary>
    string CreateRedirectUri();

    /// <summary>
    ///     Registers the attempt before the browser opens: the loopback broker starts listening, and a
    ///     mobile broker durably records the attempt so a redirect that arrives after the process was
    ///     reclaimed can still be exchanged.
    /// </summary>
    Task PrepareAsync(AuthRedirectAttempt attempt, CancellationToken cancellationToken = default);

    /// <summary>Waits for the redirect that belongs to <paramref name="attempt" />.</summary>
    Task<AuthRedirectResult> WaitForRedirectAsync(AuthRedirectAttempt attempt,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns a redirect that arrived in an earlier process lifetime, or <see langword="null" />
    ///     when nothing usable is waiting. A broker without durable attempts always returns null.
    /// </summary>
    Task<AuthRedirectResult?> TakePendingRedirectAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates a broker per sign-in attempt, because a broker owns one attempt's state.</summary>
public interface IAuthRedirectBrokerFactory
{
    /// <summary>
    ///     Creates one attempt's broker. <paramref name="preferOidcRedirectPort" /> asks the desktop
    ///     broker to come back to the pinned loopback port an OIDC redirect URI has to be registered on;
    ///     a broker that cannot offer such a URI ignores it.
    /// </summary>
    IAuthRedirectBroker Create(bool preferOidcRedirectPort = false);
}
