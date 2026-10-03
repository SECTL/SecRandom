using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Auth;
using SecRandom.Services.Config;

namespace SecRandom.Core.Tests;

public sealed class SectlAuthServiceTests
{
    private const string RefreshedTokenJson =
        "{\"access_token\":\"refreshed-access-token\",\"refresh_token\":\"refreshed-refresh-token\",\"user_id\":\"user-1\",\"expires_in\":3600}";

    private const string PublicIpJson = "{\"ip\":\"203.0.113.10\"}";

    [Fact]
    public async Task SendHeartbeatAsync_UsesOAuthHeartbeatEndpointAndBearerToken()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        var (service, _) = CreateService(handler);
        SetToken(service, new SectlToken("access-token", "refresh-token", "user-1", 3600));

        bool sent = await service.SendHeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.True(sent);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://appwrite.sectl.cn/api/oauth/heartbeat", request.Uri.ToString());
        Assert.Equal("Bearer access-token", request.Authorization);
    }

    [Fact]
    public async Task SendHeartbeatAsync_WhenSignedOut_DoesNotSendARequest()
    {
        var handler = new RecordingHandler((_, _) =>
            throw new Xunit.Sdk.XunitException("A signed-out account must not send a heartbeat."));
        var (service, _) = CreateService(handler);

        bool sent = await service.SendHeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.False(sent);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendHeartbeatAsync_WhenAccessTokenIsRejected_RefreshesAndRetriesOnce()
    {
        var heartbeatTokens = new List<string?>();
        var handler = new RecordingHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/oauth/heartbeat")
            {
                heartbeatTokens.Add(request.Headers.Authorization?.Parameter);
                return heartbeatTokens.Count == 1
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    : new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.RequestUri.Host == "uapis.cn")
                return Json(PublicIpJson);

            if (path == "/api/oauth/refresh")
                return Json(RefreshedTokenJson);

            throw new Xunit.Sdk.XunitException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var (service, _) = CreateService(handler);
        SetToken(service, new SectlToken("access-token", "refresh-token", "user-1", 3600));

        bool sent = await service.SendHeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.True(sent);
        Assert.Equal(["access-token", "refreshed-access-token"], heartbeatTokens);
    }

    [Fact]
    public async Task Refresh_RotationIsPersistedBeforeTheNewAccessTokenIsUsed()
    {
        var handler = CreateRotationHandler();
        var (service, store) = CreateService(handler);
        SetToken(service, new SectlToken("access-token", "refresh-token", "user-1", 3600));

        bool sent = await service.SendHeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.True(sent);
        var stored = ReadStoredToken(store);
        Assert.NotNull(stored);
        Assert.Equal("refreshed-access-token", stored!.AccessToken);
        Assert.Equal("refreshed-refresh-token", stored.RefreshToken);
        Assert.NotNull(stored.AccessTokenExpiresAt);
        Assert.True(stored.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(50));
        // The in-memory session matches exactly what a restart would read back.
        Assert.Equal(stored.RefreshToken, service.Token?.RefreshToken);
        // The atomic replace leaves no temporary file behind.
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.Path)!, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentRejectedRequests_TriggerExactlyOneRotation()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/oauth/heartbeat")
                return request.Headers.Authorization?.Parameter == "access-token"
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    : new HttpResponseMessage(HttpStatusCode.NoContent);

            if (request.RequestUri.Host == "uapis.cn")
                return Json(PublicIpJson);

            if (path == "/api/oauth/refresh")
                return Json(RefreshedTokenJson);

            throw new Xunit.Sdk.XunitException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var (service, _) = CreateService(handler);
        SetToken(service, new SectlToken("access-token", "refresh-token", "user-1", 3600));

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => service.SendHeartbeatAsync(TestContext.Current.CancellationToken)));

        Assert.All(results, Assert.True);
        // Eight callers share one single-use rotation instead of eight racing rotations.
        Assert.Equal(1, handler.Count("/api/oauth/refresh"));
    }

    [Fact]
    public async Task Refresh_WhenRefreshTokenIsRejectedWithInvalidGrant_EndsTheSessionWithoutRetrying()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/oauth/heartbeat")
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            if (request.RequestUri.Host == "uapis.cn")
                return Json(PublicIpJson);
            if (path == "/api/oauth/refresh")
                return Json("{\"error\":\"invalid_grant\",\"error_description\":\"Refresh token reused\"}",
                    HttpStatusCode.BadRequest);

            throw new Xunit.Sdk.XunitException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var (service, store) = CreateService(handler);
        SetToken(service, new SectlToken("access-token", "refresh-token", "user-1", 3600));
        var stateChanges = 0;
        service.StateChanged += (_, _) => stateChanges++;

        bool sent = await service.SendHeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.False(sent);
        // A terminal rejection is never retried with the same dead refresh token.
        Assert.Equal(1, handler.Count("/api/oauth/refresh"));
        Assert.False(service.IsSignedIn);
        Assert.True(service.RequiresReauthorization);
        Assert.False(File.Exists(store.Path));
        Assert.True(stateChanges > 0);
    }

    [Fact]
    public async Task Refresh_WhenRateLimited_RetriesTheSameRefreshAfterRetryAfter()
    {
        var refreshAttempts = 0;
        var usedRefreshTokens = new List<string?>();
        var handler = new RecordingHandler((request, body) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/oauth/heartbeat")
                return request.Headers.Authorization?.Parameter == "access-token"
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    : new HttpResponseMessage(HttpStatusCode.NoContent);
            if (request.RequestUri.Host == "uapis.cn")
                return Json(PublicIpJson);
            if (path == "/api/oauth/refresh")
            {
                refreshAttempts++;
                usedRefreshTokens.Add(JsonField(body, "refresh_token"));
                return refreshAttempts == 1
                    ? Json("{\"error\":\"rate_limited\",\"retry_after_ms\":30}", HttpStatusCode.TooManyRequests)
                    : Json(RefreshedTokenJson);
            }

            throw new Xunit.Sdk.XunitException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var (service, _) = CreateService(handler);
        SetToken(service, new SectlToken("access-token", "refresh-token", "user-1", 3600));

        bool sent = await service.SendHeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.True(sent);
        Assert.Equal(2, refreshAttempts);
        // Rate limiting retries the very same refresh, not a second rotation of a token already spent.
        Assert.All(usedRefreshTokens, token => Assert.Equal("refresh-token", token));
    }

    [Fact]
    public async Task Refresh_WhenServiceUnavailable_RetriesWithBackoff()
    {
        var refreshAttempts = 0;
        var handler = new RecordingHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/oauth/heartbeat")
                return request.Headers.Authorization?.Parameter == "access-token"
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    : new HttpResponseMessage(HttpStatusCode.NoContent);
            if (request.RequestUri.Host == "uapis.cn")
                return Json(PublicIpJson);
            if (path == "/api/oauth/refresh")
            {
                refreshAttempts++;
                return refreshAttempts == 1
                    ? Json("{\"error\":\"temporarily_unavailable\"}", HttpStatusCode.ServiceUnavailable)
                    : Json(RefreshedTokenJson);
            }

            throw new Xunit.Sdk.XunitException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var (service, _) = CreateService(handler);
        SetToken(service, new SectlToken("access-token", "refresh-token", "user-1", 3600));

        bool sent = await service.SendHeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.True(sent);
        Assert.Equal(2, refreshAttempts);
    }

    [Fact]
    public async Task Refresh_WhenAccessTokenIsRevoked_DoesNotRotateTheRefreshToken()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/oauth/heartbeat")
                return Json(
                    "{\"error\":\"invalid_token\",\"error_description\":\"Access token has been revoked\"}",
                    HttpStatusCode.Unauthorized);

            throw new Xunit.Sdk.XunitException($"A revoked access token must not trigger {path}.");
        });
        var (service, store) = CreateService(handler);
        SetToken(service, new SectlToken("access-token", "refresh-token", "user-1", 3600));

        bool sent = await service.SendHeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.False(sent);
        Assert.Equal(0, handler.Count("/api/oauth/refresh"));
        Assert.False(service.IsSignedIn);
        Assert.True(service.RequiresReauthorization);
        Assert.False(File.Exists(store.Path));
    }

    [Fact]
    public async Task AccessTokenNearExpiry_IsRefreshedBeforeTheRequest()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/oauth/heartbeat")
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (request.RequestUri.Host == "uapis.cn")
                return Json(PublicIpJson);
            if (path == "/api/oauth/refresh")
                return Json(RefreshedTokenJson);

            throw new Xunit.Sdk.XunitException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var (service, _) = CreateService(handler);
        SetToken(service, new SectlToken("access-token", "refresh-token", "user-1", 3600)
        {
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1)
        });

        bool sent = await service.SendHeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.True(sent);
        Assert.Equal(1, handler.Count("/api/oauth/refresh"));
        var heartbeat = Assert.Single(handler.Requests, item => item.Path == "/api/oauth/heartbeat");
        Assert.Equal("Bearer refreshed-access-token", heartbeat.Authorization);
    }

    [Fact]
    public async Task Refresh_WhenThe180DayWindowIsOver_RequiresReauthorization()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/oauth/heartbeat")
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            throw new Xunit.Sdk.XunitException($"An expired refresh window must not send {path}.");
        });
        var (service, store) = CreateService(handler);
        SetToken(service, new SectlToken("access-token", "refresh-token", "user-1", 3600)
        {
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
            RefreshTokenIssuedAt = DateTimeOffset.UtcNow.AddDays(-181)
        });

        bool sent = await service.SendHeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.False(sent);
        Assert.Equal(0, handler.Count("/api/oauth/refresh"));
        Assert.True(service.RequiresReauthorization);
        Assert.False(File.Exists(store.Path));
    }

    [Fact]
    public async Task Refresh_WhenAnotherProcessAlreadyRotated_AdoptsTheStoredPair()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/oauth/heartbeat")
                return request.Headers.Authorization?.Parameter == "other-access-token"
                    ? new HttpResponseMessage(HttpStatusCode.NoContent)
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized);

            throw new Xunit.Sdk.XunitException($"An adopted rotation must not send {path}.");
        });
        var (service, store) = CreateService(handler);
        SetToken(service, new SectlToken("access-token", "refresh-token", "user-1", 3600));
        await store.SaveAsync(new SectlToken("other-access-token", "other-refresh-token", "user-1", 3600)
        {
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        }, TestContext.Current.CancellationToken);

        bool sent = await service.SendHeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.True(sent);
        Assert.Equal(0, handler.Count("/api/oauth/refresh"));
        Assert.Equal("other-refresh-token", service.Token?.RefreshToken);
    }

    [Fact]
    public async Task Refresh_WhenTheNetworkOutcomeIsUnknown_KeepsTheSession()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/oauth/heartbeat")
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            if (request.RequestUri.Host == "uapis.cn")
                return Json(PublicIpJson);
            if (path == "/api/oauth/refresh")
                throw new HttpRequestException("connection reset");

            throw new Xunit.Sdk.XunitException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var (service, _) = CreateService(handler);
        SetToken(service, new SectlToken("access-token", "refresh-token", "user-1", 3600));

        bool sent = await service.SendHeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.False(sent);
        // The pair is never discarded on an unknown outcome: the server may not have rotated at all.
        Assert.Equal(2, handler.Count("/api/oauth/refresh"));
        Assert.Equal("refresh-token", service.Token?.RefreshToken);
        Assert.True(service.IsSignedIn);
        Assert.False(service.RequiresReauthorization);
    }

    [Fact]
    public async Task TokenFile_RoundTripsRotationMetadata()
    {
        var store = TestTokenStore.Create();
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds());

        await store.SaveAsync(new SectlToken("access", "refresh", "user-1", 3600)
        {
            AccessTokenExpiresAt = expiresAt,
            RefreshTokenIssuedAt = issuedAt
        }, TestContext.Current.CancellationToken);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(expiresAt, loaded!.AccessTokenExpiresAt!.Value);
        Assert.Equal(issuedAt, loaded.RefreshTokenIssuedAt!.Value);
    }

    [Theory]
    [InlineData("https://appwrite.sectl.cn/avatar.png", true)]
    [InlineData("https://avatar.example.test/avatar.png", false)]
    [InlineData("http://appwrite.sectl.cn/avatar.png", false)]
    [InlineData("https://appwrite.sectl.cn:8443/avatar.png", false)]
    [InlineData("https://appwrite.sectl.cn.attacker.test/avatar.png", false)]
    public async Task AvatarDownload_OnlyUsesTrustedHttpsOriginWithoutBearer(string url, bool shouldSend)
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3])
        });
        var (service, _) = CreateService(handler);
        SetToken(service, new SectlToken("dummy-access-token", "dummy-refresh-token", "user-1", 3600));
        var method = typeof(SectlAuthService).GetMethod("GetAvatarBytesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = await (Task<byte[]?>)method.Invoke(service, [url, TestContext.Current.CancellationToken])!;

        Assert.Equal(shouldSend, handler.Requests.Count == 1);
        Assert.Null(handler.Requests.SingleOrDefault()?.Authorization);
        if (shouldSend)
            Assert.Equal(new byte[] { 1, 2, 3 }, result);
        else
            Assert.Null(result);
    }

    /// <summary>Heartbeat rejected once, then accepted; refresh rotates; public IP resolves.</summary>
    private static RecordingHandler CreateRotationHandler() => new((request, _) =>
    {
        string path = request.RequestUri!.AbsolutePath;
        if (path == "/api/oauth/heartbeat")
            return request.Headers.Authorization?.Parameter == "access-token"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        if (request.RequestUri.Host == "uapis.cn")
            return Json(PublicIpJson);
        if (path == "/api/oauth/refresh")
            return Json(RefreshedTokenJson);

        throw new Xunit.Sdk.XunitException($"Unexpected request: {request.Method} {request.RequestUri}");
    });

    private static (SectlAuthService Service, SectlTokenStore Store) CreateService(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(SectlAuthService.ApiBaseUrl) };
        var factory = new StubHttpClientFactory(httpClient);
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(new MainConfigModel()));
        var deviceUuidStore = new DeviceUuidStore(configHandler, NullLogger<DeviceUuidStore>.Instance);
        var store = TestTokenStore.Create();
        return (new SectlAuthService(store, factory, deviceUuidStore, NullLogger<SectlAuthService>.Instance), store);
    }

    private static void SetToken(SectlAuthService service, SectlToken token)
    {
        var field = typeof(SectlAuthService).GetField("_token", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(service, token);
    }

    private static SectlToken? ReadStoredToken(SectlTokenStore store) =>
        JsonSerializer.Deserialize<SectlToken>(File.ReadAllText(store.Path), new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string? JsonField(string? body, string name)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        using var document = JsonDocument.Parse(body);
        return document.RootElement.ValueKind == JsonValueKind.Object
               && document.RootElement.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body)
    {
        public string Path => Uri.AbsolutePath;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> send) : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<RecordedRequest> _requests = [];

        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_gate)
                    return _requests.ToArray();
            }
        }

        public int Count(string path)
        {
            lock (_gate)
                return _requests.Count(request => request.Path == path);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _requests.Add(new RecordedRequest(request.Method, request.RequestUri!,
                    request.Headers.Authorization?.ToString(), body));
            }

            return send(request, body);
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) { }
        public override void DeleteConfig<T>(T value) { }
    }
}
