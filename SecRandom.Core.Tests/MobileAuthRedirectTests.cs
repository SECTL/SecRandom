using System.Reflection;
using SecRandom.Mobile;
using SecRandom.Services.Auth;
using SecRandom.Shared;

namespace SecRandom.Core.Tests;

/// <summary>
///     Desktop sign-in keeps its loopback listener, and a mobile sign-in comes back through the app's
///     custom scheme. Both must hand the same shape (redirect URI, state, PKCE verifier, code/error) to
///     the token exchange, and the mobile one must survive the process being reclaimed while the user
///     was in the browser.
/// </summary>
public sealed class LoopbackAuthRedirectBrokerTests
{
    [Fact]
    public async Task WaitForRedirectAsync_AnswersTheBrowserAndReturnsTheCode()
    {
        using var broker = new LoopbackAuthRedirectBroker();
        var attempt = new AuthRedirectAttempt(broker.CreateRedirectUri(), "expected-state", "verifier");
        await broker.PrepareAsync(attempt, TestContext.Current.CancellationToken);

        // The listener answers inside WaitForRedirectAsync, so the wait has to be running first.
        var wait = broker.WaitForRedirectAsync(attempt, TestContext.Current.CancellationToken);
        using var client = new HttpClient();
        using var response = await client.GetAsync($"{attempt.RedirectUri}?code=code-1&state=expected-state",
            TestContext.Current.CancellationToken);
        var result = await wait;

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Authorization successful", await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal("code-1", result.Code);
        Assert.Equal("expected-state", result.State);
        Assert.Equal("verifier", result.CodeVerifier);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task WaitForRedirectAsync_SurfacesTheServiceProvidedFailure()
    {
        using var broker = new LoopbackAuthRedirectBroker();
        var attempt = new AuthRedirectAttempt(broker.CreateRedirectUri(), "expected-state", "verifier");
        await broker.PrepareAsync(attempt, TestContext.Current.CancellationToken);

        var wait = broker.WaitForRedirectAsync(attempt, TestContext.Current.CancellationToken);
        using var client = new HttpClient();
        using var response = await client.GetAsync(
            $"{attempt.RedirectUri}?error=access_denied&error_description=denied",
            TestContext.Current.CancellationToken);
        var result = await wait;

        Assert.Contains("Authorization failed", await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal("denied", result.Error);
        Assert.Null(result.Code);
    }

    [Fact]
    public async Task TakePendingRedirectAsync_IsAlwaysEmpty()
    {
        using var broker = new LoopbackAuthRedirectBroker();

        Assert.Null(await broker.TakePendingRedirectAsync(TestContext.Current.CancellationToken));
        Assert.Equal(TimeSpan.FromMinutes(5), broker.WaitTimeout);
    }
}

/// <summary>
///     The mobile broker owns a recorded attempt in <c>data/config</c>, because a phone may reclaim the
///     backgrounded process while the user is signing in.
/// </summary>
public sealed class MobileAuthRedirectBrokerTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "mobile-auth-tests",
        Guid.NewGuid().ToString("N"));

    public MobileAuthRedirectBrokerTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        try
        {
            if (Directory.Exists(_dataRoot))
                Directory.Delete(_dataRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task WaitForRedirectAsync_ReturnsTheRedirectOfTheAttempt()
    {
        var router = new MobileAuthCallbackRouter();
        var broker = new MobileAuthRedirectBroker(router);
        var attempt = new AuthRedirectAttempt(broker.CreateRedirectUri(), "state-1", "verifier-1");
        await broker.PrepareAsync(attempt, TestContext.Current.CancellationToken);

        var wait = broker.WaitForRedirectAsync(attempt, TestContext.Current.CancellationToken);
        router.Deliver($"{attempt.RedirectUri}?code=code-1&state=state-1");
        var result = await wait;

        Assert.Equal("code-1", result.Code);
        Assert.Equal("state-1", result.State);
        Assert.Equal("verifier-1", result.CodeVerifier);
        Assert.Equal(MobileAuthCallbackRouter.RedirectUri, result.RedirectUri);
        // The recorded attempt is single-use: a consumed redirect cannot be replayed.
        Assert.Null(await broker.TakePendingRedirectAsync(TestContext.Current.CancellationToken));
        Assert.False(File.Exists(PendingPath()));
    }

    [Fact]
    public async Task WaitForRedirectAsync_SurfacesTheServiceProvidedFailure()
    {
        var router = new MobileAuthCallbackRouter();
        var broker = new MobileAuthRedirectBroker(router);
        var attempt = new AuthRedirectAttempt(broker.CreateRedirectUri(), "state-2", "verifier-2");
        await broker.PrepareAsync(attempt, TestContext.Current.CancellationToken);

        var wait = broker.WaitForRedirectAsync(attempt, TestContext.Current.CancellationToken);
        router.Deliver($"{attempt.RedirectUri}?error=access_denied&error_description=denied");
        var result = await wait;

        Assert.Equal("denied", result.Error);
        Assert.Null(result.Code);
    }

    [Fact]
    public async Task TakePendingRedirectAsync_ResumesARedirectThatArrivedBeforeTheHostExisted()
    {
        var router = new MobileAuthCallbackRouter();
        var broker = new MobileAuthRedirectBroker(router);
        var attempt = new AuthRedirectAttempt(broker.CreateRedirectUri(), "state-3", "verifier-3");
        await broker.PrepareAsync(attempt, TestContext.Current.CancellationToken);

        // Nothing has come back yet, so the app has nothing to resume.
        Assert.Null(await broker.TakePendingRedirectAsync(TestContext.Current.CancellationToken));

        router.Deliver($"{attempt.RedirectUri}?code=code-3&state=state-3");
        var resumed = await broker.TakePendingRedirectAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(resumed);
        Assert.Equal("code-3", resumed!.Code);
        Assert.Equal("verifier-3", resumed.CodeVerifier);
        Assert.Equal(attempt.RedirectUri, resumed.RedirectUri);
        Assert.Null(await broker.TakePendingRedirectAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TakePendingRedirectAsync_IgnoresARedirectFromAnotherAttempt()
    {
        var router = new MobileAuthCallbackRouter();
        var broker = new MobileAuthRedirectBroker(router);
        var attempt = new AuthRedirectAttempt(broker.CreateRedirectUri(), "state-4", "verifier-4");
        await broker.PrepareAsync(attempt, TestContext.Current.CancellationToken);

        router.Deliver($"{attempt.RedirectUri}?code=code-4&state=stale-state");

        Assert.Null(await broker.TakePendingRedirectAsync(TestContext.Current.CancellationToken));
    }

    private static string PendingPath() => Path.Combine(Utils.DataRoot, "config", "sectl-auth-pending.json");

    private static void ConfigureDataRootForTests(string dataRoot)
    {
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);
    }

    private static void ResetDataRootForTests()
    {
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);
    }

    private static MethodInfo GetUtilsMethod(string name)
    {
        return typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException($"Utils.{name} was not found.");
    }
}

/// <summary>The router is the one place a platform head hands a deep link to the shared app.</summary>
public sealed class MobileAuthCallbackRouterTests
{
    [Fact]
    public async Task WaitAsync_CompletesWhenTheHeadDeliversTheRedirect()
    {
        var router = new MobileAuthCallbackRouter();

        var wait = router.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        router.Deliver($"{MobileAuthCallbackRouter.RedirectUri}?code=code-1&state=state-1");
        var redirect = await wait;

        Assert.Equal("code-1", QueryValue(redirect, "code"));
        Assert.Equal("state-1", QueryValue(redirect, "state"));
    }

    [Fact]
    public void Deliver_BuffersARedirectUntilAnAttemptReadsIt()
    {
        var router = new MobileAuthCallbackRouter();

        router.Deliver($"{MobileAuthCallbackRouter.RedirectUri}?code=code-2");

        Assert.True(router.TryTakeBuffered(out var redirect));
        Assert.NotNull(redirect);
        Assert.Equal("code-2", QueryValue(redirect!, "code"));
        Assert.False(router.TryTakeBuffered(out _));
    }

    [Fact]
    public void DeliverFromPlatform_AdoptsARedirectThatArrivedBeforeTheHostExisted()
    {
        // The link may have started the process, so the URL is parked before any router can be resolved.
        MobileAuthCallbackRouter.DeliverEarly($"{MobileAuthCallbackRouter.RedirectUri}?code=code-3");
        var router = new MobileAuthCallbackRouter();

        Assert.True(router.TryTakeBuffered(out var redirect));
        Assert.Equal("code-3", QueryValue(redirect!, "code"));
    }

    [Fact]
    public void Deliver_IgnoresAUrlWithAnotherScheme()
    {
        var router = new MobileAuthCallbackRouter();

        router.Deliver("https://secrandom-sync.sectl.cn/session/abc");

        Assert.False(router.TryTakeBuffered(out _));
    }

    [Fact]
    public async Task WaitAsync_WithAnotherScheme_TimesOutInsteadOfConsumingIt()
    {
        var router = new MobileAuthCallbackRouter();

        var wait = router.WaitAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        router.Deliver("https://example.invalid/callback?code=code-4");

        await Assert.ThrowsAsync<TimeoutException>(() => wait);
    }

    [Fact]
    public void WaitAsync_RejectsASecondConcurrentAttempt()
    {
        var router = new MobileAuthCallbackRouter();
        _ = router.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = router.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        });
    }

    private static string? QueryValue(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator < 0 || !string.Equals(pair[..separator], name, StringComparison.Ordinal))
                continue;

            return Uri.UnescapeDataString(pair[(separator + 1)..]);
        }

        return null;
    }
}
