#if IOS
using AuthenticationServices;
using Foundation;
using SecRandom.Mobile;
using SecRandom.Services.Auth;
using System.Runtime.Versioning;
using UIKit;

namespace SecRandom.Mobile.iOS;

/// <summary>
///     iOS authorization browser. ASWebAuthenticationSession presents the SECTL page in a browser the
///     system controls and hands the custom-scheme redirect straight back to this callback, so sign-in
///     never depends on the app still being alive when the user finishes. Opening the same URL with
///     <c>UIApplication.OpenUrl</c> would not do: iOS suspends the app while Safari is in front, and an
///     <c>AvaloniaAppDelegate</c> cannot receive the redirect either, because its <c>OpenUrl</c> member
///     is not virtual.
/// </summary>
[SupportedOSPlatform("ios13.0")]
internal sealed class IosAuthBrowser : IAuthBrowser
{
    private ASWebAuthenticationSession? _session;

    public bool TryOpenAuthorization(string authorizeUrl)
    {
        var url = NSUrl.FromString(authorizeUrl);
        if (url is null)
            return false;

        CancelActiveSession();
        // The callback-scheme constructor is deprecated on iOS 17.4 in favour of
        // ASWebAuthenticationSessionCallback, which only exists from that version on; this head still
        // supports iOS 13, and the deprecated initializer keeps working.
#pragma warning disable CA1422
        var session = new ASWebAuthenticationSession(url, MobileAuthCallbackRouter.CallbackScheme,
            (callbackUrl, error) =>
            {
                // The shared flow is waiting on the router, so this is the only hand-off it needs.
                if (callbackUrl is not null)
                {
                    MobileAuthCallbackRouter.DeliverFromPlatform(callbackUrl.AbsoluteString);
                    return;
                }

                // A cancelled session must end the wait immediately instead of letting it run into its
                // timeout, so the user sees why nothing happened.
                MobileAuthCallbackRouter.DeliverFromPlatform(
                    $"{MobileAuthCallbackRouter.RedirectUri}?error_description=" +
                    Uri.EscapeDataString(error?.LocalizedDescription ?? "登录已取消"));
            })
        {
            // Keep the user's existing sectl.cn session instead of a private one, so a second sign-in
            // on the same device does not ask for credentials again.
            PrefersEphemeralWebBrowserSession = false,
            PresentationContextProvider = new AuthPresentationContextProvider()
        };
#pragma warning restore CA1422

        _session = session;
        return session.Start();
    }

    private void CancelActiveSession()
    {
        try
        {
            _session?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _session = null;
    }
}

/// <summary>ASWebAuthenticationSession can only be presented from a window; it asks for one here.</summary>
[SupportedOSPlatform("ios13.0")]
internal sealed class AuthPresentationContextProvider : NSObject, IASWebAuthenticationPresentationContextProviding
{
    public UIWindow GetPresentationAnchor(ASWebAuthenticationSession session)
    {
        var windows = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows);

        return windows.FirstOrDefault(window => window.IsKeyWindow)
               ?? windows.FirstOrDefault()
               // A bare window is a last resort for the moment before a scene window exists; the
               // iOS 26 replacement only applies to scene-hosted windows.
#pragma warning disable CA1422
               ?? new UIWindow();
#pragma warning restore CA1422
    }
}
#endif
