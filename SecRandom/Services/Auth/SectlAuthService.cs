using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Services.Config;
using SecRandom.Shared;

namespace SecRandom.Services.Auth;

/// <summary>
///     Desktop SECTL OAuth session. It owns the access/refresh pair, the refresh policy, and the one
///     authorized-request boundary other SECTL APIs use.
///     The refresh endpoint rotates the refresh token on every success and invalidates the previous
///     one immediately, so this service must (1) persist the new pair atomically before the new
///     access token is used, (2) collapse concurrent refreshes into one flight, and (3) treat a
///     rejected refresh token as terminal instead of retrying it in a loop.
/// </summary>
public sealed class SectlAuthService(
    SectlTokenStore tokenStore,
    IHttpClientFactory httpClientFactory,
    DeviceUuidStore deviceUuidStore,
    ILogger<SectlAuthService> logger,
    IAuthRedirectBrokerFactory redirectBrokerFactory)
{
    public const string ClientId = "69c8cd6a0012dd3ea10a";
    public const string ApiBaseUrl = "https://appwrite.sectl.cn";
    private const string AppwriteEndpoint = "https://appwrite.sectl.cn/v1";
    private const string AppwriteProjectId = "69bd6e700005458848db";
    private const string UserDataTableId = "user_data";
    private const string DatabaseId = "69bd89d8000304c37368";
    private const string AvatarBucketId = "69cce3720009a343f892";
    private const string BrowserBaseUrl = "https://sectl.cn";
    private const string OAuthScope = "user:read cloud:read cloud:write";
    private const string OpenIdScope = "openid";
    private static readonly Uri HeartbeatUri = new($"{ApiBaseUrl}/api/oauth/heartbeat");
    private static readonly Uri IntrospectionUri = new($"{ApiBaseUrl}/api/oauth/introspect");
    private static readonly Uri DiscoveryUri = new($"{ApiBaseUrl}/.well-known/openid-configuration");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///     Port the desktop loopback redirect is pinned to. The OIDC branch compares the redirect URI
    ///     character by character and refuses both a custom scheme and a wildcard port, so an
    ///     OIDC-capable attempt has to come back to one fixed address — the platform registers
    ///     <c>http://localhost:52831/callback</c> exactly. Binding can still fail (Windows reserves
    ///     port ranges for Hyper-V/WinNAT), and the loopback broker then falls back to a free port,
    ///     which only costs the OIDC id_token, never the sign-in itself.
    /// </summary>
    public const int OidcRedirectPort = 52831;

    /// <summary>Fallback access-token lifetime when the service omits <c>expires_in</c> entirely.</summary>
    private const int DefaultAccessTokenLifetimeSeconds = 3600;

    /// <summary>
    ///     Lifetime placeholder for a token that never expires by time. The service keeps
    ///     <c>ACCESS_TOKEN_TTL_SECONDS = REFRESH_TOKEN_TTL_SECONDS = -1</c> and reports
    ///     <c>expires_in = 315360000</c> (ten years) purely so a generic OAuth client can still compute
    ///     a far-away absolute expiry; a token is only ever invalidated by a logout, a revoked refresh
    ///     family, or a deleted account.
    /// </summary>
    private const int UnlimitedExpiresInSeconds = 315_360_000;

    /// <summary>Refresh this long before the access token expires so a request never carries a dying token.</summary>
    private static readonly TimeSpan AccessTokenRefreshLead = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan RefreshRequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RefreshLockTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromSeconds(65);
    private static readonly TimeSpan[] InitializationRetryDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1)
    ];

    private readonly object _refreshGate = new();
    private readonly object _oidcDiscoveryGate = new();
    private readonly string _tokenPath = tokenStore.Path;
    private Task<SectlRefreshOutcome>? _inflightRefresh;
    private Task<SectlOidcConfiguration?>? _inflightOidcDiscovery;
    private SectlOidcConfiguration? _oidcConfiguration;
    private Task<SectlJwkSet?>? _inflightJwks;
    private SectlJwkSet? _jwks;
    private Uri? _jwksUri;
    private SectlToken? _token;
    private int _sessionVersion;
    private bool _initialized;

    public SectlToken? Token => _token;
    public bool IsSignedIn => !string.IsNullOrWhiteSpace(_token?.AccessToken);
    public SectlUser? User { get; private set; }
    public byte[]? AvatarBytes { get; private set; }

    /// <summary>
    ///     Set when the server permanently refused the stored credentials (a reused, revoked, or
    ///     expired refresh token, a wrong client, or a revoked access token). The local session has
    ///     been cleared at that point and the user has to authorize again.
    /// </summary>
    public bool RequiresReauthorization { get; private set; }

    /// <summary>
    ///     Why the last <c>id_token</c> was discarded, or <see langword="null" /> when it verified (or when
    ///     the authorization was plain OAuth 2.0 and returned none). A sign-in never fails over this, so this
    ///     is the only trace of a key set the client could not use — it is surfaced in the log and left for a
    ///     caller that wants to explain the missing OIDC identity.
    /// </summary>
    public SectlIdTokenRejection? LastIdTokenRejection { get; private set; }

    public event EventHandler? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        try
        {
            _token = DropUnverifiedStoredIdToken(await tokenStore.LoadAsync(cancellationToken).ConfigureAwait(false));
        }
        catch
        {
            _token = null;
        }

        _initialized = true;

        // 本地登录状态必须先放行：账号资料与头像来自网络，而启动链路会在 Host 启动前等待本方法，
        // 设置窗口又等待该启动任务，慢网络下会把开窗卡到几十秒；资料改在后台补齐并由 StateChanged 刷新。
        StateChanged?.Invoke(this, EventArgs.Empty);
        if (IsSignedIn)
            _ = RefreshAccountDataAsync(cancellationToken);
        else
            _ = ResumePendingAuthorizationAsync(cancellationToken);
    }

    /// <summary>
    ///     Completes an authorization whose redirect arrived in an earlier process lifetime. Android and
    ///     iOS may reclaim a backgrounded app while the user is signing in, so the attempt recorded
    ///     before the browser opened is exchanged on the next start instead of asking the user to
    ///     authorize again. The broker has already matched the returned state against that attempt.
    /// </summary>
    public async Task ResumePendingAuthorizationAsync(CancellationToken cancellationToken = default)
    {
        if (IsSignedIn)
            return;

        try
        {
            using var broker = redirectBrokerFactory.Create();
            var redirect = await broker.TakePendingRedirectAsync(cancellationToken).ConfigureAwait(false);
            if (redirect is null)
                return;

            await CompleteAuthorizationAsync(redirect, expectedState: null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "恢复未完成的 SECTL 授权失败。");
        }
    }

    /// <summary>
    ///     后台拉取账号资料与头像。失败时保留已加载的 token，只清空资料，
    ///     并通过 <see cref="StateChanged" /> 让界面从「资料不可用」自行收敛。
    /// </summary>
    private async Task RefreshAccountDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 令牌不按时间过期，会话被服务端结束的唯一方式是撤销；这一层校验只在后台进行，
            // 启动链路永远不等待它，校验本身失败也不会清掉本地会话。
            if (!await VerifyStoredSessionAsync(cancellationToken).ConfigureAwait(false))
                return;

            await InitializeAccountDataWithRetryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    ///     Confirms the stored session is still accepted. Tokens never expire by time, so revocation is
    ///     the only thing that can end one, and introspection is the endpoint that reports it as
    ///     <c>active: false</c>. A request that cannot produce an answer keeps the session.
    /// </summary>
    private async Task<bool> VerifyStoredSessionAsync(CancellationToken cancellationToken)
    {
        var introspection = await IntrospectAsync(cancellationToken).ConfigureAwait(false);
        if (introspection is null || introspection.Active)
            return true;

        logger.LogInformation("SECTL 令牌已被撤销，清除本地会话并要求重新授权。");
        ClearSession("session_revoked", requiresReauthorization: true);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return false;
    }

    /// <summary>
    ///     Asks the service whether the stored access token is still usable. It is best-effort by
    ///     design: an unreachable endpoint returns <see langword="null" />, so a flaky network can never
    ///     be mistaken for a revoked session. The response reports a token that has no time-based end
    ///     as <c>expires_at: "-1"</c>, which must never be parsed as a date (it would read as 2000-12-31
    ///     and make a freshly issued token look expired), so the raw value is handed back untouched.
    /// </summary>
    public async Task<SectlTokenIntrospection?> IntrospectAsync(CancellationToken cancellationToken = default)
    {
        var accessToken = _token?.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        try
        {
            var payload = new { token = accessToken, client_id = ClientId };
            using var request = new HttpRequestMessage(HttpMethod.Post, IntrospectionUri)
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            request.Headers.UserAgent.ParseAdd(BuildUserAgent());
            using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content
                .ReadFromJsonAsync<SectlTokenIntrospection>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "SECTL 令牌校验请求失败，保留本地会话。");
            return null;
        }
    }

    private async Task InitializeAccountDataWithRetryAsync(CancellationToken cancellationToken)
    {
        foreach (var delay in InitializationRetryDelays)
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            try
            {
                var user = await GetUserInfoAsync(cancellationToken);
                if (user is null)
                    continue;

                User = user;
                AvatarBytes = await GetAvatarBytesAsync(user.ResolvedAvatarUrl, cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // A transient startup failure is retried below. The token is
                // retained so the account can still be signed out safely.
            }
        }

        User = null;
        AvatarBytes = null;
    }

    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var nonce = Base64Url(RandomNumberGenerator.GetBytes(32));

        var broker = redirectBrokerFactory.Create(preferOidcRedirectPort: true);
        AuthRedirectAttempt attempt;
        bool requestsOpenId;
        try
        {
            (attempt, requestsOpenId) =
                await PrepareAttemptAsync(broker, state, verifier, nonce, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpListenerException)
        {
            // The pinned port is taken, or it lies inside a range Windows reserves for Hyper-V. Signing
            // in matters more than the id_token, so this attempt falls back to a free loopback port and
            // to the OAuth 2.0 flow, which does accept a dynamic port.
            logger.LogInformation("OIDC 回调端口 {Port} 不可用，改用随机环回端口并走 OAuth 2.0 流程。", OidcRedirectPort);
            broker.Dispose();
            broker = redirectBrokerFactory.Create(preferOidcRedirectPort: false);
            (attempt, requestsOpenId) =
                await PrepareAttemptAsync(broker, state, verifier, nonce, cancellationToken).ConfigureAwait(false);
        }

        using (broker)
        {
            var authorizeUrl = BuildAuthorizeUrl(attempt, challenge, requestsOpenId);
            // 桌面与 Android 交给系统浏览器，回调通过 loopback 或自定义 scheme 深链回来；
            // iOS 用 ASWebAuthenticationSession，回调由该浏览器直接交回。
            if (!IAppHost.GetService<IAuthBrowser>().TryOpenAuthorization(authorizeUrl))
                throw new InvalidOperationException("无法打开浏览器。");

            AuthRedirectResult redirect;
            try
            {
                redirect = await broker.WaitForRedirectAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // 授权页被关掉或服务端拒绝了 redirect_uri 时，回调永远不会到达：给用户一个明确的结论，
                // 而不是让界面一直停在“登录中”。
                throw new InvalidOperationException("登录超时，请重试。");
            }

            await CompleteAuthorizationAsync(redirect, state, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(AuthRedirectAttempt Attempt, bool RequestsOpenId)> PrepareAttemptAsync(
        IAuthRedirectBroker broker,
        string state,
        string codeVerifier,
        string nonce,
        CancellationToken cancellationToken)
    {
        var attempt = new AuthRedirectAttempt(broker.CreateRedirectUri(), state, codeVerifier, nonce);
        // The desktop broker starts its loopback listener here; a mobile broker records the attempt,
        // because the redirect may come back to a process that was reclaimed in the meantime.
        await broker.PrepareAsync(attempt, cancellationToken).ConfigureAwait(false);
        var requestsOpenId =
            await ShouldRequestOpenIdAsync(attempt.RedirectUri, cancellationToken).ConfigureAwait(false);
        return (attempt, requestsOpenId);
    }

    /// <summary>
    ///     Builds the authorization URL. The browser keeps using the site origin instead of the
    ///     discovery document's <c>authorization_endpoint</c>: the authorization endpoint authenticates
    ///     with the site login session, which only exists on the origin the user signed in on.
    /// </summary>
    private static string BuildAuthorizeUrl(AuthRedirectAttempt attempt, string codeChallenge,
        bool requestsOpenId)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = attempt.RedirectUri,
            ["response_type"] = "code",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = requestsOpenId ? $"{OAuthScope} {OpenIdScope}" : OAuthScope,
            ["state"] = attempt.State
        };
        if (requestsOpenId)
            parameters["nonce"] = attempt.Nonce ?? string.Empty;

        var query = string.Join("&",
            parameters.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return $"{BrowserBaseUrl}/oauth/authorize?{query}";
    }

    /// <summary>
    ///     Decides whether this attempt may ask for the OIDC branch. Two conditions must hold: the
    ///     redirect URI has to be one OIDC accepts — a registered <c>http</c> loopback URI on the pinned
    ///     port, because a custom scheme or a wildcard/dynamic port is refused by the authorization
    ///     endpoint — and the discovery document has to advertise <c>openid</c>. A discovery failure
    ///     therefore falls back to plain OAuth 2.0 instead of blocking the sign-in.
    /// </summary>
    private async Task<bool> ShouldRequestOpenIdAsync(string redirectUri, CancellationToken cancellationToken)
    {
        if (!IsOidcEligibleRedirect(redirectUri))
            return false;

        var configuration = await TryGetOidcConfigurationAsync(cancellationToken).ConfigureAwait(false);
        return configuration?.SupportsOpenId == true;
    }

    internal static bool IsOidcEligibleRedirect(string redirectUri) =>
        Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && IsLoopbackHost(uri.Host)
        && uri.Port == OidcRedirectPort;

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
        || string.Equals(host, "[::1]", StringComparison.Ordinal)
        || string.Equals(host, "::1", StringComparison.Ordinal);

    private Task<SectlOidcConfiguration?> TryGetOidcConfigurationAsync(CancellationToken cancellationToken)
    {
        lock (_oidcDiscoveryGate)
        {
            if (_oidcConfiguration is not null)
                return Task.FromResult<SectlOidcConfiguration?>(_oidcConfiguration);

            return _inflightOidcDiscovery ??= FetchOidcConfigurationAsync();
        }
    }

    private async Task<SectlOidcConfiguration?> FetchOidcConfigurationAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, DiscoveryUri);
            request.Headers.UserAgent.ParseAdd(BuildUserAgent());
            using var response = await httpClientFactory.CreateClient().SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var configuration = await response.Content
                .ReadFromJsonAsync<SectlOidcConfiguration>(JsonOptions).ConfigureAwait(false);
            lock (_oidcDiscoveryGate)
            {
                _oidcConfiguration = configuration;
            }

            return configuration;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "读取 SECTL OIDC 发现文档失败，本次授权走 OAuth 2.0 流程。");
            return null;
        }
        finally
        {
            lock (_oidcDiscoveryGate)
            {
                _inflightOidcDiscovery = null;
            }
        }
    }

    /// <summary>
    ///     Whether this client may follow a metadata address the discovery document names. The document is
    ///     fetched over HTTPS from the fixed API host, but <c>jwks_uri</c> comes <em>from</em> it, so a plain
    ///     HTTP key set on a public host would let anyone on the path substitute signing keys.
    /// </summary>
    private static bool IsAllowedMetadataUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);

    /// <summary>
    ///     Reads the published key set, cached for the process and fetched once at a time. A cached set is
    ///     reused while the document keeps naming the same <c>jwks_uri</c>; keys are never written to disk,
    ///     so a rotated key set cannot outlive the process.
    /// </summary>
    private Task<SectlJwkSet?> TryGetJwksAsync(Uri jwksUri, bool forceRefresh)
    {
        lock (_oidcDiscoveryGate)
        {
            if (!forceRefresh && _jwks is not null && Equals(_jwksUri, jwksUri))
                return Task.FromResult<SectlJwkSet?>(_jwks);
            if (!forceRefresh && _inflightJwks is not null)
                return _inflightJwks;

            return _inflightJwks = FetchJwksAsync(jwksUri);
        }
    }

    private async Task<SectlJwkSet?> FetchJwksAsync(Uri jwksUri)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, jwksUri);
            request.Headers.UserAgent.ParseAdd(BuildUserAgent());
            using var response = await httpClientFactory.CreateClient().SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var keySet = await response.Content
                .ReadFromJsonAsync<SectlJwkSet>(JsonOptions).ConfigureAwait(false);
            if (keySet?.Keys is null || keySet.Keys.Length == 0)
                return null;

            lock (_oidcDiscoveryGate)
            {
                _jwks = keySet;
                _jwksUri = jwksUri;
            }

            return keySet;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "读取 SECTL OIDC 公钥集失败，本次不校验 id_token。");
            return null;
        }
        finally
        {
            lock (_oidcDiscoveryGate)
            {
                _inflightJwks = null;
            }
        }
    }

    /// <summary>
    ///     Verifies the <c>id_token</c> the token exchange returned and drops it when it does not verify.
    ///     Verification is deliberately not a sign-in gate: an OIDC identity is an addition to a session
    ///     the access token already establishes, so a broken or rotated key set must never lock the user out.
    ///     What it must not do is leave an unverified claim set behind, so the token is stored without it and
    ///     <see cref="LastIdTokenRejection" /> carries the reason.
    /// </summary>
    public async Task<SectlToken> VerifyIdTokenAsync(SectlToken token, CancellationToken cancellationToken = default)
    {
        LastIdTokenRejection = null;
        if (string.IsNullOrWhiteSpace(token.IdToken))
            return token;

        var configuration = await TryGetOidcConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(configuration?.Issuer)
            || string.IsNullOrWhiteSpace(configuration.JwksUri)
            || !Uri.TryCreate(configuration.JwksUri, UriKind.Absolute, out var jwksUri)
            || !IsAllowedMetadataUri(jwksUri))
            return DropIdToken(token, SectlIdTokenRejection.KeysUnavailable);

        var parameters = new SectlIdTokenValidationParameters(configuration.Issuer, ClientId, token.IdTokenNonce);
        var keySet = await TryGetJwksAsync(jwksUri, forceRefresh: false).ConfigureAwait(false);
        var validation = SectlIdTokenValidator.Validate(token.IdToken, keySet, parameters);

        // A key rotation between publishing and signing is the one failure a second look can fix, so an
        // unknown kid re-reads the key set exactly once instead of rejecting a token the service just issued.
        if (validation.Rejection == SectlIdTokenRejection.UnknownKeyId)
        {
            keySet = await TryGetJwksAsync(jwksUri, forceRefresh: true).ConfigureAwait(false);
            validation = SectlIdTokenValidator.Validate(token.IdToken, keySet, parameters);
        }

        return validation.IsValid
            ? token with { IdTokenVerifiedAt = DateTimeOffset.UtcNow }
            : DropIdToken(token, validation.Rejection ?? SectlIdTokenRejection.InvalidSignature);
    }

    private SectlToken DropIdToken(SectlToken token, SectlIdTokenRejection rejection)
    {
        LastIdTokenRejection = rejection;
        logger.LogWarning("SECTL 返回的 id_token 未通过校验（{Reason}），已丢弃；本次登录不受影响。", rejection);
        return token with { IdToken = null, IdTokenNonce = null, IdTokenVerifiedAt = null };
    }

    /// <summary>
    ///     Drops an <c>id_token</c> read from disk that carries no verification stamp, which is what a token
    ///     file written by a build before verification existed looks like. The claims are not re-verified here
    ///     on purpose: an <c>id_token</c> lives about an hour, so re-checking at startup would mostly fail, and
    ///     fetching the key set on every launch would make a session depend on the network. Dropping is the
    ///     honest answer — that claim set was never proven — and it costs nothing, because the access token
    ///     keeps the session and a fresh authorization carries a fresh <c>id_token</c>.
    /// </summary>
    private SectlToken? DropUnverifiedStoredIdToken(SectlToken? token)
    {
        if (token is null || string.IsNullOrWhiteSpace(token.IdToken) || token.IdTokenVerifiedAt is not null)
            return token;

        logger.LogInformation("已丢弃上次登录留下的未校验 id_token；登录状态不受影响。");
        return token with { IdToken = null, IdTokenNonce = null };
    }

    /// <summary>
    ///     Exchanges an authorization code for the token pair. <paramref name="expectedState" /> is the
    ///     state this sign-in attempt generated; pass <see langword="null" /> only when the broker has
    ///     already matched the redirect against the attempt it recorded.
    /// </summary>
    private async Task CompleteAuthorizationAsync(AuthRedirectResult redirect, string? expectedState,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(redirect.Error))
            throw new InvalidOperationException($"SECTL 授权失败：{redirect.Error}");
        var stateMatches = expectedState is null || string.Equals(expectedState, redirect.State, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(redirect.Code) || !stateMatches)
            throw new InvalidOperationException("SECTL 授权回调无效。");

        var client = httpClientFactory.CreateClient();
        // 客户端不再自行解析并上报公网 IP：服务端从连接来源即可得知地址，本机地址只上传设备标识。
        var payload = new { grant_type = "authorization_code", code = redirect.Code, client_id = ClientId, redirect_uri = redirect.RedirectUri, code_verifier = redirect.CodeVerifier, device_uuid = deviceUuidStore.GetOrCreate().ToString() };
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/api/oauth/token")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        tokenRequest.Headers.UserAgent.ParseAdd(BuildUserAgent());
        using var result = await client.SendAsync(tokenRequest, cancellationToken);
        result.EnsureSuccessStatusCode();
        var issued = await result.Content.ReadFromJsonAsync<SectlToken>(JsonOptions, cancellationToken) ?? throw new InvalidOperationException("SECTL 未返回 token。");
        var authorizedAt = DateTimeOffset.UtcNow;
        var authorized = issued with
        {
            AccessTokenExpiresAt = authorizedAt.AddSeconds(ResolveAccessTokenLifetime(issued)),
            RefreshTokenIssuedAt = authorizedAt,
            // Only an OIDC authorization carries a nonce; keep it beside the id_token so verification can
            // replay the binding the token claims. The pending check compares it against the `nonce` claim.
            IdTokenNonce = string.IsNullOrWhiteSpace(issued.IdToken) ? null : redirect.Nonce
        };
        // Verifying here rather than at read time keeps one rule: whatever is stored was verified, and a
        // token that does not verify is dropped instead of being saved for a later caller to trust.
        var verified = await VerifyIdTokenAsync(authorized, cancellationToken).ConfigureAwait(false);
        await tokenStore.SaveAsync(verified, cancellationToken);
        SetSession(verified);
        await InitializeAccountDataWithRetryAsync(cancellationToken);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (IsSignedIn)
        {
            var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/api/oauth/logout");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token!.AccessToken);
            try { await client.SendAsync(request, cancellationToken); } catch { }
        }

        ClearSession("signed_out", requiresReauthorization: false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Sends an authenticated SECTL API request: it refreshes the access token before the call
    ///     when the stored token is about to expire, and once more when the service rejects it.
    ///     Callers pass a request factory because a rejected request must be rebuilt for the retry;
    ///     token storage and refresh policy stay inside this service.
    /// </summary>
    public async Task<HttpResponseMessage> SendAuthorizedAsync(
        Func<HttpRequestMessage> createRequest,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createRequest);
        var accessToken = await GetUsableAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("SECTL 账号未登录。");

        var response = await SendWithTokenAsync(createRequest, accessToken, completionOption, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        var description = await ReadErrorDescriptionAsync(response, cancellationToken).ConfigureAwait(false);
        if (IsAccessTokenRevoked(description))
        {
            // The access token was revoked server-side; a refresh cannot succeed, so end the local
            // session instead of rotating a refresh token that is already dead.
            logger.LogWarning("SECTL 访问令牌已被吊销，需要重新授权。");
            ClearSession("access_token_revoked", requiresReauthorization: true);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return response;
        }

        if (!await RefreshForRejectedTokenAsync(accessToken, cancellationToken).ConfigureAwait(false))
            return response;

        var retryToken = _token?.AccessToken;
        if (string.IsNullOrWhiteSpace(retryToken))
            return response;

        response.Dispose();
        return await SendWithTokenAsync(createRequest, retryToken, completionOption, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Returns the access token a non-HTTP transport should present as a bearer credential.
    /// </summary>
    /// <remarks>
    ///     The control node channel opens a WebSocket and puts the token in an <c>Authorization</c>
    ///     header (never a query parameter, which would land in access logs), so it cannot go through
    ///     <see cref="SendAuthorizedAsync" />. It still needs the same policy: refresh before the stored
    ///     expiry, and rotate once after the server rejects the token. Callers that get a rejected token
    ///     ask again with <paramref name="forceRefresh" /> set.
    /// </remarks>
    public async Task<string?> TryGetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh)
            return await GetUsableAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        var outcome = await RefreshSingleFlightAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (outcome.Status == SectlRefreshStatus.Ended)
            return null;
        if (outcome.Status == SectlRefreshStatus.Succeeded)
            return _token?.AccessToken;

        // A transient refresh failure must not drop a token that is still valid.
        var token = _token;
        return token is not null && !IsExpired(token) ? token.AccessToken : null;
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(Func<HttpRequestMessage> createRequest, string accessToken,
        HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        var request = createRequest();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            return await httpClientFactory.CreateClient()
                .SendAsync(request, completionOption, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            request.Dispose();
        }
    }

    /// <summary>
    /// Reports the authenticated device as active. A rejected access token is refreshed once
    /// before retrying so a long-running desktop session does not stop reporting after expiry.
    /// </summary>
    public Task<bool> SendHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        return SendHeartbeatAsync(allowRefresh: true, cancellationToken);
    }

    private async Task<bool> SendHeartbeatAsync(bool allowRefresh, CancellationToken cancellationToken)
    {
        string? accessToken = await GetUsableAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
            return false;

        using var request = new HttpRequestMessage(HttpMethod.Post, HeartbeatUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await httpClientFactory.CreateClient()
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var description = await ReadErrorDescriptionAsync(response, cancellationToken).ConfigureAwait(false);
            if (IsAccessTokenRevoked(description))
            {
                logger.LogWarning("SECTL 访问令牌已被吊销，需要重新授权。");
                ClearSession("access_token_revoked", requiresReauthorization: true);
                StateChanged?.Invoke(this, EventArgs.Empty);
                return false;
            }

            if (allowRefresh && await RefreshForRejectedTokenAsync(accessToken, cancellationToken).ConfigureAwait(false))
                return await SendHeartbeatAsync(allowRefresh: false, cancellationToken).ConfigureAwait(false);

            return false;
        }

        return response.IsSuccessStatusCode;
    }

    private async Task<SectlUser?> GetUserInfoAsync(CancellationToken cancellationToken, bool allowRefresh = true)
    {
        if (!IsSignedIn) return null;

        var userinfo = await GetOAuthUserInfoAsync(cancellationToken, allowRefresh);
        return userinfo;
    }

    private async Task<SectlUser?> GetOAuthUserInfoAsync(CancellationToken cancellationToken, bool allowRefresh)
    {
        if (!IsSignedIn) return null;
        var accessToken = await GetUsableAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken)) return null;

        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/api/oauth/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var description = await ReadErrorDescriptionAsync(response, cancellationToken);
            if (IsAccessTokenRevoked(description))
            {
                ClearSession("access_token_revoked", requiresReauthorization: true);
                StateChanged?.Invoke(this, EventArgs.Empty);
                return null;
            }

            if (allowRefresh && await RefreshForRejectedTokenAsync(accessToken, cancellationToken))
                return await GetOAuthUserInfoAsync(cancellationToken, allowRefresh: false);
        }

        if (!response.IsSuccessStatusCode) return null;

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
        return SectlUser.TryParse(payload);
    }

    /// <summary>
    ///     Returns an access token that is not about to expire. A token inside the refresh lead
    ///     window is refreshed first; a transient refresh failure still returns the current token
    ///     while it is valid, because the session is not endangered by a short outage.
    /// </summary>
    private async Task<string?> GetUsableAccessTokenAsync(CancellationToken cancellationToken)
    {
        var token = _token;
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            return null;
        if (!IsExpiringWithin(token, AccessTokenRefreshLead))
            return token.AccessToken;

        var outcome = await RefreshSingleFlightAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (outcome.Status == SectlRefreshStatus.Succeeded)
            return _token?.AccessToken;
        if (outcome.Status == SectlRefreshStatus.Ended)
            return null;
        return IsExpired(token) ? null : token.AccessToken;
    }

    /// <summary>
    ///     Refreshes after the service rejected an access token that had not reached its stored
    ///     expiry (the server may have revoked it early). Nothing is refreshed when another caller
    ///     already replaced that token.
    /// </summary>
    private async Task<bool> RefreshForRejectedTokenAsync(string rejectedAccessToken, CancellationToken cancellationToken)
    {
        // A session cleared by a concurrent terminal outcome has nothing left to retry with.
        if (string.IsNullOrWhiteSpace(_token?.AccessToken))
            return false;
        if (!string.Equals(_token.AccessToken, rejectedAccessToken, StringComparison.Ordinal))
            return true;

        var outcome = await RefreshSingleFlightAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return outcome.Status == SectlRefreshStatus.Succeeded
               && !string.IsNullOrWhiteSpace(_token?.AccessToken)
               && !string.Equals(_token.AccessToken, rejectedAccessToken, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Collapses every concurrent refresh into one flight. The shared flight deliberately does
    ///     not carry a caller token: one caller giving up must not abort a rotation the other
    ///     callers are waiting for. Callers observe their own cancellation while awaiting it.
    /// </summary>
    private Task<SectlRefreshOutcome> RefreshSingleFlightAsync()
    {
        lock (_refreshGate)
        {
            if (_inflightRefresh is { IsCompleted: false } running)
                return running;

            var flight = RefreshAsync();
            _inflightRefresh = flight;
            return flight;
        }
    }

    private async Task<SectlRefreshOutcome> RefreshAsync()
    {
        try
        {
            return await RefreshCoreAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "SECTL 令牌刷新出现未预期的错误。");
            return SectlRefreshOutcome.Unavailable("unexpected_error", retryable: false);
        }
        finally
        {
            lock (_refreshGate)
            {
                _inflightRefresh = null;
            }
        }
    }

    private async Task<SectlRefreshOutcome> RefreshCoreAsync()
    {
        var current = _token;
        var refreshToken = current?.RefreshToken;
        if (current is null || string.IsNullOrWhiteSpace(refreshToken))
        {
            // Without a refresh token there is nothing to rotate; the user has to authorize again.
            logger.LogWarning("SECTL 本地令牌缺少 refresh token，需要重新授权。");
            ClearSession("refresh_token_missing", requiresReauthorization: true);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return SectlRefreshOutcome.Ended("refresh_token_missing");
        }

        // Tokens never expire by time (`ACCESS_TOKEN_TTL_SECONDS = REFRESH_TOKEN_TTL_SECONDS = -1`), so
        // no local age check may end the session: a stored pair stays usable until the service revokes
        // it, which introspection reports and the refresh endpoint answers with `invalid_grant`.
        var sessionVersion = Volatile.Read(ref _sessionVersion);

        // Refreshing rotates a single-use token, so the whole read/rotate/write sequence holds a
        // cross-process lock: another window's process must never rotate the same token twice.
        var fileLock = await tokenStore.TryAcquireLockAsync(RefreshLockTimeout, CancellationToken.None)
            .ConfigureAwait(false);
        try
        {
            if (fileLock is null)
                logger.LogDebug("SECTL 刷新锁未取得，改用进程内单飞与磁盘重读保证一致性。");

            // Another process may have rotated the pair while this flight waited. Adopting the
            // token already on disk is exactly the recovery for an unknown network outcome.
            if (await AdoptRotatedTokenAsync(refreshToken).ConfigureAwait(false) is { } adopted)
                return adopted;

            for (var attempt = 1; ; attempt++)
            {
                var outcome = await SendRefreshRequestAsync(refreshToken).ConfigureAwait(false);
                if (outcome.Status == SectlRefreshStatus.Succeeded && outcome.Token is { } rotated)
                {
                    if (sessionVersion != Volatile.Read(ref _sessionVersion))
                        return SectlRefreshOutcome.Ended("session_changed");

                    await ApplyRotatedTokenAsync(rotated, current).ConfigureAwait(false);
                    return SectlRefreshOutcome.Success();
                }

                if (outcome.Status == SectlRefreshStatus.Ended)
                {
                    logger.LogWarning(
                        "SECTL refresh token 被服务端终态拒绝（{Code}）：{Description}；停止重试并要求重新授权。",
                        outcome.ErrorCode, outcome.Description);
                    ClearSession(outcome.ErrorCode ?? "invalid_grant", requiresReauthorization: true);
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    return outcome;
                }

                var attemptLimit = outcome.NetworkUnknown ? 2 : 3;
                if (!outcome.Retryable || attempt >= attemptLimit)
                {
                    logger.LogWarning("SECTL 令牌刷新暂时不可用（{Code}），保留本地会话并等待下次重试。",
                        outcome.ErrorCode);
                    return outcome;
                }

                var delay = ComputeBackoff(outcome, attempt);
                logger.LogDebug("SECTL 令牌刷新暂不可用（{Code}），{DelayMs}ms 后重试第 {Attempt} 次。",
                    outcome.ErrorCode, (int)delay.TotalMilliseconds, attempt + 1);
                await Task.Delay(delay, CancellationToken.None).ConfigureAwait(false);

                if (await AdoptRotatedTokenAsync(refreshToken).ConfigureAwait(false) is { } rotatedAfterBackoff)
                    return rotatedAfterBackoff;
            }
        }
        finally
        {
            fileLock?.Dispose();
        }
    }

    /// <summary>
    ///     Adopts a newer pair written to the token file by another process, so two processes never
    ///     rotate the same single-use refresh token in sequence.
    /// </summary>
    private async Task<SectlRefreshOutcome?> AdoptRotatedTokenAsync(string usedRefreshToken)
    {
        var stored = await tokenStore.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        if (stored is null || string.IsNullOrWhiteSpace(stored.AccessToken))
            return null;
        if (string.IsNullOrWhiteSpace(stored.RefreshToken)
            || string.Equals(stored.RefreshToken, usedRefreshToken, StringComparison.Ordinal))
            return null;

        SetSession(stored);
        logger.LogInformation("检测到其他进程已完成 SECTL 令牌轮换，复用磁盘上的新令牌。");
        return SectlRefreshOutcome.Success();
    }

    private async Task<SectlRefreshOutcome> SendRefreshRequestAsync(string refreshToken)
    {
        using var timeout = new CancellationTokenSource(RefreshRequestTimeout);
        try
        {
            var client = httpClientFactory.CreateClient();
            var payload = new
            {
                grant_type = "refresh_token",
                refresh_token = refreshToken,
                client_id = ClientId,
                device_uuid = deviceUuidStore.GetOrCreate().ToString()
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/api/oauth/refresh")
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            request.Headers.UserAgent.ParseAdd(BuildUserAgent());
            using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return ClassifyRefreshResponse(response, body);
        }
        catch (OperationCanceledException)
        {
            // The refresh request timed out: the server may or may not have rotated the pair.
            return SectlRefreshOutcome.NetworkFailure("timeout");
        }
        catch (HttpRequestException exception)
        {
            return SectlRefreshOutcome.NetworkFailure($"network_error: {exception.Message}");
        }
    }

    private static SectlRefreshOutcome ClassifyRefreshResponse(HttpResponseMessage response, string body)
    {
        var (code, description, retryAfter) = ParseOAuthError(body, response);
        if (response.IsSuccessStatusCode)
        {
            var token = TryDeserializeToken(body);
            return token is null || string.IsNullOrWhiteSpace(token.AccessToken)
                ? SectlRefreshOutcome.Unavailable("invalid_response")
                : SectlRefreshOutcome.Success(token);
        }

        return response.StatusCode switch
        {
            // 400 invalid_grant means the stored refresh token was already used, revoked, or has
            // expired. Retrying the same value can never succeed, and doing so is what produces the
            // server-side reuse detection storm, so the session ends here.
            HttpStatusCode.BadRequest => SectlRefreshOutcome.Ended(code ?? "invalid_grant", description),
            HttpStatusCode.Unauthorized => SectlRefreshOutcome.Ended(code ?? "invalid_client", description),
            HttpStatusCode.TooManyRequests => SectlRefreshOutcome.RateLimited(code ?? "rate_limited", retryAfter),
            HttpStatusCode.ServiceUnavailable => SectlRefreshOutcome.ServiceUnavailable(code ?? "temporarily_unavailable", retryAfter),
            _ when (int)response.StatusCode >= 500 => SectlRefreshOutcome.ServiceUnavailable(code ?? $"http_{(int)response.StatusCode}", retryAfter),
            _ => SectlRefreshOutcome.Unavailable(code ?? $"http_{(int)response.StatusCode}")
        };
    }

    private async Task ApplyRotatedTokenAsync(SectlToken rotated, SectlToken previous)
    {
        var applied = rotated with
        {
            // A rotation always returns a new refresh token; falling back to the previous one keeps a
            // session alive if an older service build omits it.
            RefreshToken = string.IsNullOrWhiteSpace(rotated.RefreshToken) ? previous.RefreshToken : rotated.RefreshToken,
            UserId = rotated.UserId ?? previous.UserId,
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ResolveAccessTokenLifetime(rotated)),
            RefreshTokenIssuedAt = previous.RefreshTokenIssuedAt
        };

        // 先落盘再用：刷新令牌单次使用，进程若在这两步之间崩溃，旧令牌已失效而新令牌尚未保存。
        try
        {
            await tokenStore.SaveAsync(applied).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The server has already rotated, so refusing the new pair would also cost this process
            // its session. Keep running on the in-memory pair and try to persist it again later.
            logger.LogError(exception, "SECTL 新令牌写入磁盘失败，本次仅在内存中继续使用。");
        }

        SetSession(applied);
        logger.LogInformation("SECTL 访问令牌已刷新并写入磁盘。");
    }

    private void SetSession(SectlToken token)
    {
        _token = token;
        RequiresReauthorization = false;
        Interlocked.Increment(ref _sessionVersion);
    }

    private void ClearSession(string reason, bool requiresReauthorization)
    {
        var wasSignedIn = _token is not null;
        Interlocked.Increment(ref _sessionVersion);
        _token = null;
        User = null;
        AvatarBytes = null;
        RequiresReauthorization = requiresReauthorization;
        tokenStore.Delete();
        if (wasSignedIn)
            logger.LogInformation("SECTL 会话已结束：{Reason}。", reason);
    }

    private static TimeSpan ComputeBackoff(SectlRefreshOutcome outcome, int attempt)
    {
        if (outcome.RetryAfter is { } hinted && hinted > TimeSpan.Zero)
            return hinted > MaximumBackoff ? MaximumBackoff : hinted;

        var baseDelay = outcome.ErrorCode is "rate_limited"
            ? TimeSpan.FromSeconds(1)
            : TimeSpan.FromMilliseconds(500);
        var scaled = baseDelay * Math.Pow(2, attempt - 1) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        return scaled > MaximumBackoff ? MaximumBackoff : scaled;
    }

    private static SectlToken? TryDeserializeToken(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<SectlToken>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (string? Code, string? Description, TimeSpan? RetryAfter) ParseOAuthError(
        string? body, HttpResponseMessage? response = null)
    {
        string? code = null;
        string? description = null;
        TimeSpan? retryAfter = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    code = ReadString(document.RootElement, "error") ?? ReadString(document.RootElement, "code");
                    description = ReadString(document.RootElement, "error_description")
                                  ?? ReadString(document.RootElement, "message");
                    if (document.RootElement.TryGetProperty("retry_after_ms", out var retryMilliseconds)
                        && retryMilliseconds.ValueKind == JsonValueKind.Number
                        && retryMilliseconds.TryGetDouble(out var value)
                        && value > 0)
                        retryAfter = TimeSpan.FromMilliseconds(value);
                }
            }
            catch (JsonException)
            {
                description = Truncate(body.Trim(), 200);
            }
        }

        if (retryAfter is null && response is not null)
        {
            var header = response.Headers.RetryAfter;
            if (header?.Delta is { } delta && delta > TimeSpan.Zero)
                retryAfter = delta;
            else if (header?.Date is { } date)
                retryAfter = date - DateTimeOffset.UtcNow;
        }

        return (code, description, retryAfter);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength] + "…";

    private static bool IsAccessTokenRevoked(string? description) =>
        description is not null && description.Contains("revoked", StringComparison.OrdinalIgnoreCase);

    private static int ResolveAccessTokenLifetime(SectlToken token) => token.ExpiresIn switch
    {
        // A negative lifetime is the service's own "never expires" constant; its documented response
        // already reports ten years instead, so this only guards a client that receives `-1` directly.
        < 0 => UnlimitedExpiresInSeconds,
        > 0 => token.ExpiresIn,
        _ => DefaultAccessTokenLifetimeSeconds
    };

    private static bool IsExpiringWithin(SectlToken token, TimeSpan lead) =>
        token.AccessTokenExpiresAt is { } expiresAt && expiresAt - DateTimeOffset.UtcNow <= lead;

    private static bool IsExpired(SectlToken token) =>
        token.AccessTokenExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow;

    /// <summary>
    ///     Reads the OAuth error body of a rejected request. The body is buffered by the content
    ///     implementation, so a caller that keeps the response can still read it afterwards.
    /// </summary>
    private static async Task<string?> ReadErrorDescriptionAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var (_, description, _) = ParseOAuthError(body, response);
            return description;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<byte[]?> GetAvatarBytesAsync(string? avatarUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl)
            || !Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.GetLeftPart(UriPartial.Authority), ApiBaseUrl, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static int GetFreePort()
    {
        using var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        return ((IPEndPoint)tcp.LocalEndpoint).Port;
    }

    private static string BuildUserAgent() =>
        $"SecRandom/{GlobalConstants.Version} ({RuntimeInformation.OSDescription}; {Environment.MachineName})";

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>How one refresh attempt ended, including whether retrying it can help.</summary>
    private enum SectlRefreshStatus
    {
        Succeeded,
        /// <summary>Rate limited or temporarily unavailable: back off and retry the same refresh.</summary>
        Transient,
        /// <summary>The credential is permanently refused: no retry can succeed.</summary>
        Ended
    }

    private readonly record struct SectlRefreshOutcome(
        SectlRefreshStatus Status,
        SectlToken? Token = null,
        string? ErrorCode = null,
        string? Description = null,
        bool Retryable = false,
        bool NetworkUnknown = false,
        TimeSpan? RetryAfter = null)
    {
        public static SectlRefreshOutcome Success(SectlToken? token = null) =>
            new(SectlRefreshStatus.Succeeded, Token: token);

        public static SectlRefreshOutcome Ended(string code, string? description = null) =>
            new(SectlRefreshStatus.Ended, ErrorCode: code, Description: description);

        public static SectlRefreshOutcome RateLimited(string code, TimeSpan? retryAfter) =>
            new(SectlRefreshStatus.Transient, ErrorCode: code, Retryable: true, RetryAfter: retryAfter);

        public static SectlRefreshOutcome ServiceUnavailable(string code, TimeSpan? retryAfter) =>
            new(SectlRefreshStatus.Transient, ErrorCode: code, Retryable: true, RetryAfter: retryAfter);

        public static SectlRefreshOutcome Unavailable(string code, bool retryable = true) =>
            new(SectlRefreshStatus.Transient, ErrorCode: code, Retryable: retryable);

        /// <summary>
        ///     The request never produced a usable answer (timeout or disconnect). The service may
        ///     already have rotated the pair, so the caller re-reads the token file before this
        ///     outcome is treated as a failure.
        /// </summary>
        public static SectlRefreshOutcome NetworkFailure(string code) =>
            new(SectlRefreshStatus.Transient, ErrorCode: code, Retryable: true, NetworkUnknown: true);
    }
}

public sealed record SectlToken(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("expires_in")] int ExpiresIn)
{
    /// <summary>
    ///     Local expiry of <see cref="AccessToken" />, stamped when the pair is persisted. Files
    ///     written before this field existed leave it empty, which only means the token is refreshed
    ///     after a rejection instead of shortly before it expires.
    /// </summary>
    [JsonPropertyName("access_token_expires_at")]
    public DateTimeOffset? AccessTokenExpiresAt { get; init; }

    /// <summary>
    ///     When the pair was first authorized. Rotation keeps this value, because a rotation never
    ///     restarts the session; it is recorded for diagnostics only — the service expires nothing by
    ///     time, so it no longer bounds how long the client keeps refreshing.
    /// </summary>
    [JsonPropertyName("refresh_token_issued_at")]
    public DateTimeOffset? RefreshTokenIssuedAt { get; init; }

    /// <summary>
    ///     The OIDC <c>id_token</c> the authorization returned, or <see langword="null" /> for a plain
    ///     OAuth 2.0 authorization, whenever the service could not sign one, and whenever the token did not
    ///     verify. It is only ever stored after <see cref="SectlIdTokenValidator" /> accepted its RS256
    ///     signature, issuer, audience, lifetime, and nonce against the published key set
    ///     (<see cref="SectlAuthService.VerifyIdTokenAsync" />), so its claims may be read; a token that
    ///     failed verification is dropped instead of being kept unverified.
    /// </summary>
    [JsonPropertyName("id_token")]
    public string? IdToken { get; init; }

    /// <summary>
    ///     The <c>nonce</c> sent with the authorization request that produced <see cref="IdToken" />. It is
    ///     the value verification compares the token's <c>nonce</c> claim against, so it is kept only while
    ///     the token it belongs to is kept.
    /// </summary>
    [JsonPropertyName("id_token_nonce")]
    public string? IdTokenNonce { get; init; }

    /// <summary>
    ///     When <see cref="IdToken" /> passed verification. Its only job is to keep the "stored means verified"
    ///     rule true across builds: a token file written before verification existed has an <c>id_token</c> but
    ///     no stamp, and <see cref="SectlAuthService.InitializeAsync" /> drops such a claim set on load instead
    ///     of trusting it — no signature check is repeated at startup, because that would need the key set on
    ///     every launch and would discard a perfectly good token whenever the network is down.
    /// </summary>
    [JsonPropertyName("id_token_verified_at")]
    public DateTimeOffset? IdTokenVerifiedAt { get; init; }
}

public sealed record SectlUser(
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("name")] string? UserName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl)
{
    public string? ResolvedUserName => FirstNonBlank(UserName, Data?.UserName);
    public string? ResolvedUserId => FirstNonBlank(UserId, Data?.UserId);
    public string? ResolvedEmail => FirstNonBlank(Email, Data?.Email);
    public string? ResolvedAvatarUrl => FirstNonBlank(AvatarUrl, Data?.AvatarUrl);

    [JsonIgnore]
    public SectlUserData? Data { get; init; }

    public static SectlUser? TryParse(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        var data = payload.TryGetProperty("data", out var nested) && nested.ValueKind == JsonValueKind.Object
            ? ReadFields(nested)
            : null;
        var direct = ReadFields(payload);
        return new SectlUser(direct.UserId, direct.UserName, direct.Email, direct.AvatarUrl)
        {
            Data = data
        };
    }

    private static SectlUserData ReadFields(JsonElement value) => new(
        ReadString(value, "user_id"),
        ReadString(value, "name"),
        ReadString(value, "email"),
        ReadString(value, "avatar_url"));

    private static string? ReadString(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? FirstNonBlank(property.GetString())
            : null;

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

public sealed record SectlUserData(
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("name")] string? UserName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl);
