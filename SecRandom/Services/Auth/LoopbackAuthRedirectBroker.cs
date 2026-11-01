using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core;

namespace SecRandom.Services.Auth;

/// <summary>Creates the desktop loopback broker; it has no dependencies of its own.</summary>
public sealed class LoopbackAuthRedirectBrokerFactory : IAuthRedirectBrokerFactory
{
    public IAuthRedirectBroker Create(bool preferOidcRedirectPort = false) =>
        new LoopbackAuthRedirectBroker(preferOidcRedirectPort);
}

/// <summary>
///     Desktop redirect broker: a loopback HTTP listener receives the redirect in the system browser and
///     answers it with a short confirmation page. The port is normally picked per attempt because the
///     authorization service registers loopback redirects for the shared client id — but an OIDC
///     authorization additionally demands a redirect URI that matches the registered one character by
///     character, so an OIDC-capable attempt comes back on <see cref="SectlAuthService.OidcRedirectPort" />
///     instead. If that port cannot be bound the sign-in still works, only the id_token is lost.
/// </summary>
public sealed class LoopbackAuthRedirectBroker : IAuthRedirectBroker
{
    private readonly bool _preferOidcRedirectPort;
    private HttpListener? _listener;

    public LoopbackAuthRedirectBroker(bool preferOidcRedirectPort = false) =>
        _preferOidcRedirectPort = preferOidcRedirectPort;

    /// <summary>The browser round trip, unchanged from the original desktop sign-in flow.</summary>
    public TimeSpan WaitTimeout { get; } = TimeSpan.FromMinutes(5);

    public string CreateRedirectUri() =>
        $"http://localhost:{(_preferOidcRedirectPort ? SectlAuthService.OidcRedirectPort : GetFreePort())}/callback";

    public Task PrepareAsync(AuthRedirectAttempt attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"{attempt.RedirectUri}/");
        _listener.Start();
        return Task.CompletedTask;
    }

    public async Task<AuthRedirectResult> WaitForRedirectAsync(AuthRedirectAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        var listener = _listener ?? throw new InvalidOperationException("授权回调监听尚未启动。");
        // The timeout belongs to the broker, so it surfaces as a TimeoutException the caller can turn
        // into a conclusion instead of an open-ended wait. It spans the whole loop below: a browser that
        // asks for a resource before the redirect must not restart the clock.
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException("等待授权回调超时。");
            var context = await listener.GetContextAsync().WaitAsync(remaining, cancellationToken)
                .ConfigureAwait(false);
            var request = context.Request;
            var code = request.QueryString["code"];
            var errorCode = request.QueryString["error"];
            var error = request.QueryString["error_description"] ?? errorCode;

            // Not the redirect itself (a browser requesting /favicon.ico, a probe, a reload of the page).
            // Answer it and keep waiting: consuming the single context here would let a stray request delay
            // the real callback until the timeout.
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(error))
            {
                await ReplyAsync(context.Response, HttpStatusCode.NotFound, string.Empty, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            var pageState = string.IsNullOrWhiteSpace(code)
                ? string.Equals(errorCode, "access_denied", StringComparison.Ordinal)
                    ? AuthCallbackPageState.Denied
                    : AuthCallbackPageState.Failed
                : AuthCallbackPageState.Success;
            await ReplyAsync(context.Response, HttpStatusCode.OK,
                    LoopbackAuthCallbackPage.Render(pageState, errorCode, GlobalConstants.Version),
                    cancellationToken)
                .ConfigureAwait(false);

            return new AuthRedirectResult(attempt.RedirectUri, request.QueryString["state"] ?? string.Empty,
                attempt.CodeVerifier, code, error, attempt.Nonce);
        }
    }

    /// <summary>The loopback flow always completes inside the attempt, so nothing can be resumed.</summary>
    public Task<AuthRedirectResult?> TakePendingRedirectAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<AuthRedirectResult?>(null);

    public void Dispose()
    {
        try
        {
            _listener?.Close();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (HttpListenerException)
        {
        }

        _listener = null;
    }

    /// <summary>
    ///     Writes one reply and always closes the response. A browser that closed the tab first makes the write
    ///     throw; that must never travel out of here, because the redirect has already been read and the caller
    ///     is about to exchange the code. The document is self-contained, so the policy below is enough to stop
    ///     it from reaching anything.
    /// </summary>
    private static async Task ReplyAsync(HttpListenerResponse response, HttpStatusCode status, string html,
        CancellationToken cancellationToken)
    {
        try
        {
            response.StatusCode = (int)status;
            response.Headers["Content-Security-Policy"] =
                "default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Cache-Control"] = "no-store";
            if (html.Length == 0) return;
            var bytes = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
        {
            // The tab is gone; the outcome of the sign-in does not depend on this page being delivered.
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static int GetFreePort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        return ((IPEndPoint)tcp.LocalEndpoint).Port;
    }
}
