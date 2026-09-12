using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Auth;
using SecRandom.Services.Config;

namespace SecRandom.Core.Tests;

public sealed class SectlCloudStorageClientTests
{
    private const string ClientId = SectlAuthService.ClientId;

    [Fact]
    public async Task ListFilesAsync_PagesUntilHasMoreIsFalseAndMapsFiles()
    {
        var requestedUris = new List<string>();
        var client = CreateCloudClient(request =>
        {
            requestedUris.Add(request.RequestUri!.ToString());
            return request.RequestUri.Query.Contains("offset=500")
                ? Json("{\"files\":[{\"file_id\":\"f2\",\"filename\":\"SecRandom-cloud-b-p01of01.srpart\",\"size\":20}],\"has_more\":false}")
                : Json("{\"files\":[{\"file_id\":\"f1\",\"file_document_id\":\"d1\",\"filename\":\"SecRandom-cloud-a-manifest.json\",\"size\":10,\"created_at\":\"2026-08-30T12:00:00Z\"}],\"has_more\":true}");
        });

        var files = await client.ListFilesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, files.Count);
        Assert.Equal("f1", files[0].FileId);
        Assert.Equal("d1", files[0].FileDocumentId);
        Assert.Equal("SecRandom-cloud-a-manifest.json", files[0].FileName);
        Assert.Equal(20, files[1].Size);
        Assert.Null(files[1].CreatedAt);
        Assert.Equal(2, requestedUris.Count);
        Assert.All(requestedUris, uri => Assert.Contains($"client_id={ClientId}", uri));
        Assert.Contains("offset=500", requestedUris[1]);
    }

    [Fact]
    public async Task UploadAsync_SendsBase64PartWithPlatformScope()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var client = CreateCloudClient(request =>
        {
            captured = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("{\"success\":true,\"file_id\":\"fid\",\"filename\":\"part.srpart\",\"size\":4}");
        });

        var uploaded = await client.UploadAsync("part.srpart", "application/octet-stream", [1, 2, 3, 4],
            TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"https://appwrite.sectl.cn/api/cloud/upload?client_id={ClientId}", captured.RequestUri!.ToString());
        Assert.Equal("Bearer access-token", captured.Headers.Authorization?.ToString());

        var payload = JsonNode.Parse(capturedBody!)!;
        Assert.Equal(ClientId, payload["client_id"]!.GetValue<string>());
        Assert.Equal("part.srpart", payload["file"]!["name"]!.GetValue<string>());
        Assert.Equal(4, payload["file"]!["size"]!.GetValue<int>());
        Assert.StartsWith("data:application/octet-stream;base64,", payload["file"]!["data"]!.GetValue<string>());

        Assert.Equal("fid", uploaded.FileId);
        Assert.Equal(4, uploaded.Size);
    }

    [Fact]
    public async Task UploadAsync_RejectsAPartLargerThanTheUploadLimit()
    {
        var requests = 0;
        var client = CreateCloudClient(_ =>
        {
            requests++;
            return Json("{}");
        });

        var exception = await Assert.ThrowsAsync<SectlCloudStorageException>(() =>
            client.UploadAsync("part.srpart", "application/octet-stream", new byte[5 * 1024 * 1024],
                TestContext.Current.CancellationToken));

        Assert.Equal("payload_too_large", exception.Code);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task DownloadAsync_ResolvesTheShortLivedUrlBeforeFetchingBytes()
    {
        var captured = new List<HttpRequestMessage>();
        var bytes = new byte[] { 9, 8, 7 };
        var client = CreateCloudClient(request =>
        {
            captured.Add(request);
            return request.RequestUri!.AbsolutePath.EndsWith("/download", StringComparison.Ordinal)
                ? Json("{\"download_url\":\"https://storage.example/token/abc\",\"expires_at\":\"2026-08-30T12:05:00Z\"}")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });

        var downloaded = await client.DownloadAsync("f1", TestContext.Current.CancellationToken);

        Assert.Equal(bytes, downloaded);
        Assert.Equal(2, captured.Count);
        Assert.Equal("Bearer access-token", captured[0].Headers.Authorization?.ToString());
        Assert.Equal("https://storage.example/token/abc", captured[1].RequestUri!.ToString());
        Assert.Null(captured[1].Headers.Authorization);
    }

    [Fact]
    public async Task DeleteAsync_SendsPlatformScopedBody()
    {
        string? capturedBody = null;
        HttpMethod? method = null;
        string? uri = null;
        var client = CreateCloudClient(request =>
        {
            method = request.Method;
            uri = request.RequestUri!.ToString();
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("{\"success\":true}");
        });

        await client.DeleteAsync("f1", "d1", TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Delete, method);
        Assert.Equal($"https://appwrite.sectl.cn/api/cloud/files/f1?client_id={ClientId}", uri);
        var payload = JsonNode.Parse(capturedBody!)!;
        Assert.Equal(ClientId, payload["client_id"]!.GetValue<string>());
        Assert.Equal("d1", payload["file_document_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetUsageAsync_MapsQuotaFields()
    {
        var client = CreateCloudClient(_ => Json(
            "{\"used_storage\":100,\"total_storage\":1000,\"available_storage\":900,\"percentage\":10,\"file_count\":3,\"platform_used_storage\":40,\"platform_file_count\":2}"));

        var quota = await client.GetUsageAsync(TestContext.Current.CancellationToken);

        Assert.Equal(100, quota.Used);
        Assert.Equal(1000, quota.Total);
        Assert.Equal(900, quota.Available);
        Assert.Equal(3, quota.FileCount);
        Assert.Equal(40, quota.PlatformUsed);
        Assert.Equal(2, quota.PlatformFileCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "", "payload_too_large")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "{\"error\":\"storage_exceeded\",\"error_description\":\"Storage quota exceeded\"}", "storage_exceeded")]
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":\"cloud_service_disabled\",\"error_description\":\"该平台未启用云服务\"}", "cloud_service_disabled")]
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":\"insufficient_scope\",\"error_description\":\"Missing cloud:write permission\"}", "insufficient_scope")]
    [InlineData(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_token\",\"error_description\":\"The access token is invalid or has expired\"}", "invalid_token")]
    [InlineData(HttpStatusCode.InternalServerError, "{\"error\":\"internal_error\",\"error_description\":\"boom\"}", "internal_error")]
    [InlineData(HttpStatusCode.InternalServerError, "{}", "cloud_timeout")]
    [InlineData(HttpStatusCode.InternalServerError, "not-json", "cloud_timeout")]
    [InlineData(HttpStatusCode.InternalServerError, "{\"type\":\"general_unknown\",\"message\":\"boom\"}", "cloud_timeout")]
    public async Task ServiceErrors_KeepTheServiceErrorCode(HttpStatusCode status, string body, string expectedCode)
    {
        var client = CreateCloudClient(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

        var exception = await Assert.ThrowsAsync<SectlCloudStorageException>(() =>
            client.GetUsageAsync(TestContext.Current.CancellationToken));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task GatewayTimeout_ReportsStatusAndEmptyBodyForDiagnosis()
    {
        var client = CreateCloudClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });

        var exception = await Assert.ThrowsAsync<SectlCloudStorageException>(() =>
            client.GetUsageAsync(TestContext.Current.CancellationToken));

        Assert.Equal("cloud_timeout", exception.Code);
        Assert.Contains("HTTP 500", exception.Message);
        Assert.Contains("空响应", exception.Message);
    }

    [Fact]
    public async Task CloudRequests_WhenSignedOut_FailWithASignInMessage()
    {
        var client = CreateCloudClient(_ => Json("{}"), signedIn: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ListFilesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CloudRequests_WhenTheAccessTokenIsRejected_RefreshAndRetryOnce()
    {
        var tokens = new List<string?>();
        var listAttempts = 0;
        var client = CreateCloudClient(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/oauth/refresh")
                return Json("{\"access_token\":\"refreshed-access-token\",\"refresh_token\":\"refreshed-refresh-token\",\"user_id\":\"user-1\",\"expires_in\":3600}");
            if (request.RequestUri.Host == "uapis.cn")
                return Json("{\"ip\":\"203.0.113.10\"}");

            tokens.Add(request.Headers.Authorization?.Parameter);
            listAttempts++;
            return listAttempts == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("{\"error\":\"invalid_token\"}", Encoding.UTF8, "application/json")
                }
                : Json("{\"files\":[],\"has_more\":false}");
        });

        var files = await client.ListFilesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(files);
        Assert.Equal(["access-token", "refreshed-access-token"], tokens);
    }

    private static SectlCloudStorageClient CreateCloudClient(
        Func<HttpRequestMessage, HttpResponseMessage> send, bool signedIn = true)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(send))
        {
            BaseAddress = new Uri("https://appwrite.sectl.cn/")
        };
        var factory = new StubHttpClientFactory(httpClient);
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(new MainConfigModel()));
        var deviceUuidStore = new DeviceUuidStore(configHandler, NullLogger<DeviceUuidStore>.Instance);
        var authService = new SectlAuthService(factory, deviceUuidStore);
        if (signedIn)
            SetToken(authService, new SectlToken("access-token", "refresh-token", "user-1", 3600));
        return new SectlCloudStorageClient(authService, factory, NullLogger<SectlCloudStorageClient>.Instance);
    }

    private static void SetToken(SectlAuthService service, SectlToken token)
    {
        var field = typeof(SectlAuthService).GetField("_token", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(service, token);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(send(request));
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) { }
        public override void DeleteConfig<T>(T value) { }
    }
}
