using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Services.Config;
using SecRandom.Shared;

namespace SecRandom.Services.Auth;

public sealed class SectlAuthService(IHttpClientFactory httpClientFactory, DeviceUuidStore deviceUuidStore)
{
    public const string ClientId = "69c8cd6a0012dd3ea10a";
    private const string AuthBaseUrl = "https://appwrite.sectl.cn";
    private const string AppwriteEndpoint = "https://appwrite.sectl.cn/v1";
    private const string AppwriteProjectId = "69bd6e700005458848db";
    private const string UserDataTableId = "user_data";
    private const string DatabaseId = "69bd89d8000304c37368";
    private const string AvatarBucketId = "69cce3720009a343f892";
    private const string BrowserBaseUrl = "https://sectl.cn";
    private const string OAuthScope = "user:read cloud:read cloud:write";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan[] InitializationRetryDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1)
    ];
    private readonly string _tokenPath = Utils.GetFilePath("config", "sectl-auth.json");
    private SectlToken? _token;
    private bool _initialized;

    public SectlToken? Token => _token;
    public bool IsSignedIn => !string.IsNullOrWhiteSpace(_token?.AccessToken);
    public SectlUser? User { get; private set; }
    public byte[]? AvatarBytes { get; private set; }
    public event EventHandler? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        try
        {
            if (File.Exists(_tokenPath))
                _token = JsonSerializer.Deserialize<SectlToken>(await File.ReadAllTextAsync(_tokenPath, cancellationToken), JsonOptions);
            if (IsSignedIn)
            {
                await InitializeAccountDataWithRetryAsync(cancellationToken);
            }
        }
        catch
        {
            User = null;
            AvatarBytes = null;
        }

        _initialized = true;

        StateChanged?.Invoke(this, EventArgs.Empty);
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
        var port = GetFreePort();
        var redirectUri = $"http://localhost:{port}/callback";
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        using var listener = new HttpListener();
        listener.Prefixes.Add($"{redirectUri}/");
        listener.Start();

        var query = string.Join("&", new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = OAuthScope,
            ["state"] = state
        }.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        var launcher = IAppHost.GetService<Desktop.IExternalLauncher>();
        if (!launcher.TryOpenUri($"{BrowserBaseUrl}/oauth/authorize?{query}"))
            throw new InvalidOperationException("无法打开浏览器。");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
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
        await response.OutputStream.WriteAsync(bytes, timeout.Token);
        response.Close();
        if (!string.IsNullOrWhiteSpace(error)) throw new InvalidOperationException($"SECTL 授权失败：{error}");
        if (string.IsNullOrWhiteSpace(code) || !string.Equals(state, returnedState, StringComparison.Ordinal))
            throw new InvalidOperationException("SECTL 授权回调无效。");

        var client = httpClientFactory.CreateClient();
        var publicIp = await GetPublicIpAsync(client, timeout.Token)
            ?? throw new InvalidOperationException("无法获取公网 IP，授权已取消，请检查网络连接。");
        var payload = new { grant_type = "authorization_code", code, client_id = ClientId, redirect_uri = redirectUri, code_verifier = verifier, device_uuid = deviceUuidStore.GetOrCreate().ToString(), ip_address = publicIp };
        using var result = await client.PostAsJsonAsync($"{AuthBaseUrl}/api/oauth/token", payload, timeout.Token);
        result.EnsureSuccessStatusCode();
        _token = await result.Content.ReadFromJsonAsync<SectlToken>(JsonOptions, timeout.Token) ?? throw new InvalidOperationException("SECTL 未返回 token。");
        await SaveAsync();
        await InitializeAccountDataWithRetryAsync(timeout.Token);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (IsSignedIn)
        {
            var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{AuthBaseUrl}/api/oauth/logout");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token!.AccessToken);
            try { await client.SendAsync(request, cancellationToken); } catch { }
        }
        _token = null;
        User = null;
        AvatarBytes = null;
        if (File.Exists(_tokenPath)) File.Delete(_tokenPath);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<SectlUser?> GetUserInfoAsync(CancellationToken cancellationToken, bool allowRefresh = true)
    {
        if (!IsSignedIn) return null;

        // Account profile fields are owned by the public user_data table. Use
        // the OAuth token only to identify the row; userinfo is a compatibility
        // fallback for older tokens that do not carry user_id.
        var tokenUserId = _token?.UserId;
        if (!string.IsNullOrWhiteSpace(tokenUserId))
        {
            var userData = await GetUserDataRowAsync(tokenUserId, cancellationToken, allowRefresh);
            if (userData is not null)
                return userData;
        }

        var userinfo = await GetOAuthUserInfoAsync(cancellationToken, allowRefresh);
        if (userinfo is null)
            return null;

        var userId = userinfo.ResolvedUserId ?? _token?.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            return userinfo;

        var profile = await GetUserDataRowAsync(userId, cancellationToken, allowRefresh: false);
        return profile ?? userinfo;
    }

    private async Task<SectlUser?> GetOAuthUserInfoAsync(CancellationToken cancellationToken, bool allowRefresh)
    {
        if (!IsSignedIn) return null;
        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{AuthBaseUrl}/api/oauth/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token!.AccessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized
            && allowRefresh
            && await TryRefreshTokenAsync(cancellationToken))
            return await GetOAuthUserInfoAsync(cancellationToken, allowRefresh: false);

        if (!response.IsSuccessStatusCode) return null;

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
        return SectlUser.TryParse(payload);
    }

    private async Task<SectlUser?> GetUserDataRowAsync(
        string userId,
        CancellationToken cancellationToken,
        bool allowRefresh = true)
    {
        try
        {
            var queries = new[]
            {
                JsonSerializer.Serialize(new { method = "equal", attribute = "user_id", values = new[] { userId } }, JsonOptions),
                JsonSerializer.Serialize(new { method = "select", values = new[] { "user_id", "email", "user_name", "avatar_file_id" } }, JsonOptions)
            };
            var queryString = string.Join("&", queries.Select(query => $"queries[]={Uri.EscapeDataString(query)}"));
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{AppwriteEndpoint}/tablesdb/{DatabaseId}/tables/{UserDataTableId}/rows?{queryString}");
            request.Headers.TryAddWithoutValidation("X-Appwrite-Project", AppwriteProjectId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token!.AccessToken);
            using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized
                && allowRefresh
                && await TryRefreshTokenAsync(cancellationToken))
                return await GetUserDataRowAsync(userId, cancellationToken, allowRefresh: false);

            if (!response.IsSuccessStatusCode)
                return null;

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
            if (payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("rows", out var rows)
                || rows.ValueKind != JsonValueKind.Array
                || rows.GetArrayLength() == 0)
                return null;

            return SectlUser.TryParse(rows[0]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> TryRefreshTokenAsync(CancellationToken cancellationToken)
    {
        var currentToken = _token;
        var refreshToken = currentToken?.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
            return false;

        try
        {
            var client = httpClientFactory.CreateClient();
            var publicIp = await GetPublicIpAsync(client, cancellationToken);
            if (string.IsNullOrWhiteSpace(publicIp))
                return false;

            var payload = new
            {
                grant_type = "refresh_token",
                refresh_token = refreshToken,
                client_id = ClientId,
                device_uuid = deviceUuidStore.GetOrCreate().ToString(),
                ip_address = publicIp
            };
            using var response = await client.PostAsJsonAsync($"{AuthBaseUrl}/api/oauth/refresh", payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;

            var refreshed = await response.Content.ReadFromJsonAsync<SectlToken>(JsonOptions, cancellationToken);
            if (refreshed is null || string.IsNullOrWhiteSpace(refreshed.AccessToken))
                return false;

            _token = refreshed with
            {
                RefreshToken = string.IsNullOrWhiteSpace(refreshed.RefreshToken)
                    ? refreshToken
                    : refreshed.RefreshToken,
                UserId = refreshed.UserId ?? currentToken?.UserId
            };
            await SaveAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<byte[]?> GetAvatarBytesAsync(string? avatarUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl)
            || !Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (!string.IsNullOrWhiteSpace(_token?.AccessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token.AccessToken);
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

    private static async Task<string?> GetPublicIpAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://uapis.cn/api/v1/network/myip");
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var document = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (document.ValueKind == JsonValueKind.Object && document.TryGetProperty("ip", out var value))
        {
            var ip = value.GetString();
            if (IPAddress.TryParse(ip, out _))
                return ip;
        }

        return null;
    }

    private static int GetFreePort()
    {
        using var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        return ((IPEndPoint)tcp.LocalEndpoint).Port;
    }

    private async Task SaveAsync()
    {
        var directory = Path.GetDirectoryName(_tokenPath)!;
        Directory.CreateDirectory(directory);
        var temporary = $"{_tokenPath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(_token, JsonOptions), Encoding.UTF8);
        File.Move(temporary, _tokenPath, true);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record SectlToken(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

public sealed record SectlUser(
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("user_name")] string? UserName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("avatar_file_id")] string? AvatarFileId)
{
    private const string AppwriteEndpoint = "https://appwrite.sectl.cn/v1";
    private const string AppwriteProjectId = "69bd6e700005458848db";
    private const string AvatarBucketId = "69cce3720009a343f892";

    public string? ResolvedUserName => FirstNonBlank(UserName, Data?.UserName);
    public string? ResolvedUserId => FirstNonBlank(UserId, Data?.UserId);
    public string? ResolvedEmail => FirstNonBlank(Email, Data?.Email);
    public string? ResolvedAvatarFileId => FirstNonBlank(AvatarFileId, Data?.AvatarFileId);

    public string? ResolvedAvatarUrl => BuildAvatarFileUrl(ResolvedAvatarFileId);

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
        return new SectlUser(direct.UserId, direct.UserName, direct.Email, direct.AvatarFileId)
        {
            Data = data
        };
    }

    public SectlUser Merge(SectlUser? fallback)
    {
        if (fallback is null)
            return this;

        return new SectlUser(
            FirstNonBlank(ResolvedUserId, fallback.ResolvedUserId),
            FirstNonBlank(ResolvedUserName, fallback.ResolvedUserName),
            FirstNonBlank(ResolvedEmail, fallback.ResolvedEmail),
            FirstNonBlank(ResolvedAvatarFileId, fallback.ResolvedAvatarFileId));
    }

    private static SectlUserData ReadFields(JsonElement value) => new(
        ReadString(value, "user_id"),
        ReadString(value, "user_name"),
        ReadString(value, "email"),
        ReadString(value, "avatar_file_id"));

    private static string? ReadString(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? FirstNonBlank(property.GetString())
            : null;

    private static string? BuildAvatarFileUrl(string? fileId) => string.IsNullOrWhiteSpace(fileId)
        ? null
        : $"{AppwriteEndpoint}/storage/buckets/{AvatarBucketId}/files/{Uri.EscapeDataString(fileId)}/view?project={AppwriteProjectId}";

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

}

public sealed record SectlUserData(
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("user_name")] string? UserName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("avatar_file_id")] string? AvatarFileId);
