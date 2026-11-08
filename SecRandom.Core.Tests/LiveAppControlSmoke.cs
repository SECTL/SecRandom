using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.ControlNode;
using SecRandom.Services.ControlPlane;
using SecRandom.Shared.Models.ControlNode;
using Xunit;

namespace SecRandom.Core.Tests;

/// <summary>
///     临时的一次性真机冒烟（跑完即删）：用 App 自己的接入客户端 + 会话 + 传输去打线上 Hub。
///     需要环境变量 SECTL_LIVE_SITE_ID / SECTL_LIVE_PAIR_CODE / SECTL_LIVE_CLASS_ID / SECTL_LIVE_ADMIN，
///     缺任何一个直接返回（不失败），所以留在仓库里也不会破坏 CI。
/// </summary>
public sealed class LiveAppControlSmoke
{
    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);

    [Fact]
    public async Task 真机_接入分班握手并收到指令两帧回执()
    {
        var siteId = Env("SECTL_LIVE_SITE_ID");
        var pairCode = Env("SECTL_LIVE_PAIR_CODE");
        var classId = Env("SECTL_LIVE_CLASS_ID");
        var admin = Env("SECTL_LIVE_ADMIN");
        var baseUrl = Env("SECTL_LIVE_BASE_URL") ?? "https://sectl.cn";
        if (string.IsNullOrWhiteSpace(siteId) || string.IsNullOrWhiteSpace(pairCode) ||
            string.IsNullOrWhiteSpace(classId) || string.IsNullOrWhiteSpace(admin))
            return;

        // 1) 真接入：地点 ID + 配对码 → 设备凭据
        var enrollment = new NodeEnrollmentClient(
            new SimpleHttpClientFactory(),
            new FakeEndpointStore(baseUrl),
            baseUrl);
        var record = await enrollment.EnrollAsync(siteId, pairCode, "applive-e2e");
        Assert.False(string.IsNullOrWhiteSpace(record.DeviceId));
        Assert.False(string.IsNullOrWhiteSpace(record.Secret));
        Assert.Equal("SC-6M34RQ", record.SiteId);
        Assert.False(string.IsNullOrWhiteSpace(record.SiteName));
        Assert.Equal(classId, record.ClassId);

        // 2) 真连接 + 握手：用 App 自己的 WebSocket 传输与会话
        var stateStore = new MemoryStateStore(new ControlNodeState
        {
            SiteId = record.SiteId,
            SiteName = record.SiteName,
            ClassId = record.ClassId,
            // 本机开关必须打开，否则设备会按设计回 ack rejected(local_remote_disabled)
            RemoteControlEnabled = true,
        });
        var dispatcher = new StubDispatcher();
        var factory = new WebSocketControlNodeTransportFactory(
            NullLogger<WebSocketControlNodeTransport>.Instance);
        await using var transport = await factory.ConnectAsync(
            new ControlNodeConnectRequest("wss://sectl.cn/control/ws"), CancellationToken.None);
        var session = new ControlNodeSession(
            transport,
            stateStore,
            dispatcher,
            new NodeDeviceCredential(record.DeviceId, record.Secret, record.SiteId, record.SiteName,
                record.ClassId, record.ClassName),
            new ControlNodeClientOptions { Platform = "windows", Version = "smoke", HostName = "applive-e2e" },
            NullLogger<ControlNodeSession>.Instance);

        var handshake = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Handshaken += (_, seconds) => handshake.TrySetResult(seconds);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var run = session.RunAsync(cts.Token);

        var negotiated = await handshake.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(25, negotiated);
        Assert.Equal(75, session.OfflineAfterSeconds);

        // 3) 让 Hub 从管理通道下发一条查询命令：设备侧要回 cmd.ack + cmd.result，管理接口才会 200
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{baseUrl}/control/api/cmd")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    class_id = classId,
                    platform = "secrandom",
                    cmd = "roster.read",
                    args = new Dictionary<string, object> { ["roster_kind"] = "students" },
                    issuer = "applive-e2e",
                }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {admin}");
        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"管理通道下发失败：{response.StatusCode} {body}");
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.StartsWith("cmd_", doc.RootElement.GetProperty("commandId").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("payload").GetProperty("students").GetInt32());
        Assert.True(dispatcher.Calls >= 1);
        Assert.True(session.IsHandshaken);

        await cts.CancelAsync();
        await run;
    }

    private sealed class SimpleHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeEndpointStore(string current) : IControlPlaneEndpointStore
    {
        public string Current { get; } = current;
        public bool IsCustom => false;
        public bool TryUpdate(string? endpoint, out string? error)
        {
            error = null;
            return true;
        }

        public void ResetToDefault()
        {
        }

        public event EventHandler<string>? Changed;
    }

    private sealed class MemoryStateStore(ControlNodeState initial) : IControlNodeStateStore
    {
        public ControlNodeState Current { get; private set; } = initial;
        public event EventHandler<ControlNodeState>? Changed;

        public void Update(Func<ControlNodeState, ControlNodeState> mutate)
        {
            Current = mutate(Current);
            Changed?.Invoke(this, Current);
        }
    }

    private sealed class StubDispatcher : IControlCommandDispatcher
    {
        public int Calls { get; private set; }
        public IReadOnlyList<string> DeclaredCapabilities { get; } =
            new[] { "status.read", "class.select", "class.release", "draw.lock", "roster.read" };

        public bool CanExecute(string capability) => DeclaredCapabilities.Contains(capability);

        public Task<ControlCommandOutcome> ExecuteAsync(
            ControlCommandInvocation invocation,
            CancellationToken cancellationToken)
        {
            Calls += 1;
            return Task.FromResult(ControlCommandOutcome.SuccessWith(new { students = 2 }));
        }
    }
}