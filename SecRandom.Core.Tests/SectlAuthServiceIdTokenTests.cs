using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Auth;
using SecRandom.Services.Config;

namespace SecRandom.Core.Tests;

/// <summary>
///     Covers the one policy decision <see cref="SectlAuthService.VerifyIdTokenAsync" /> embodies: an
///     <c>id_token</c> that does not verify is dropped, and the sign-in it arrived with still succeeds.
///     The claim-level rules live in <see cref="SectlIdTokenValidatorTests" />.
/// </summary>
public sealed class SectlAuthServiceIdTokenTests
{
    private const string Issuer = "https://appwrite.sectl.cn";
    private const string Nonce = "attempt-nonce";
    private static readonly RSA SigningKey = RSA.Create(2048);

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string JwksJson()
    {
        var parameters = SigningKey.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    kid = "test-kid",
                    n = Base64Url(parameters.Modulus!),
                    e = Base64Url(parameters.Exponent!)
                }
            }
        });
    }

    private static string DiscoveryJson() => JsonSerializer.Serialize(new
    {
        issuer = Issuer,
        authorization_endpoint = $"{Issuer}/oauth/authorize",
        token_endpoint = $"{Issuer}/api/oauth/token",
        jwks_uri = $"{Issuer}/.well-known/jwks.json",
        id_token_signing_alg_values_supported = new[] { "RS256" },
        scopes_supported = new[] { "openid", "profile", "email" }
    });

    private static string CreateIdToken(string nonce, string? issuer = null, RSA? signingKey = null,
        DateTimeOffset? expiresAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        var header = new Dictionary<string, object> { ["alg"] = "RS256", ["typ"] = "JWT", ["kid"] = "test-kid" };
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = issuer ?? Issuer,
            ["aud"] = SectlAuthService.ClientId,
            ["exp"] = (expiresAt ?? now.AddHours(1)).ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nonce"] = nonce,
            ["sub"] = "user-1"
        };
        var headerPart = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadPart = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = (signingKey ?? SigningKey).SignData(
            Encoding.ASCII.GetBytes($"{headerPart}.{payloadPart}"), HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return $"{headerPart}.{payloadPart}.{Base64Url(signature)}";
    }

    private static (SectlAuthService Service, SectlTokenStore Store) CreateService(HttpMessageHandler handler)
        => CreateService(handler, TestTokenStore.Create());

    private static (SectlAuthService Service, SectlTokenStore Store) CreateService(HttpMessageHandler handler,
        SectlTokenStore store)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(SectlAuthService.ApiBaseUrl) };
        var factory = new StubHttpClientFactory(httpClient);
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(new MainConfigModel()));
        var deviceUuidStore = new DeviceUuidStore(configHandler, NullLogger<DeviceUuidStore>.Instance);
        return (new SectlAuthService(store, factory, deviceUuidStore, NullLogger<SectlAuthService>.Instance,
            new LoopbackAuthRedirectBrokerFactory()), store);
    }

    /// <summary>Serves the real discovery document and key set shape the service will see in production.</summary>
    private static RecordingHandler DiscoverableHandler()
    {
        var discovery = DiscoveryJson();
        var jwks = JwksJson();
        return new RecordingHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/.well-known/openid-configuration" => Json(discovery),
            "/.well-known/jwks.json" => Json(jwks),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static SectlToken TokenWithIdToken(string? idToken, string? nonce) => new(
        "access-token", "refresh-token", "user-1", 31_536_000)
    {
        IdToken = idToken,
        IdTokenNonce = nonce
    };

    [Fact]
    public async Task VerifyIdTokenAsync_WhenTheSignatureVerifies_KeepsTheToken()
    {
        var (service, _) = CreateService(DiscoverableHandler());
        var idToken = CreateIdToken(Nonce);

        var result = await service.VerifyIdTokenAsync(TokenWithIdToken(idToken, Nonce),
            TestContext.Current.CancellationToken);

        Assert.Null(service.LastIdTokenRejection);
        Assert.Equal(idToken, result.IdToken);
        Assert.Equal(Nonce, result.IdTokenNonce);
    }

    [Fact]
    public async Task VerifyIdTokenAsync_WhenTheTokenWasSignedByAnotherKey_DropsItButKeepsTheSession()
    {
        var (service, _) = CreateService(DiscoverableHandler());

        var result = await service.VerifyIdTokenAsync(
            TokenWithIdToken(CreateIdToken(Nonce, signingKey: RSA.Create(2048)), Nonce),
            TestContext.Current.CancellationToken);

        Assert.Equal(SectlIdTokenRejection.InvalidSignature, service.LastIdTokenRejection);
        Assert.Null(result.IdToken);
        Assert.Null(result.IdTokenNonce);
        // The point of the policy: the session the access token establishes is untouched.
        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("refresh-token", result.RefreshToken);
    }

    [Fact]
    public async Task VerifyIdTokenAsync_WhenTheTokenNamesAnotherIssuer_DropsIt()
    {
        var (service, _) = CreateService(DiscoverableHandler());

        var result = await service.VerifyIdTokenAsync(
            TokenWithIdToken(CreateIdToken(Nonce, issuer: "https://evil.example"), Nonce),
            TestContext.Current.CancellationToken);

        Assert.Equal(SectlIdTokenRejection.InvalidIssuer, service.LastIdTokenRejection);
        Assert.Null(result.IdToken);
    }

    [Fact]
    public async Task VerifyIdTokenAsync_WhenTheDiscoveryDocumentIsUnavailable_DropsTheToken()
    {
        var (service, _) = CreateService(new RecordingHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await service.VerifyIdTokenAsync(TokenWithIdToken(CreateIdToken(Nonce), Nonce),
            TestContext.Current.CancellationToken);

        Assert.Equal(SectlIdTokenRejection.KeysUnavailable, service.LastIdTokenRejection);
        Assert.Null(result.IdToken);
    }

    [Fact]
    public async Task VerifyIdTokenAsync_WhenTheTokenExpired_DropsIt()
    {
        var (service, _) = CreateService(DiscoverableHandler());

        var result = await service.VerifyIdTokenAsync(
            TokenWithIdToken(CreateIdToken(Nonce, expiresAt: DateTimeOffset.UtcNow.AddHours(-1)), Nonce),
            TestContext.Current.CancellationToken);

        Assert.Equal(SectlIdTokenRejection.Expired, service.LastIdTokenRejection);
        Assert.Null(result.IdToken);
    }

    [Fact]
    public async Task VerifyIdTokenAsync_WhenTheAuthorizationWasPlainOAuth_LeavesItAlone()
    {
        var (service, _) = CreateService(new RecordingHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await service.VerifyIdTokenAsync(TokenWithIdToken(null, null),
            TestContext.Current.CancellationToken);

        Assert.Null(service.LastIdTokenRejection);
        Assert.Null(result.IdToken);
        Assert.Equal("access-token", result.AccessToken);
    }

    [Fact]
    public async Task InitializeAsync_WhenAStoredIdTokenWasNeverVerified_DropsItButKeepsTheSession()
    {
        // A token file written before verification existed: it carries an id_token but no stamp, so the
        // claims in it were never proven and must not be loaded as if they had been.
        var store = TestTokenStore.Create();
        await store.SaveAsync(new SectlToken("access-token", "refresh-token", "user-1", 31_536_000)
        {
            IdToken = "legacy.unverified.token",
            IdTokenNonce = Nonce
        }, TestContext.Current.CancellationToken);

        var (service, _) = CreateService(SignedInHandler(), store);
        await service.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(service.Token);
        Assert.Equal("access-token", service.Token!.AccessToken);
        Assert.Null(service.Token.IdToken);
        Assert.Null(service.Token.IdTokenNonce);
        Assert.True(service.IsSignedIn);
    }

    [Fact]
    public async Task InitializeAsync_WhenAStoredIdTokenWasVerified_KeepsIt()
    {
        var store = TestTokenStore.Create();
        var verifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await store.SaveAsync(new SectlToken("access-token", "refresh-token", "user-1", 31_536_000)
        {
            IdToken = "verified.token.value",
            IdTokenNonce = Nonce,
            IdTokenVerifiedAt = verifiedAt
        }, TestContext.Current.CancellationToken);

        var (service, _) = CreateService(SignedInHandler(), store);
        await service.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("verified.token.value", service.Token!.IdToken);
        Assert.Equal(verifiedAt, service.Token.IdTokenVerifiedAt);
    }

    private static RecordingHandler SignedInHandler() => new((request, _) =>
        request.RequestUri!.AbsolutePath switch
        {
            "/api/oauth/introspect" => Json("{\"active\":true}"),
            "/api/oauth/userinfo" => Json("{\"user_id\":\"user-1\"}"),
            "/api/oauth/heartbeat" => new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

    private sealed class RecordingHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
