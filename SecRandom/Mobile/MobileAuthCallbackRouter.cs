using SecRandom.Core.Abstraction;

namespace SecRandom.Mobile;

/// <summary>
///     Delivers the SECTL OAuth redirect that a platform head received through the app's custom URL
///     scheme to the shared sign-in flow.
///     A redirect can arrive before the Host exists, because the link may have started the process,
///     so a URL with no live waiter is parked in <see cref="DeliverEarly" /> and the router drains that
///     buffer when it is created.
/// </summary>
public sealed class MobileAuthCallbackRouter
{
    /// <summary>
    ///     Custom scheme the mobile heads claim. It must stay identical to the Android intent filter
    ///     and the iOS <c>CFBundleURLTypes</c> entry, and the SECTL authorization service must have
    ///     this redirect URI registered for the shared client id.
    /// </summary>
    public const string CallbackScheme = "cn.sectl.secrandom.mobile";

    /// <summary>Authority part of the redirect URI; heads declare it in their platform manifest.</summary>
    public const string CallbackHost = "oauth";

    /// <summary>Path part of the redirect URI; heads declare it in their platform manifest.</summary>
    public const string CallbackPath = "/callback";

    /// <summary>Redirect URI a mobile host hands to the authorization endpoint.</summary>
    public static string RedirectUri => $"{CallbackScheme}://{CallbackHost}{CallbackPath}";

    private static readonly object EarlyGate = new();
    private static Uri? _earlyRedirect;

    private readonly object _gate = new();
    private readonly Queue<Uri> _redirects = new();
    private TaskCompletionSource<Uri>? _waiter;

    public MobileAuthCallbackRouter()
    {
        // The head parks the URL while the Host is still being built, so adopt it on construction.
        DrainEarlyRedirect();
    }

    /// <summary>
    ///     Forwards a platform-delivered URL. Heads call this without checking whether the Host
    ///     exists: the router is used when it is already resolvable and the URL is buffered otherwise.
    /// </summary>
    public static void DeliverFromPlatform(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (IAppHost.TryGetService<MobileAuthCallbackRouter>() is { } router)
            router.Deliver(url);
        else
            DeliverEarly(url);
    }

    /// <summary>Parks a redirect that arrived before this router could be resolved.</summary>
    public static void DeliverEarly(string url)
    {
        if (!TryParse(url, out var redirect))
            return;

        lock (EarlyGate)
        {
            _earlyRedirect = redirect;
        }
    }

    /// <summary>Hands a redirect to the waiting sign-in attempt, or buffers it for the next one.</summary>
    public void Deliver(string url)
    {
        if (!TryParse(url, out var redirect))
            return;

        TaskCompletionSource<Uri>? waiter;
        lock (_gate)
        {
            waiter = _waiter;
            if (waiter is null)
                _redirects.Enqueue(redirect);
        }

        waiter?.TrySetResult(redirect);
    }

    /// <summary>
    ///     Returns a buffered redirect, including one parked before the Host was built. The caller
    ///     owns it: it is removed from the buffer.
    /// </summary>
    public bool TryTakeBuffered(out Uri? redirect)
    {
        DrainEarlyRedirect();
        lock (_gate)
        {
            if (_redirects.Count == 0)
            {
                redirect = null;
                return false;
            }

            redirect = _redirects.Dequeue();
            return true;
        }
    }

    /// <summary>
    ///     Waits for the next redirect. Only one sign-in attempt may wait at a time: a second waiter
    ///     could otherwise steal the redirect that belongs to the first one.
    /// </summary>
    public Task<Uri> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        TaskCompletionSource<Uri> waiter;
        lock (_gate)
        {
            if (_redirects.Count > 0)
                return Task.FromResult(_redirects.Dequeue());
            if (_waiter is not null)
                throw new InvalidOperationException("已有登录授权回调正在等待。");

            waiter = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiter = waiter;
        }

        return WaitWithTimeoutAsync(waiter, timeout, cancellationToken);
    }

    private async Task<Uri> WaitWithTimeoutAsync(TaskCompletionSource<Uri> waiter, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            return await waiter.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            // A redirect that landed while the timeout was being observed still belongs to this attempt.
            if (waiter.Task.IsCompletedSuccessfully)
                return waiter.Task.Result;

            throw;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_waiter, waiter))
                    _waiter = null;
            }
        }
    }

    private void DrainEarlyRedirect()
    {
        Uri? redirect;
        lock (EarlyGate)
        {
            redirect = _earlyRedirect;
            _earlyRedirect = null;
        }

        if (redirect is null)
            return;

        lock (_gate)
        {
            if (_waiter is { } waiter && waiter.TrySetResult(redirect))
                return;

            _redirects.Enqueue(redirect);
        }
    }

    private static bool TryParse(string url, out Uri redirect)
    {
        redirect = null!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return false;
        if (!string.Equals(parsed.Scheme, CallbackScheme, StringComparison.OrdinalIgnoreCase))
            return false;

        redirect = parsed;
        return true;
    }
}
