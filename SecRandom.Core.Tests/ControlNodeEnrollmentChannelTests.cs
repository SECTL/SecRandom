using System.Net;
using System.Text;
using System.Text.Json;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.ControlNode;
using SecRandom.Services.ControlPlane;

namespace SecRandom.Core.Tests;

/// <summary>
///     集控接入（v4.1）的传输层测试：地点 ID 归一化、两因子接入、错误码映射、设备树与分班。
/// </summary>
public sealed class ControlNodeEnrollmentChannelTests
{
    private static readonly NodeDeviceCredential Credential = new("dev-1", "secret-1", "SC-K7Q2M9", "第一实验学校");

    [Theory]
    [InlineData("k7q2m9", "SC-K7Q2M9")]
    [InlineData("SC-K7Q2M9", "SC-K7Q2M9")]
    [InlineData(" sc k7q2m9 ", "SC-K7Q2M9")]
    [InlineData("SCK7Q2M9", "SC-K7Q2M9")]
    public void 地点ID统一成SC加六位(string input, string expected)
    {
        Assert.Equal(expected, NodeEnrollmentClient.NormalizeSiteId(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SC-K7Q")]
    [InlineData("SC-K7Q2M9X")]
    [InlineData("SC-K7Q2M!")]
    public void 非法地点ID返回null(string? input)
    {
        Assert.Null(NodeEnrollmentClient.NormalizeSiteId(input));
    }

    [Theory]
    [InlineData("abcd7kq2", "ABCD7KQ2")]
    [InlineData("ABCD-7KQ2", "ABCD7KQ2")]
    [InlineData("abcd 7kq2", "ABCD7KQ2")]
    public void 配对码忽略连字符与大小写(string input, string expected)
    {
        Assert.Equal(expected, NodeEnrollmentClient.NormalizePairingCode(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ABCD7KQ")]
    [InlineData("ABCD7KQ2X")]
    [InlineData("ABCD-7KQ!")]
    public void 非法配对码返回null(string? input)
    {
        Assert.Null(NodeEnrollmentClient.NormalizePairingCode(input));
    }

    [Fact]
    public void 配对码按四位一组显示()
    {
        Assert.Equal("ABCD-7KQ2", NodeEnrollmentClient.FormatPairingCode("abcd7kq2"));
        Assert.Equal("ABCD-7KQ2", NodeEnrollmentClient.FormatPairingCode("ABCD7KQ2"));
    }

    [Fact]
    public async Task 接入成功解析设备凭据与站点信息()
    {
        var handler = new FakeHandler(request =>
        {
            Assert.Equal("/control/api/enroll", request.RequestUri!.AbsolutePath);
            var body = JsonDocument.Parse(ReadBody(request)).RootElement;
            // 断言里带上原始 body：字段名一旦改错，KeyNotFound 只会说"key 不存在"，看不到实际发的是什么。
            Assert.True(body.TryGetProperty("siteId", out var siteIdProp) && siteIdProp.GetString() == "SC-K7Q2M9",
                $"body={body}");
            Assert.True(body.TryGetProperty("code", out var codeProp) && codeProp.GetString() == "ABCD7KQ2",
                $"body={body}");
            Assert.True(body.TryGetProperty("platform", out var platformProp) && platformProp.GetString() == "secrandom",
                $"body={body}");
            Assert.True(
                body.TryGetProperty("machine", out var machineProp)
                && machineProp.TryGetProperty("host", out var hostProp)
                && !string.IsNullOrWhiteSpace(hostProp.GetString()),
                $"body={body}");

            return Json(HttpStatusCode.Created, """
                                                {
                                                  "deviceId": "dev-1",
                                                  "secret": "secret-1",
                                                  "site": { "publicId": "SC-K7Q2M9", "name": "第一实验学校" },
                                                  "class": { "id": "class-1", "name": "一班" }
                                                }
                                                """);
        });

        var client = CreateClient(handler);
        var record = await client.EnrollAsync("k7q2m9", "abcd-7kq2", "讲台机");

        Assert.Equal("dev-1", record.DeviceId);
        Assert.Equal("secret-1", record.Secret);
        Assert.Equal("SC-K7Q2M9", record.SiteId);
        Assert.Equal("第一实验学校", record.SiteName);
        Assert.Equal("class-1", record.ClassId);
        Assert.Equal("一班", record.ClassName);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "site_not_found", NodeEnrollmentFailure.SiteNotFound)]
    [InlineData(HttpStatusCode.NotFound, "code_not_found", NodeEnrollmentFailure.CodeNotFound)]
    [InlineData(HttpStatusCode.Forbidden, "code_site_mismatch", NodeEnrollmentFailure.CodeSiteMismatch)]
    [InlineData(HttpStatusCode.Gone, "code_expired", NodeEnrollmentFailure.CodeExpired)]
    [InlineData(HttpStatusCode.Gone, "code_used", NodeEnrollmentFailure.CodeUsed)]
    [InlineData(HttpStatusCode.Gone, "code_revoked", NodeEnrollmentFailure.CodeRevoked)]
    [InlineData(HttpStatusCode.Forbidden, "platform_not_allowed", NodeEnrollmentFailure.PlatformNotAllowed)]
    [InlineData(HttpStatusCode.TooManyRequests, "too_many_requests", NodeEnrollmentFailure.TooManyAttempts)]
    [InlineData(HttpStatusCode.BadRequest, "bad_request", NodeEnrollmentFailure.InvalidRequest)]
    public async Task 接入错误码映射为明确失败原因(
        HttpStatusCode status,
        string code,
        NodeEnrollmentFailure expected)
    {
        var handler = new FakeHandler(_ =>
            Json(status, JsonSerializer.Serialize(new { code, detail = new { field = "code" } })));

        var exception = await Assert.ThrowsAsync<NodeEnrollmentException>(() =>
            CreateClient(handler).EnrollAsync("SC-K7Q2M9", "ABCD7KQ2", null));

        Assert.Equal(expected, exception.Failure);
        Assert.Equal(code, exception.ServerCode);
        Assert.Equal((int)status, exception.StatusCode);
    }

    [Fact]
    public async Task 设备冲突带回已接入的站点名()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.Conflict, """
                                                            {
                                                              "code": "device_conflict",
                                                              "detail": { "siteId": "SC-AAAAAA", "siteName": "东校区", "deviceId": "dev-1" }
                                                            }
                                                            """));

        var exception = await Assert.ThrowsAsync<NodeEnrollmentException>(() =>
            CreateClient(handler).EnrollAsync("SC-K7Q2M9", "ABCD7KQ2", null));

        Assert.Equal(NodeEnrollmentFailure.DeviceConflict, exception.Failure);
        Assert.Equal("东校区", exception.ConflictSiteName);
    }

    [Fact]
    public async Task 接入响应缺少secret视为无效响应()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.Created, """{ "deviceId": "dev-1" }"""));
        await Assert.ThrowsAsync<NodeEnrollmentException>(() =>
            CreateClient(handler).EnrollAsync("SC-K7Q2M9", "ABCD7KQ2", null));
    }

    [Fact]
    public async Task 设备树解析年级班级与本机分班()
    {
        var handler = new FakeHandler(request =>
        {
            Assert.Equal("/control/api/device/tree", request.RequestUri!.AbsolutePath);
            var body = JsonDocument.Parse(ReadBody(request)).RootElement;
            Assert.Equal("dev-1", body.GetProperty("deviceId").GetString());
            Assert.Equal("secret-1", body.GetProperty("secret").GetString());

            return Json(HttpStatusCode.OK, """
                                           {
                                             "site": { "publicId": "SC-K7Q2M9", "name": "第一实验学校" },
                                             "grades": [
                                               { "id": "g1", "name": "高一", "classes": [
                                                   { "id": "c1", "name": "一班" }, { "id": "c2", "name": "二班" } ] },
                                               { "id": "g2", "name": "高二", "classes": [
                                                   { "id": "c3", "name": "三班" } ] }
                                             ],
                                             "device": { "id": "dev-1", "name": "讲台机", "classId": "c2" }
                                           }
                                           """);
        });

        var tree = await CreateClient(handler).GetDeviceTreeAsync(Credential);

        Assert.Equal("第一实验学校", tree.SiteName);
        Assert.Equal(2, tree.Grades.Count);
        Assert.Equal(["c1", "c2", "c3"], tree.EnumerateClasses().Select(item => item.ClassId));
        Assert.Equal("讲台机", tree.DeviceName);
        Assert.Equal("c2", tree.ClassId);
    }

    [Fact]
    public async Task 设备凭据失效按bad_secret映射()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.Unauthorized, """{ "code": "bad_secret" }"""));

        var exception = await Assert.ThrowsAsync<NodeEnrollmentException>(() =>
            CreateClient(handler).GetDeviceTreeAsync(Credential));

        Assert.Equal(NodeEnrollmentFailure.CodeNotFound, exception.Failure);
        Assert.Equal("bad_secret", exception.ServerCode);
    }

    [Fact]
    public async Task 分班请求携带classId_允许传null表示不分班()
    {
        string? captured = "not-set";
        var handler = new FakeHandler(request =>
        {
            Assert.Equal("/control/api/device/assign", request.RequestUri!.AbsolutePath);
            var body = JsonDocument.Parse(ReadBody(request)).RootElement;
            captured = body.TryGetProperty("classId", out var classId) && classId.ValueKind != JsonValueKind.Null
                ? classId.GetString()
                : null;
            return Json(HttpStatusCode.OK, """{ "ok": true }""");
        });

        await CreateClient(handler).AssignClassAsync(Credential, "c1");
        Assert.Equal("c1", captured);

        await CreateClient(handler).AssignClassAsync(Credential, null);
        Assert.Null(captured);
    }

    [Fact]
    public void 旧版接入地址仅用于兼容提示()
    {
        var uri = NodeEnrollmentClient.BuildEnrollUri("https://sectl.cn");
        Assert.Equal("https://sectl.cn/v1/node/enroll", uri);
        Assert.True(NodeEnrollmentClient.LooksLikeNodeToken("srn_abc"));
    }

    private static NodeEnrollmentClient CreateClient(FakeHandler handler)
    {
        var store = new FakeEndpointStore();
        return new NodeEnrollmentClient(
            new FakeHttpClientFactory(handler), store, "https://sectl.cn");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string ReadBody(HttpRequestMessage request) =>
        request.Content is null
            ? ""
            : new StreamReader(request.Content.ReadAsStream()).ReadToEnd();

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }

    private sealed class FakeEndpointStore : IControlPlaneEndpointStore
    {
        public string Current => "https://sectl.cn";
        public bool IsCustom => false;

        public bool TryUpdate(string? endpoint, out string? error)
        {
            error = null;
            return false;
        }

        public void ResetToDefault()
        {
        }

        public event EventHandler<string>? Changed;
    }
}