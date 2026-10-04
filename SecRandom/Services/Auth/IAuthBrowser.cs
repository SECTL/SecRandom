namespace SecRandom.Services.Auth;

/// <summary>
///     Opens the SECTL authorization page in the platform's sign-in browser.
///     The default implementation hands the URL to the external launcher: the desktop flow then waits
///     for the loopback redirect, and a mobile head waits for the deep link the app's custom scheme
///     delivers. iOS replaces it with ASWebAuthenticationSession, which captures the redirect inside
///     the browser it presents, so its sign-in never depends on the app still being alive when the
///     user finishes — a suspended iOS app cannot receive a custom-scheme URL through its delegate.
/// </summary>
public interface IAuthBrowser
{
    /// <summary>Opens the authorization page. Returns false when no browser could be opened.</summary>
    bool TryOpenAuthorization(string authorizeUrl);
}

/// <summary>Default authorization browser: the platform's own external launcher.</summary>
public sealed class ExternalLauncherAuthBrowser(Desktop.IExternalLauncher launcher) : IAuthBrowser
{
    public bool TryOpenAuthorization(string authorizeUrl) => launcher.TryOpenUri(authorizeUrl);
}
