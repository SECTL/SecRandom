using System;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using SecRandom.Langs.Auth;

namespace SecRandom.Services.Auth;

/// <summary>Which conclusion the browser page states.</summary>
public enum AuthCallbackPageState
{
    /// <summary>A code came back. State validation and the token exchange have not run yet.</summary>
    Success,

    /// <summary>The authorization page reported <c>access_denied</c>, that is the user said no.</summary>
    Denied,

    /// <summary>The authorization service reported some other error, or nothing usable came back.</summary>
    Failed
}

/// <summary>The two sentences a callback page is made of.</summary>
public readonly record struct AuthCallbackPageText(string Title, string Lede);

/// <summary>
///     Renders the one-off browser page that answers the desktop OAuth loopback redirect.
///     <para>
///         It is deliberately one self-contained document: no script, no external request, no build-time or
///         runtime asset lookup. Light and dark follow the browser through <c>prefers-color-scheme</c>, because
///         this page is not an app window and cannot read the in-app theme. The app icon is inlined as base64 so
///         the page still renders once the loopback listener is gone, and the document stays a single response
///         under a <c>default-src 'none'</c> policy.
///     </para>
///     <para>
///         The wording says "back in the app", never "signed in": the response is written before the state is
///         validated and the code exchanged, so any claim about the session is something this page cannot know
///         yet.
///     </para>
/// </summary>
public static class LoopbackAuthCallbackPage
{
    /// <summary>
    ///     The app icon shown inside the status ring: 96×96 palette PNG with transparency, derived from
    ///     <c>Assets/AppLogo.png</c> (drop the near-white backdrop by saturation, trim the transparent margin, resize to 96×96, quantize).
    ///     It is inlined rather than linked because this page must never depend on a second request.
    /// </summary>
    private const string AppIconBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAGAAAABgCAMAAADVRocKAAABIFBMVEWiyt9eraQAE1EHYJZjuX0QnRoAWkx4v4yNy6hXlsSSzKc9loIzm4IZgWt9sNN5xqCBrNAAAAATetN5uuU3qnSGudxLmNctiNXo9+iQxOcJmAkAigAAdgCJueFOs4MApgAAVwBsqtsAlwBlvJEAJwB4stoSpBIAhwIAtgAAaACkyuVwwZgARwBArnpaodkAeQaFyZoAGwDg7uKWxdyq0eukyuWHtc+TvdWHuNdruIIUkig7kNbe7d+YxOGRwtJ6xJBIqVuTw9wpmjwANgB+xZcznEkOeGctinU5daN6qctwp85PppUAWQJGsH6hyOQegNSX0bOdxeJDi5lPkr0AcVlUl69NqWsYa6UAagSJy6qIyZxds3Tf7uJkmL4ABDIAHHJMcywoAAAAYHRSTlPjYRklWSgsgbM90k09Ol7/cgD///////8D/yUSGv//FRP/Ev8R/yUkFBX5/xX//yaWEwL/+8+Iqm5rLf8Dj694Ro02EYU8NkY7Zk5SIP+z//+sUDopTkwnJv+iVwNUGxfa0iUCAAAEiElEQVR42u2YCXPaOBSASXrs1XbXMpYsW4DttSEcgQKBpM3VNE03yTbpvb3b//8vVj6RhQ8J6HQ6w5vJZBiL79OTZOmJivKdo7IWrAVrQbng2+Xh9d7l5vyD6w6u4clfywlOOlgNA+/spZ5AEAXq44UFhzE9crCKGpgFwosJ0vhAkZVCoPgiL7ic51PDu+T5W5AyTGQFJ2pm1O4lLdSUAWA5wWs1J/C3pE1aAHalBFjNNSRt7LQAnUsI8vmq2okbnXMpQHHBnloU7zIXEo2OsAAXCpJB8jgBEhW8VotjFLXbBQLznCXYKRFcxw2fcwJVUIBLBDhvEpCgQF1UAFYkUHPeBAA2hQQ3hAX8MgJTIcFvwgJNYBmtdoi+rFjQX3CSa997FXVKBDtxQySw3WUJDksEezknAqiJbnaCI4R5wVdRQfFmVMlbRLb4gVMTSWBuCr6KCyYFgsNkLXB8TeZM7pQvIWVf5MTMLVtw6QBV0vx92cILl/C5twzKl447heOTKk5zFlBZbXrCJ8FWvxepk2y8YPk+YRX4JfuImWGkLXEBuTHBGKv0b7LJLzOIngOEoDddXwJ/ZsEUa57dh3AfQtv2NDxdoaDjQX6vDK+UWlYJ7aG+N34qLLjQICgKqL2a649fpSKbhZFfn2QKjmwEygPZXAU0jb7VjxP872p7uzsvqInQM7eHt/E3vZC/XaXxd1pwbovjA4V3O/murh9Fu56u04+fAn61+oIRTCGQj/4GZYcGZRzuqrquj25GfD+FSubuKx7aiD0gwl37QcyvfkgEECwa8JmiWDRO3Ttz/NkcLM4PzrJT04/2kTXPDwW23ORyn72eZYYGq3fqMvyDWNCRwdNheMUlvEucxtbWVsMhPZfrfyiQGSAt68Bvm4HAJNZxleP7ggtpPm/YiDNg+aNEYMvzD9IGzWzQcMiTGX94QN+PNzgQQGm+/7ay3+obzVvNpsH0f6iMdOUNgoEAyfFHoeCUMXgGxRtM/3u02eg28m/mlflrRDH//lX1zwH97z5LDF7LMIzW/RRfObiLgjuVuCDkB92s31Jc13oP4/4btP8s36UNnyIgJwiLz7OQUX88dC0rzCHoP8tvD92YLyMIE/gQUepnQ4uQh94+HBs8v+e6yj8ILCZ4EXPqj3qEEJOym02Ob7nDmC8jCH+q+VydGQgJ8Gn+IMUHMss0ugHUGUMkYPnEsoZ/IKZXlYwfBAoN7xlDi18/A0IFG7Me9wPBSyBlGMwMn1pU8THhd1uEtN1/mRE5CndTW8qgM4azweCXGd8gJmmzfDs+cKCMQWdz+Pg7wzcdQlg+nNVFGIkb6D70MDFUmfExnYbDjH9UOMVVxaTGh5YZJ0Hrx/UsvtNojJOGlc9Lle+8gfLpmbBlmr1oN1/6fpA2UD7F0yFyLCUuxZYTuKl5oOvTbPjnsmOu7IbjKq3E0KXLn44QzUBPjc6yV6g4B8o3/RXk4/WV3tGCHK66batttk1Hz+Qvdwn0c+gSi5hOLn/JWyY5vjl0ezSDbPjKrrF6Pn8lAn39U8JasBb8YMH/Xym4Fl5NNREAAAAASUVORK5CYII=";

    private const string Css = """
*,*::before,*::after{box-sizing:border-box}
:root{color-scheme:light dark;--bg:#fff;--fg:#16181d;--fg-muted:#61656e;--ok:#2c9a63;--warn:#c42b1c;--chip:rgba(0,0,0,.045);--font:"Segoe UI Variable Text","Segoe UI",-apple-system,BlinkMacSystemFont,"Microsoft YaHei UI","Microsoft YaHei","Hiragino Sans","Noto Sans JP",system-ui,sans-serif}
@media (prefers-color-scheme:dark){:root{--bg:#1a1b1f;--fg:#eceef2;--fg-muted:#9aa0aa;--ok:#4cc38a;--warn:#f1707a;--chip:rgba(255,255,255,.07)}}
html,body{height:100%}
body{margin:0;display:grid;place-items:center;padding:2.5rem 1.5rem;background:var(--bg);color:var(--fg);font-family:var(--font);font-size:16px;line-height:1.6;-webkit-font-smoothing:antialiased}
main{width:min(100%,23rem);text-align:center}
.ring{width:72px;height:72px;margin:0 auto 1.75rem;border:2px solid var(--ok);border-radius:50%;display:grid;place-items:center}
main[data-state="denied"] .ring,main[data-state="failed"] .ring{border-color:var(--warn)}
.ring img{display:block;width:38px;height:38px}
h1{margin:0 0 .5rem;font-size:1.375rem;font-weight:600;line-height:1.35;letter-spacing:-.01em;text-wrap:balance}
.lede{margin:0;font-size:.9375rem;color:var(--fg-muted);text-wrap:pretty}
.code{display:inline-block;margin-top:1.25rem;padding:.125rem .5rem;border-radius:4px;background:var(--chip);color:var(--fg-muted);font-family:ui-monospace,"Cascadia Mono",Consolas,monospace;font-size:.75rem;letter-spacing:.02em}
footer{position:fixed;inset:auto 0 1.25rem;text-align:center;font-size:.75rem;color:var(--fg-muted);opacity:.9;font-variant-numeric:tabular-nums}
footer span+span::before{content:"\00b7";margin:0 .45em}
""";

    /// <summary>The only external input this page echoes back: a short lowercase error code.</summary>
    private static readonly Regex ErrorCodePattern =
        new("^[a-z0-9_]{1,64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Renders the page with copy resolved for the current UI culture.</summary>
    public static string Render(AuthCallbackPageState state, string? errorCode, string version) =>
        Render(state, errorCode, version, ResolveText(state));

    /// <summary>Renders the page with explicit copy; the visible strings stay caller-owned.</summary>
    public static string Render(AuthCallbackPageState state, string? errorCode, string version,
        AuthCallbackPageText text)
    {
        ArgumentNullException.ThrowIfNull(version);

        // Default encodes every non-ASCII character as a numeric entity, which keeps the page correct in a
        // browser but makes the bytes unreadable and triples the size of the three languages. Allowing all
        // Unicode keeps CJK as itself while &, <, >, quotes, and apostrophes stay escaped.
        var encoder = HtmlEncoder.Create(UnicodeRanges.All);
        var stateToken = state switch
        {
            AuthCallbackPageState.Success => "success",
            AuthCallbackPageState.Denied => "denied",
            _ => "failed"
        };
        var title = encoder.Encode(text.Title);
        var lede = encoder.Encode(text.Lede);
        var versionText = encoder.Encode(version);

        // Only a real failure earns the raw code: success has nothing to explain and a cancellation is already
        // explained by the text above it. Anything that is not a short lowercase token is dropped, not echoed.
        var codeHtml = state == AuthCallbackPageState.Failed && NormalizeErrorCode(errorCode) is { } code
            ? $"<code class=\"code\">{encoder.Encode(code)}</code>"
            : string.Empty;

        var builder = new StringBuilder(8 * 1024);
        builder.Append($$"""
            <!doctype html>
            <html lang="{{ResolveLanguage()}}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="light dark">
            <meta name="referrer" content="no-referrer">
            <title>{{title}} · SecRandom</title>
            <style>{{Css}}</style>
            </head>
            <body>
            <main data-state="{{stateToken}}">
            <div class="ring"><img src="data:image/png;base64,{{AppIconBase64}}" width="38" height="38" alt=""></div>
            <h1>{{title}}</h1>
            <p class="lede">{{lede}}</p>
            {{codeHtml}}
            </main>
            <footer><span>SecRandom</span><span>{{versionText}}</span></footer>
            </body>
            </html>
            """);
        return builder.ToString();
    }

    private static AuthCallbackPageText ResolveText(AuthCallbackPageState state) => state switch
    {
        AuthCallbackPageState.Success => new(Resources.C_AuthCallback_Title_Success,
            Resources.C_AuthCallback_Lede_Success),
        AuthCallbackPageState.Denied => new(Resources.C_AuthCallback_Title_Denied,
            Resources.C_AuthCallback_Lede_Denied),
        _ => new(Resources.C_AuthCallback_Title_Failed, Resources.C_AuthCallback_Lede_Failed)
    };

    private static string? NormalizeErrorCode(string? errorCode) =>
        !string.IsNullOrEmpty(errorCode) && ErrorCodePattern.IsMatch(errorCode) ? errorCode : null;

    private static string ResolveLanguage() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "zh" => "zh-Hans",
        "ja" => "ja-JP",
        _ => "en-US"
    };
}
