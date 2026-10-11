using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Models;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.Auth;
using SecRandom.Services.Config;
using SecRandom.Services.ControlNode;
using SecRandom.Services.ControlPlane;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Core.Tests;

/// <summary>
///     自建集控接入（接入码 ⇒ 节点令牌）的存储、接入请求形状与两路 Bearer 凭据选择。
/// </summary>
/// <remarks>
///     <para>
///         这里钉住的是本功能的**安全边界**，不是"按钮能不能点"：
///         ①令牌只以密文落在 <c>data/config/security</c> 下，明文在任何文件里都搜不到；
///         ②接入成功后 REST 与节点通道出示的都是节点令牌，SECTL 账号令牌一个字节都不再发出去；
///         ③没有接入时两路都逐字回退到今天的行为（线上用户感知不到这次改动）。
///     </para>
///     <para>
///         节点通道那一侧的"出示哪个凭据"落在 <see cref="ControlNodeCredentialProvider" />，
///         REST 那一侧落在 <see cref="ControlPlaneClient" /> 之前的那层
///         <c>IAuthorizedApiSender</c>（装饰实现是 internal，因此这里用真实的
///         <see cref="ControlPlaneClient" /> 打一条真请求来观察它最终带出去的头）。
///     </para>
/// </remarks>
public sealed class NodeEnrollmentTests : IDisposable
{
    private const string NodeToken = "srn_node7_secret-value-that-must-never-leak";
    private const string SectlAccessToken = "sectl-access-token-must-not-be-sent-when-enrolled";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "SecRandom", "node-enrollment-tests", Guid.NewGuid().ToString("N"));

    private string EnrollmentPath => Path.Combine(_directory, "control-node.json");

    private string KeyPath => Path.Combine(_directory, "control-node.key");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清不掉不该让测试变红。
        }
    }

    // ---------------------------------------------------------------- ①加密存储

    [Fact]
    public void 令牌只以密文落盘_明文在任何文件里都搜不到()
    {
        var store = CreateStore();
        Assert.False(store.TryGetAccessToken(out var before), "全新存储不该有令牌。");
        Assert.Null(before);

        Assert.True(store.Save(Record()), $"save failed; dir={_directory} exists={Directory.Exists(_directory)}");

        // 状态可读、令牌可取：功能是通的。
        Assert.True(store.Status.HasToken);
        Assert.Equal("node-7", store.Status.NodeId);
        Assert.Equal("grp_1", store.Status.GroupId);
        Assert.Equal("高一（1）班", store.Status.GroupName);
        Assert.True(store.TryGetAccessToken(out var token));
        Assert.Equal(NodeToken, token);

        // 而落盘的东西里一个明文字节都不许有：令牌、节点 id、组 id 全在 AES-GCM 密文里。
        foreach (var file in Directory.GetFiles(_directory))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(NodeToken, text, StringComparison.Ordinal);
            Assert.DoesNotContain("node-7", text, StringComparison.Ordinal);
            Assert.DoesNotContain("grp_1", text, StringComparison.Ordinal);
        }

        // 信封是显式格式版本 + Base64 三段，将来换格式必须显式升版本。
        using var envelope = JsonDocument.Parse(File.ReadAllText(EnrollmentPath));
        Assert.Equal(1, envelope.RootElement.GetProperty("FormatVersion").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(envelope.RootElement.GetProperty("Nonce").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(envelope.RootElement.GetProperty("Tag").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(envelope.RootElement.GetProperty("Ciphertext").GetString()));

        // 密钥文件与令牌文件同目录、不同文件：这是"冷启动不需要安全密码"的前提。
        Assert.True(File.Exists(EnrollmentPath));
        Assert.True(File.Exists(KeyPath));
    }

    [Fact]
    public void 读回来的是同一份接入信息_接口本身不给令牌真值()
    {
        Assert.True(CreateStore().Save(Record()));

        var reopened = CreateStore();
        Assert.True(reopened.Status.HasToken);
        Assert.Equal("node-7", reopened.Status.NodeId);
        Assert.False(reopened.Status.IsUnreadable);
        Assert.True(reopened.Status.ExpiresAt > DateTimeOffset.UtcNow);

        // 对外状态类型里根本没有放令牌的属性：界面/诊断想回显都写不出来。
        var statusProperties = typeof(NodeEnrollmentStatus).GetProperties().Select(property => property.Name).ToList();
        Assert.DoesNotContain("NodeToken", statusProperties);
        Assert.DoesNotContain("Token", statusProperties);
    }

    [Fact]
    public void 记录被改坏时按未接入处理并清掉_绝不崩也不留着重试()
    {
        Assert.True(CreateStore().Save(Record()));

        // 模拟"文件被改过/换了机器"：密文保持合法 Base64，内容却不是同一把密钥能解开的。
        using (var document = JsonDocument.Parse(File.ReadAllText(EnrollmentPath)))
        {
            var tampered = JsonSerializer.Serialize(new
            {
                FormatVersion = 1,
                Nonce = document.RootElement.GetProperty("Nonce").GetString(),
                Tag = document.RootElement.GetProperty("Tag").GetString(),
                Ciphertext = Convert.ToBase64String(Encoding.UTF8.GetBytes("not-the-real-ciphertext"))
            });
            File.WriteAllText(EnrollmentPath, tampered);
        }

        var store = CreateStore();

        Assert.False(store.Status.HasToken);
        Assert.True(store.Status.IsUnreadable);
        Assert.False(store.TryGetAccessToken(out var token));
        Assert.Null(token);

        // 坏记录必须就地清掉：留着它每次启动都要失败一次，而用户只看到"连不上"。
        Assert.False(File.Exists(EnrollmentPath));
        Assert.False(File.Exists(KeyPath));

        // 清掉之后是可恢复的：重新接入一次就回到正常状态。
        Assert.True(store.Save(Record()));
        Assert.True(store.Status.HasToken);
    }

    [Fact]
    public void 清除接入会把状态与文件一起复位()
    {
        var store = CreateStore();
        Assert.True(store.Save(Record()));

        Assert.True(store.Clear());
        Assert.False(store.Status.HasToken);
        Assert.False(store.Status.IsUnreadable);
        Assert.False(store.TryGetAccessToken(out _));
        Assert.False(File.Exists(EnrollmentPath));
        Assert.False(File.Exists(KeyPath));

        // 再清一次没有东西可清。
        Assert.False(store.Clear());
    }

    [Fact]
    public void 本地看已过期时状态说已过期但令牌照样交出去()
    {
        var store = CreateStore();
        Assert.True(store.Save(Record() with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }));

        Assert.True(store.Status.HasToken);
        Assert.True(store.Status.IsExpired);

        // 本地时钟不可信：过期与否由服务端说了算，客户端单方面断连会把只是时钟偏了的教室机踢下线。
        Assert.True(store.TryGetAccessToken(out var token));
        Assert.Equal(NodeToken, token);
    }

    // ---------------------------------------------------------------- 接入码 ⇒ 令牌

    [Fact]
    public async Task 接入请求按服务端契约发出_响应字段一一对上()
    {
        var handler = new RecordingHandler(_ =>
            Json(HttpStatusCode.OK, $$"""
            { "node_id": "node-9", "group_id": "grp_9", "group_name": "高三（2）班",
              "node_token": "{{NodeToken}}", "expires_at": "2030-01-02T03:04:05Z" }
            """));
        var client = new NodeEnrollmentClient(new FakeHttpClientFactory(handler), new FakeEndpointStore("https://control.example/"));

        var record = await client.EnrollAsync(
            " 7K3M-9QZX ",
            nodeId: "local-node",
            platform: "windows",
            version: "3.1.2",
            displayName: "教室机 1",
            capabilities: [ControlCapabilities.DrawTrigger],
            cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://control.example/v1/node/enroll", request.Uri.AbsoluteUri);

        // 接入是匿名接口：这里**必须**没有 Authorization 头，否则用户会被"还没登录"卡在门外。
        Assert.Null(request.Authorization);

        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("7K3M-9QZX", root.GetProperty("code").GetString());          // 两端空白要剪掉
        Assert.Equal("local-node", root.GetProperty("node_id").GetString());
        Assert.Equal("windows", root.GetProperty("platform").GetString());
        Assert.Equal("3.1.2", root.GetProperty("version").GetString());
        Assert.Equal("教室机 1", root.GetProperty("display_name").GetString());
        Assert.Equal(ControlCapabilities.DrawTrigger, root.GetProperty("capabilities")[0].GetString());

        Assert.Equal("node-9", record.NodeId);
        Assert.Equal("grp_9", record.GroupId);
        Assert.Equal("高三（2）班", record.GroupName);
        Assert.Equal(NodeToken, record.NodeToken);
        Assert.Equal(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero), record.ExpiresAt);
    }

    [Fact]
    public async Task 不填的字段不出现_而不是发一个空串()
    {
        var handler = new RecordingHandler(_ =>
            Json(HttpStatusCode.OK, $$"""{ "node_id": "n", "group_id": "g", "node_token": "{{NodeToken}}" }"""));
        var client = new NodeEnrollmentClient(new FakeHttpClientFactory(handler), new FakeEndpointStore("https://control.example"));

        await client.EnrollAsync("ABC-123", nodeId: "  ", displayName: null, cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.False(body.RootElement.TryGetProperty("node_id", out _));
        Assert.False(body.RootElement.TryGetProperty("display_name", out _));
        Assert.False(body.RootElement.TryGetProperty("capabilities", out _));
    }

    [Theory]
    // 状态码只说大类，具体下一步由响应体里的 code 决定。
    [InlineData(401, "enrollment_code_invalid", NodeEnrollmentFailure.CodeInvalid)]
    [InlineData(400, "invalid_request", NodeEnrollmentFailure.InvalidRequest)]
    [InlineData(410, "enrollment_code_expired", NodeEnrollmentFailure.CodeExpired)]
    [InlineData(410, "enrollment_code_used", NodeEnrollmentFailure.CodeUsed)]
    [InlineData(410, "enrollment_code_revoked", NodeEnrollmentFailure.CodeRevoked)]
    [InlineData(429, "too_many_attempts", NodeEnrollmentFailure.TooManyAttempts)]
    [InlineData(503, "enrollment_disabled", NodeEnrollmentFailure.Disabled)]
    // 网关可能只给状态码：这时按状态码兜底，不能让界面显示一个说不清的失败。
    [InlineData(401, null, NodeEnrollmentFailure.CodeInvalid)]
    [InlineData(410, null, NodeEnrollmentFailure.CodeExpired)]
    [InlineData(503, null, NodeEnrollmentFailure.Disabled)]
    [InlineData(500, null, NodeEnrollmentFailure.Unknown)]
    public async Task 失败按服务端代码分类_且异常里不含接入码与令牌(
        int status, string? serverCode, NodeEnrollmentFailure expected)
    {
        var body = serverCode is null ? "{}" : $$"""{ "code": "{{serverCode}}" }""";
        var handler = new RecordingHandler(_ => Json((HttpStatusCode)status, body));
        var client = new NodeEnrollmentClient(new FakeHttpClientFactory(handler), new FakeEndpointStore("https://control.example"));

        var exception = await Assert.ThrowsAsync<NodeEnrollmentException>(
            () => client.EnrollAsync("SECRET-CODE-7K3M", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(expected, exception.Failure);
        Assert.Equal(serverCode, exception.ServerCode);
        Assert.Equal(status, exception.StatusCode);

        // 异常会被日志与诊断包带走：里面绝不许出现接入码。
        Assert.DoesNotContain("SECRET-CODE-7K3M", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 网络不通要说网络_而不是把好码说成坏码()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection refused"));
        var client = new NodeEnrollmentClient(new FakeHttpClientFactory(handler), new FakeEndpointStore("https://control.example"));

        var exception = await Assert.ThrowsAsync<NodeEnrollmentException>(
            () => client.EnrollAsync("ABC-123", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(NodeEnrollmentFailure.Network, exception.Failure);
    }

    [Fact]
    public async Task 令牌前缀不对就是无效响应_绝不把没前缀的东西存下来()
    {
        var handler = new RecordingHandler(_ =>
            Json(HttpStatusCode.OK, """{ "node_id": "n", "group_id": "g", "node_token": "not-a-node-token" }"""));
        var client = new NodeEnrollmentClient(new FakeHttpClientFactory(handler), new FakeEndpointStore("https://control.example"));

        var exception = await Assert.ThrowsAsync<NodeEnrollmentException>(
            () => client.EnrollAsync("ABC-123", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(NodeEnrollmentFailure.InvalidResponse, exception.Failure);
    }

    [Fact]
    public void 粘进来的已经是一个令牌时不再换一次()
    {
        // 一个班几十台机器复制粘贴同一个令牌是运维常态：再换一次会作废掉旧令牌，把先接好的机器踢下线。
        Assert.True(NodeEnrollmentClient.LooksLikeNodeToken("  srn_a_b  "));
        Assert.False(NodeEnrollmentClient.LooksLikeNodeToken("7K3M-9QZX"));
        Assert.False(NodeEnrollmentClient.LooksLikeNodeToken(null));
    }

    [Fact]
    public void 接入地址跟着控制面基址走_自建部署填一个地址就够()
    {
        Assert.Equal("https://control.example/v1/node/enroll", NodeEnrollmentClient.BuildEnrollUri("https://control.example/"));
        Assert.Equal("http://127.0.0.1:8080/v1/node/enroll", NodeEnrollmentClient.BuildEnrollUri("http://127.0.0.1:8080"));
        Assert.Equal("/v1/node/enroll", NodeEnrollmentClient.BuildEnrollUri("  "));
    }

    // ---------------------------------------------------------------- ①②两路凭据

    [Fact]
    public async Task 已接入时REST只带节点令牌_从不带SECTL账号令牌()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "[]"));
        var fake = new FakeEnrollmentStore();
        Assert.True(fake.Save(Record()));
        var sender = new EnrolledAuthorizedApiSender(new ForbiddenFallbackSender(), fake, new FakeHttpClientFactory(handler));

        // 用真实的 ControlPlaneClient 打一条真请求：节点通道之外的 REST 走的就是这条路。
        var client = new ControlPlaneClient(sender, NullLogger<ControlPlaneClient>.Instance, "https://control.example");
        await client.GetGroupsAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"Bearer {NodeToken}", request.Authorization);
        Assert.DoesNotContain(SectlAccessToken, request.Authorization ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 未接入时REST逐字走回原通道_线上用户感知不到改动()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "[]"));
        var fallback = new RecordingFallbackSender(() => Json(HttpStatusCode.OK, "[]"));

        // 没有接入记录 = 今天的行为。
        var sender = new EnrolledAuthorizedApiSender(fallback, new FakeEnrollmentStore(), new FakeHttpClientFactory(handler));
        var client = new ControlPlaneClient(sender, NullLogger<ControlPlaneClient>.Instance, "https://control.example");
        await client.GetGroupsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, fallback.Calls);
        Assert.NotNull(fallback.LastRequest);
        Assert.Equal($"Bearer {SectlAccessToken}", fallback.LastAuthorization);

        // 关键：这条路一个请求都没经过"节点令牌"那条通道，行为与改动前逐字一致。
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task 清除接入后下一个请求立刻回到原通道_不用重启()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "[]"));
        var fallback = new RecordingFallbackSender(() => Json(HttpStatusCode.OK, "[]"));
        var store = new FakeEnrollmentStore();
        Assert.True(store.Save(Record()));

        var sender = new EnrolledAuthorizedApiSender(fallback, store, new FakeHttpClientFactory(handler));
        var client = new ControlPlaneClient(sender, NullLogger<ControlPlaneClient>.Instance, "https://control.example");

        await client.GetGroupsAsync(TestContext.Current.CancellationToken);
        Assert.Single(handler.Requests);
        Assert.Equal($"Bearer {NodeToken}", handler.Requests[0].Authorization);
        Assert.Equal(0, fallback.Calls);

        store.Clear();

        await client.GetGroupsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, fallback.Calls);
        Assert.Equal($"Bearer {SectlAccessToken}", fallback.LastAuthorization);

        // 清除之后不再有任何请求带节点令牌出去。
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task 已接入时节点通道出示节点令牌()
    {
        var fake = new FakeEnrollmentStore();
        Assert.True(fake.Save(Record()));

        var provider = new ControlNodeCredentialProvider(CreateAuthService(), fake);

        // 没登录也必须拿得到令牌：这正是"不登录 SECTL 也能用自建集控"。
        Assert.Equal(NodeToken, await provider.TryGetAccessTokenAsync(false, CancellationToken.None));
    }

    [Fact]
    public async Task 未接入且未登录时节点通道拿不到凭据_客户端该等待而不是拿空token去撞()
    {
        var provider = new ControlNodeCredentialProvider(CreateAuthService(), new FakeEnrollmentStore());

        Assert.Null(await provider.TryGetAccessTokenAsync(false, CancellationToken.None));
    }

    /// <summary>一个**没登录**的真实认证服务：节点通道的凭据选择逻辑要能在它面前证明自己不依赖登录。</summary>
    private static SectlAuthService CreateAuthService()
    {
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(new MainConfigModel()));

        return new SectlAuthService(
            TestTokenStore.Create(),
            new FakeHttpClientFactory(new RecordingHandler(_ => Json(HttpStatusCode.OK, "{}"))),
            new DeviceUuidStore(configHandler, NullLogger<DeviceUuidStore>.Instance),
            NullLogger<SectlAuthService>.Instance,
            new LoopbackAuthRedirectBrokerFactory());
    }

    // ---------------------------------------------------------------- 测试替身

    private FileNodeEnrollmentStore CreateStore() =>
        new(EnrollmentPath, NullLogger<FileNodeEnrollmentStore>.Instance);

    private static NodeEnrollmentRecord Record() => new()
    {
        NodeId = "node-7",
        GroupId = "grp_1",
        GroupName = "高一（1）班",
        NodeToken = NodeToken,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record SentRequest(HttpMethod Method, Uri Uri, string Body, string? Authorization);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly List<SentRequest> _requests = [];
        private readonly object _gate = new();

        public IReadOnlyList<SentRequest> Requests
        {
            get
            {
                lock (_gate)
                {
                    return _requests.ToList();
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            lock (_gate)
            {
                _requests.Add(new SentRequest(
                    request.Method,
                    request.RequestUri!,
                    body,
                    request.Headers.Authorization?.ToString()));
            }

            return responder(request);
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeEndpointStore : IControlPlaneEndpointStore
    {
        private readonly string _defaultBaseUrl;

        public FakeEndpointStore(string baseUrl)
        {
            _defaultBaseUrl = baseUrl;
            Current = baseUrl;
        }

        public string Current { get; private set; }

        public bool IsCustom => true;

        public event EventHandler<string>? Changed;

        public bool TryUpdate(string? endpoint, out string? error)
        {
            error = null;
            Current = endpoint ?? _defaultBaseUrl;
            Changed?.Invoke(this, Current);
            return true;
        }

        public void ResetToDefault() => Current = _defaultBaseUrl;
    }

    /// <summary>内存版接入存储：用来把"已接入/未接入"两种世界摆出来。</summary>
    private sealed class FakeEnrollmentStore : INodeEnrollmentStore
    {
        private readonly object _gate = new();
        private NodeEnrollmentRecord? _record;
        private NodeEnrollmentStatus _status = NodeEnrollmentStatus.NotEnrolled;

        public NodeEnrollmentStatus Status
        {
            get
            {
                lock (_gate)
                {
                    return _status;
                }
            }
        }

        public int Generation { get; private set; }

        public event EventHandler<NodeEnrollmentStatus>? Changed;

        public bool TryGetAccessToken(out string? accessToken)
        {
            lock (_gate)
            {
                accessToken = _record?.NodeToken;
                return _record is not null;
            }
        }

        public bool Save(NodeEnrollmentRecord record)
        {
            NodeEnrollmentStatus status;
            lock (_gate)
            {
                _record = record;
                _status = new NodeEnrollmentStatus
                {
                    HasToken = true,
                    NodeId = record.NodeId,
                    GroupId = record.GroupId,
                    GroupName = record.GroupName,
                    ExpiresAt = record.ExpiresAt
                };
                status = _status;
                Generation++;
            }

            Changed?.Invoke(this, status);
            return true;
        }

        public bool Clear()
        {
            lock (_gate)
            {
                var had = _record is not null;
                _record = null;
                _status = NodeEnrollmentStatus.NotEnrolled;
                Generation++;
                if (!had)
                    return false;
            }

            Changed?.Invoke(this, NodeEnrollmentStatus.NotEnrolled);
            return true;
        }
    }

    /// <summary>未接入时若被调用就说明行为变了：这条通道绝不该在已接入时被走到。</summary>
    private sealed class ForbiddenFallbackSender : IAuthorizedApiSender
    {
        public Task<HttpResponseMessage> SendAuthorizedAsync(
            Func<HttpRequestMessage> createRequest,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
            CancellationToken cancellationToken = default) =>
            throw new Xunit.Sdk.XunitException("已接入时不得再走 SECTL 账号通道。");
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;

        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;

        public override void SaveConfig<T>(T value)
        {
        }

        public override void DeleteConfig<T>(T value)
        {
        }
    }

    /// <summary>记录"回退通道被走了几次"，并证明请求确实交给了它。</summary>
    /// <remarks>
    ///     它自己造一个**不记录**的 handler：回退请求不该出现在记录里，否则"哪些请求走了节点令牌"
    ///     这个断言就分不清是谁发的了。
    /// </remarks>
    private sealed class RecordingFallbackSender : IAuthorizedApiSender
    {
        private readonly Func<HttpResponseMessage> _respond;

        public RecordingFallbackSender(Func<HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public int Calls { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastAuthorization { get; private set; }

        public Task<HttpResponseMessage> SendAuthorizedAsync(
            Func<HttpRequestMessage> createRequest,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var request = createRequest();
            LastRequest = request;

            // 真实实现会在这里附上 SECTL 的 Bearer；测试里只关心"请求走到这儿了"。
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SectlAccessToken);
            LastAuthorization = request.Headers.Authorization.ToString();
            return Task.FromResult(_respond());
        }
    }
}
