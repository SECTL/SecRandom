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
    private const string BrowserBaseUrl = "https://sectl.cn";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _tokenPath = Utils.GetFilePath("config", "sectl-auth.json");
    private SectlToken? _token;

    public SectlToken? Token => _token;
    public bool IsSignedIn => !string.IsNullOrWhiteSpace(_token?.AccessToken);
    public SectlUser? User { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(_tokenPath))
                _token = JsonSerializer.Deserialize<SectlToken>(await File.ReadAllTextAsync(_tokenPath, cancellationToken), JsonOptions);
            if (IsSignedIn)
                User = await GetUserInfoAsync(cancellationToken);
        }
        catch
        {
            _token = null;
            User = null;
        }
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
            ["scope"] = "user:read",
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
        User = await GetUserInfoAsync(timeout.Token);
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
        if (File.Exists(_tokenPath)) File.Delete(_tokenPath);
    }

    private async Task<SectlUser?> GetUserInfoAsync(CancellationToken cancellationToken)
    {
        if (!IsSignedIn) return null;
        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{AuthBaseUrl}/api/oauth/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token!.AccessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<SectlUser>(JsonOptions, cancellationToken);
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
    string? Email);
