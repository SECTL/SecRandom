using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.ControlNode;
using SecRandom.Services.ControlPlane;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Core.Tests;

/// <summary>
///     设备接入记录（地点 ID + 配对码换来的 <c>device_id</c> + <c>secret</c>）的加密存储，
///     以及接入失败时"不把双因子带出日志/异常"的安全边界。
/// </summary>
/// <remarks>
///     <para>
///         v1 的"接入码 ⇒ <c>srn_</c> 令牌"整条路径已经断代（<see cref="NodeEnrollmentClient" /> 上的旧重载
///         全部 <see cref="ObsoleteAttribute">obsolete</see> 并直接抛 <c>Disabled</c>），接入请求本身的形状、
///         错误码映射、设备树与分班由 <see cref="ControlNodeEnrollmentChannelTests" /> 覆盖。
///     </para>
///     <para>
///         这里只保留 v4 下仍然成立、且新接入测试没有覆盖的四条边界：
///         ①<see cref="Secret" /> 与设备/地点身份只以密文落盘，明文在任何落盘文件里都搜不到；
///         ②记录被改坏时就地清掉、回到"需要重新接入"，而不是每次启动都拿一个解不开的东西去撞；
///         ③接入失败的异常里只有服务端错误码，没有配对码、也没有地点 ID；
///         ④传输层失败只说"网络不通"，绝不把用户输入的好码报成坏码。
///     </para>
/// </remarks>
public sealed class NodeEnrollmentTests : IDisposable
{
    private const string DeviceId = "dev_7k3m9qzx";
    private const string Secret = "secret-value-that-must-never-leak";
    private const string SiteId = "SC-7K3M9Q";
    private const string PairingCode = "ABCD-7KQ2";

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
    public void 接入记录只以密文落盘_状态接口也不回显secret()
    {
        var store = CreateStore();
        Assert.False(store.TryGetCredential(out var before), "全新存储不该有凭据。");
        Assert.Null(before);

        // v4 没有 Bearer 令牌：控制面 REST 一律回落到账号令牌，这个接口必须恒为 false。
        Assert.False(store.TryGetAccessToken(out _));

        Assert.True(store.Save(Record()), $"save failed; dir={_directory} exists={Directory.Exists(_directory)}");

        // 状态可读、凭据可取：功能是通的。
        Assert.True(store.Status.HasCredential);
        Assert.False(store.Status.IsUnreadable);
        Assert.Equal(DeviceId, store.Status.DeviceId);
        Assert.Equal(SiteId, store.Status.SiteId);
        Assert.Equal("高一（1）班", store.Status.SiteName);

        Assert.True(store.TryGetCredential(out var credential));
        Assert.NotNull(credential);
        Assert.Equal(DeviceId, credential.DeviceId);
        Assert.Equal(Secret, credential.Secret);
        Assert.Equal(SiteId, credential.SiteId);

        // 而落盘的东西里一个明文字节都不许有：secret、设备 id、地点 id 全在 AES-GCM 密文里。
        foreach (var file in Directory.GetFiles(_directory))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
            Assert.DoesNotContain(DeviceId, text, StringComparison.Ordinal);
            Assert.DoesNotContain(SiteId, text, StringComparison.Ordinal);
        }

        // 信封是显式格式版本 + Base64 三段，将来换格式必须显式升版本。
        using var envelope = JsonDocument.Parse(File.ReadAllText(EnrollmentPath));
        Assert.Equal(2, envelope.RootElement.GetProperty("FormatVersion").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(envelope.RootElement.GetProperty("Nonce").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(envelope.RootElement.GetProperty("Tag").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(envelope.RootElement.GetProperty("Ciphertext").GetString()));

        // 密钥文件与记录文件同目录、不同文件：这是"冷启动不需要安全密码"的前提。
        Assert.True(File.Exists(EnrollmentPath));
        Assert.True(File.Exists(KeyPath));

        // 对外状态类型里根本没有放 secret 的属性：界面/诊断想回显都写不出来。
        var statusProperties = typeof(NodeEnrollmentStatus).GetProperties()
            .Select(property => property.Name)
            .ToList();
        Assert.DoesNotContain("Secret", statusProperties);
        Assert.DoesNotContain("NodeToken", statusProperties);
        Assert.DoesNotContain("Token", statusProperties);
    }

    // ---------------------------------------------------------------- ②坏记录

    [Fact]
    public void 记录被改坏时按未接入处理并清掉_绝不崩也不留着重试()
    {
        Assert.True(CreateStore().Save(Record()));

        // 模拟"文件被改过/换了机器"：密文保持合法 Base64，内容却不是同一把密钥能解开的。
        using (var document = JsonDocument.Parse(File.ReadAllText(EnrollmentPath)))
        {
            var tampered = JsonSerializer.Serialize(new
            {
                FormatVersion = 2,
                Nonce = document.RootElement.GetProperty("Nonce").GetString(),
                Tag = document.RootElement.GetProperty("Tag").GetString(),
                Ciphertext = Convert.ToBase64String(Encoding.UTF8.GetBytes("not-the-real-ciphertext"))
            });
            File.WriteAllText(EnrollmentPath, tampered);
        }

        var store = CreateStore();

        Assert.False(store.Status.HasCredential);
        Assert.True(store.Status.IsUnreadable);
        Assert.False(store.TryGetCredential(out var credential));
        Assert.Null(credential);

        // 坏记录必须就地清掉：留着它每次启动都要失败一次，而用户只看到"连不上"。
        Assert.False(File.Exists(EnrollmentPath));
        Assert.False(File.Exists(KeyPath));

        // 清掉之后是可恢复的：重新接入一次就回到正常状态。
        Assert.True(store.Save(Record()));
        Assert.True(store.Status.HasCredential);
    }

    // ---------------------------------------------------------------- ③失败不外泄双因子

    [Fact]
    public async Task 接入失败的异常不带出地点ID与配对码()
    {
        var handler = new RecordingHandler(_ =>
            Json(HttpStatusCode.NotFound, """{ "code": "code_not_found" }"""));
        var client = new NodeEnrollmentClient(
            new FakeHttpClientFactory(handler),
            new FakeEndpointStore("https://control.example/"));

        var exception = await Assert.ThrowsAsync<NodeEnrollmentException>(() => client.EnrollAsync(
            SiteId,
            PairingCode,
            deviceName: "教室机 1",
            cancellationToken: TestContext.Current.CancellationToken));

        // 状态码只说大类，具体原因来自响应体里的 code。
        Assert.Equal(NodeEnrollmentFailure.CodeNotFound, exception.Failure);
        Assert.Equal("code_not_found", exception.ServerCode);
        Assert.Equal(404, exception.StatusCode);

        // 异常会被日志与诊断包带走：里面绝不许出现配对码，也不许出现地点 ID。
        var text = exception.ToString();
        Assert.DoesNotContain(PairingCode, text, StringComparison.Ordinal);
        Assert.DoesNotContain("7KQ2", text, StringComparison.Ordinal);
        Assert.DoesNotContain(SiteId, text, StringComparison.Ordinal);
        Assert.DoesNotContain("7K3M9Q", text, StringComparison.Ordinal);

        // 请求本身走 v4 的接入地址，且必须匿名：用户还没登录也要能接入。
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://control.example/control/api/enroll", request.Uri.AbsoluteUri);
        Assert.Null(request.Authorization);

        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal(SiteId, root.GetProperty("siteId").GetString());
        // 请求体里是规范化后的配对码：连字符与大小写都在发出去之前被吃掉（服务端只认 8 位）。
        Assert.Equal("ABCD7KQ2", root.GetProperty("code").GetString());
    }

    // ---------------------------------------------------------------- ④网络不通 ≠ 码坏了

    [Fact]
    public async Task 网络不通只说网络_不把好码说成坏码()
    {
        // 传输层直接炸（DNS/连接/TLS/超时）：这时输入可能完全正确，界面绝不能说"码无效"。
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection refused"));
        var client = new NodeEnrollmentClient(
            new FakeHttpClientFactory(handler),
            new FakeEndpointStore("https://control.example/"));

        var exception = await Assert.ThrowsAsync<NodeEnrollmentException>(() => client.EnrollAsync(
            SiteId,
            PairingCode,
            deviceName: null,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(NodeEnrollmentFailure.Network, exception.Failure);
        Assert.Null(exception.ServerCode);
        Assert.Null(exception.StatusCode);
        Assert.Contains("无法连接集控服务端", exception.Message, StringComparison.Ordinal);

        // 与 ③ 同一条安全边界：这一条也不能把双因子带进异常文本。
        var text = exception.ToString();
        Assert.DoesNotContain(SiteId, text, StringComparison.Ordinal);
        Assert.DoesNotContain(PairingCode, text, StringComparison.Ordinal);
        Assert.DoesNotContain("7K3M9Q", text, StringComparison.Ordinal);
        Assert.DoesNotContain("7KQ2", text, StringComparison.Ordinal);

        // 失败发生在传输层，请求确实发出去过一次。
        Assert.Single(handler.Requests);
    }

    // ---------------------------------------------------------------- 测试替身

    private FileNodeEnrollmentStore CreateStore() =>
        new(EnrollmentPath, NullLogger<FileNodeEnrollmentStore>.Instance);

    private static NodeEnrollmentRecord Record() => new()
    {
        DeviceId = DeviceId,
        Secret = Secret,
        SiteId = SiteId,
        SiteName = "高一（1）班",
        ClassId = "class-1",
        ClassName = "高一（1）班",
        EnrolledAt = DateTimeOffset.UtcNow
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
}