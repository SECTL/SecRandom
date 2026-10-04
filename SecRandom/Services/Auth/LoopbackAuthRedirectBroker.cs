using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SecRandom.Services.Auth;

/// <summary>Creates the desktop loopback broker; it has no dependencies of its own.</summary>
public sealed class LoopbackAuthRedirectBrokerFactory : IAuthRedirectBrokerFactory
{
    public IAuthRedirectBroker Create() => new LoopbackAuthRedirectBroker();
}

/// <summary>
///     Desktop redirect broker: a loopback HTTP listener on a free port receives the redirect in the
///     system browser and answers it with a short confirmation page. The port is picked per attempt
///     because the authorization service registers loopback redirects for the shared client id.
/// </summary>
public sealed class LoopbackAuthRedirectBroker : IAuthRedirectBroker
{
    private HttpListener? _listener;

    /// <summary>The browser round trip, unchanged from the original desktop sign-in flow.</summary>
    public TimeSpan WaitTimeout { get; } = TimeSpan.FromMinutes(5);

    public string CreateRedirectUri() => $"http://localhost:{GetFreePort()}/callback";

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
        // into a conclusion instead of an open-ended wait.
        var context = await listener.GetContextAsync().WaitAsync(WaitTimeout, cancellationToken)
            .ConfigureAwait(false);
        var request = context.Request;
        var response = context.Response;
        var code = request.QueryString["code"];
        var returnedState = request.QueryString["state"];
        var error = request.QueryString["error_description"] ?? request.QueryString["error"];
        var html = string.IsNullOrWhiteSpace(code)
            ? "<h1>Authorization failed</h1><p>You can close this window.</p>"
            : "<h1>Authorization successful</h1><p>You can close this window.</p>";
        var bytes = Encoding.UTF8.GetBytes($"<html><meta charset='utf-8'><body>{html}</body></html>");
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        response.Close();

        return new AuthRedirectResult(attempt.RedirectUri, returnedState ?? string.Empty, attempt.CodeVerifier,
            code, error);
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

    private static int GetFreePort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        return ((IPEndPoint)tcp.LocalEndpoint).Port;
    }
}
