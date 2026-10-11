using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.Auth;
using SecRandom.Services.Config;
using SecRandom.Services.ControlNode;
using SecRandom.Services.ControlPlane;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Core.Tests;

/// <summary>
///     "不登录 SECTL 也能用自建集控"在**节点通道**这一侧的落地点。
/// </summary>
/// <remarks>
///     <para>
///         三条断言合起来才是这个功能的完整证明：
///         ①已接入 ⇒ 连接用的 Bearer 是节点令牌（并且在真实 <c>ClientWebSocket</c> 上确实落在
///         <c>Authorization</c> 头里，不是进了 URL）；
///         ②已接入 ⇒ 登录/退出 SECTL 不动这条连接；
///         ③未接入 ⇒ 登录状态变化仍然立刻重连（线上行为逐字不变）。
///     </para>
/// </remarks>
public sealed class ControlNodeEnrollmentChannelTests(ITestOutputHelper output)
{
    private const string NodeToken = "srn_node7_secret-value-that-must-never-leak";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly ITestOutputHelper _output = output;

    // ---------------------------------------------------------------- ①出示节点令牌

    [Fact]
    public async Task 已接入时节点通道连接请求只带节点令牌()
    {
        var enrollment = new FakeEnrollmentStore();
        Assert.True(enrollment.Save(Record()));

        var (client, factory, _) = CreateClient(enrollment);
        await RunUntilConnectedAsync(client, factory);

        var request = Assert.Single(factory.Requests);
        Assert.Equal($"ws://127.0.0.1:9/v1/node/connect", request.Endpoint);
        Assert.Equal(NodeToken, request.BearerToken);
        Assert.DoesNotContain("sectl", request.BearerToken, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("node-7", request.NodeId);
        Assert.Equal("grp_1", request.GroupId);

        // 令牌不得出现在地址里：查询参数会进服务端访问日志。
        Assert.DoesNotContain(NodeToken, request.Endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 已接入时真实WebSocket的Authorization头是节点令牌且地址里没有令牌()
    {
        using var server = new LoopbackNodeServer();

        var enrollment = new FakeEnrollmentStore();
        Assert.True(enrollment.Save(Record()));

        var stateStore = new InMemoryStateStore(State(server.Endpoint));
        // 这一条走**真实** ClientWebSocket：Header 到底带出去没有，只有真连一次才能证明。
        var transportFactory = new WebSocketControlNodeTransportFactory(NullLogger<WebSocketControlNodeTransport>.Instance);
        var client = new ControlNodeClient(
            transportFactory,
            new ControlNodeCredentialProvider(CreateAuthService(), enrollment),
            stateStore,
            new RecordingDispatcher(),
            Options(),
            NullLogger<ControlNodeClient>.Instance,
            NullLogger<ControlNodeSession>.Instance,
            enrollment);

        using var source = new CancellationTokenSource(Timeout);
        var runTask = client.RunAsync(source.Token);

        var headers = await server.WaitForConnectionAsync();

        Assert.Equal($"Bearer {NodeToken}", headers.Authorization);
        Assert.DoesNotContain(NodeToken, headers.RawUrl, StringComparison.Ordinal);
        Assert.Equal("/v1/node/connect", headers.Path);

        await source.CancelAsync();
        await runTask;
        Assert.Equal(ControlNodeLinkStatus.Disabled, client.LinkState.Status);
    }

    // ---------------------------------------------------------------- ②③登录状态与连接无关

    [Fact]
    public async Task 已接入时登录与退出都不重启节点连接()
    {
        var enrollment = new FakeEnrollmentStore();
        Assert.True(enrollment.Save(Record()));

        var auth = CreateAuthService();
        var (client, factory, _) = CreateClient(enrollment, auth);
        var hosted = new ControlNodeHostedService(
            client,
            NullLogger<ControlNodeHostedService>.Instance,
            auth,
            enrollment);

        await hosted.StartAsync(CancellationToken.None);
        await WaitForConnectsAsync(factory, 1);

        // 登录：连接不该动。
        SetSignedIn(auth, true);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Equal(1, factory.ConnectCount);

        // 退出登录：同样不该动——这就是"退出 SECTL 不影响自建集控"。
        SetSignedIn(auth, false);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Equal(1, factory.ConnectCount);

        await hosted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task 未接入时登录状态变化仍然立刻重启_线上行为不变()
    {
        var enrollment = new FakeEnrollmentStore();
        var auth = CreateAuthService();
        SetSignedIn(auth, true); // 线上用户：先登录，节点通道才会连
        var (client, factory, _) = CreateClient(enrollment, auth);
        var hosted = new ControlNodeHostedService(
            client,
            NullLogger<ControlNodeHostedService>.Instance,
            auth,
            enrollment);

        await hosted.StartAsync(CancellationToken.None);
        await WaitForConnectsAsync(factory, 1);
        Assert.Equal("access", factory.Requests[^1].BearerToken);

        // 退出登录：未接入时今天的行为就是连接立刻跟着停下来，这一点必须逐字保留。
        SetSignedIn(auth, false);
        await WaitForStateAsync(client, ControlNodeLinkStatus.Idle, "not_signed_in");

        // 重新登录：连接立刻回来。
        SetSignedIn(auth, true);
        await WaitForConnectsAsync(factory, 2);

        await hosted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task 接入与清除接入都会让连接立刻换用新凭据()
    {
        var enrollment = new FakeEnrollmentStore();
        var auth = CreateAuthService();
        SetSignedIn(auth, true); // 线上用户：已登录 SECTL
        var (client, factory, _) = CreateClient(enrollment, auth);
        var hosted = new ControlNodeHostedService(
            client,
            NullLogger<ControlNodeHostedService>.Instance,
            auth,
            enrollment);

        await hosted.StartAsync(CancellationToken.None);
        await WaitForConnectsAsync(factory, 1);
        Assert.Equal("access", factory.Requests[^1].BearerToken); // 未接入 ⇒ 还是账号令牌

        Assert.True(enrollment.Save(Record()));
        await WaitForConnectsAsync(factory, 2);
        Assert.Equal(NodeToken, factory.Requests[^1].BearerToken); // 接入 ⇒ 只剩节点令牌

        enrollment.Clear();
        await WaitForConnectsAsync(factory, 3);
        Assert.Equal("access", factory.Requests[^1].BearerToken); // 清除接入 ⇒ 立刻回到账号令牌

        await hosted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task 凭据被服务端拒绝且已接入时报未接入而不是让用户去登录()
    {
        var enrollment = new FakeEnrollmentStore();
        Assert.True(enrollment.Save(Record()));

        // 服务端通过握手的 error 帧拒绝节点令牌（真实协议路径），
        // 而"登录 SECTL"对自建集控是死路：连续被拒后必须停在 Blocked。
        var factory = new FakeTransportFactory(() => new RejectingNodeTransport());
        var client = new ControlNodeClient(
            factory,
            new ControlNodeCredentialProvider(CreateAuthService(), enrollment),
            new InMemoryStateStore(State()),
            new RecordingDispatcher(),
            Options(maxUnauthorized: 1),
            NullLogger<ControlNodeClient>.Instance,
            NullLogger<ControlNodeSession>.Instance,
            enrollment);

        using var source = new CancellationTokenSource(Timeout);
        var runTask = client.RunAsync(source.Token);

        await WaitForStatusAsync(client, ControlNodeLinkStatus.Blocked);
        Assert.Equal("unauthorized", client.LinkState.Detail);

        await source.CancelAsync();
        await runTask;
    }

    // ---------------------------------------------------------------- 重连语义

    /// <summary>
    ///     退避等待必须能被唤醒，而"连接过程中积压的唤醒"不得短路本轮退避。
    /// </summary>
    /// <remarks>
    ///     两个方向都要测：只测前者会退化成"每次退避都被上一次留下的信号立刻吃掉"（重连风暴），
    ///     只测后者会退化成"用户翻开关后还要干等最长 60 秒"。
    /// </remarks>
    [Fact]
    public async Task 退避期间唤醒能立刻重连_而连接过程中的残留唤醒不会短路退避()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var enrollment = new FakeEnrollmentStore();
        Assert.True(enrollment.Save(Record()));

        var factory = new GatedTransportFactory();
        var client = new ControlNodeClient(
            factory,
            new ControlNodeCredentialProvider(CreateAuthService(), enrollment),
            new InMemoryStateStore(State()),
            new RecordingDispatcher(),
            Options() with
            {
                // 退避 700ms 起步（抖动最多 +1000ms）：远大于下面 400ms 的判定窗口，
                // "没有立刻重连"这条断言不会因为抖动而假绿。
                InitialBackoff = TimeSpan.FromMilliseconds(700),
                MaxBackoff = TimeSpan.FromSeconds(5)
            },
            NullLogger<ControlNodeClient>.Instance,
            NullLogger<ControlNodeSession>.Instance,
            enrollment);

        using var source = new CancellationTokenSource(Timeout);
        var runTask = client.RunAsync(source.Token);

        // ---- ① 连接进行中注入一次唤醒：它属于"上一次连接"，不能把接下来的退避吃掉。
        await factory.WaitForAttemptsAsync(1, cancellationToken);
        client.Wake();

        var firstRelease = Stopwatch.GetTimestamp();
        factory.ReleaseAttempt(1);

        await WaitForStatusAsync(client, ControlNodeLinkStatus.WaitingToRetry);
        var scheduledDelay = client.LinkState.RetryDelay;
        Assert.NotNull(scheduledDelay);

        await Task.Delay(400, cancellationToken);
        Assert.Equal(1, factory.AttemptCount);

        // 退避到点后仍然会自己重连：既没被旧信号短路，也没卡死。
        await factory.WaitForAttemptsAsync(2, cancellationToken);
        _output.WriteLine(
            $"残留唤醒（连接进行中注入）：第二次连接在 {Stopwatch.GetElapsedTime(firstRelease).TotalMilliseconds:0} ms 后才开始，"
            + $"本轮退避 {scheduledDelay.Value.TotalMilliseconds:0} ms 未被短路。");

        // ---- ② 退避等待中唤醒：必须立刻重连。
        await WaitForStatusAsync(client, ControlNodeLinkStatus.Connecting);
        factory.ReleaseAttempt(2);

        await WaitForStatusAsync(client, ControlNodeLinkStatus.WaitingToRetry);
        // 状态在 DrainWakeSignal 之前就设好了，留一点余量再唤醒，确保测的是"等待中唤醒"。
        await Task.Delay(100, cancellationToken);
        var pendingDelay = client.LinkState.RetryDelay;
        Assert.NotNull(pendingDelay);

        var wakeMoment = Stopwatch.GetTimestamp();
        client.Wake();

        await factory.WaitForAttemptsAsync(3, cancellationToken);
        var wakeLatency = Stopwatch.GetElapsedTime(wakeMoment);
        _output.WriteLine(
            $"退避等待中唤醒：{wakeLatency.TotalMilliseconds:0} ms 后开始第 3 次连接"
            + $"（本轮退避 {pendingDelay.Value.TotalMilliseconds:0} ms）。");

        Assert.True(
            wakeLatency < TimeSpan.FromMilliseconds(400),
            $"唤醒后 {wakeLatency.TotalMilliseconds:0} ms 才开始重连，退避等待没有被唤醒。");

        await source.CancelAsync();
        await runTask;
    }

    [Fact]
    public async Task 升级期被拒的401计入未授权并停在需要重新接入()
    {
        var enrollment = new FakeEnrollmentStore();
        Assert.True(enrollment.Save(Record()));

        // 真实路径：服务端在 WebSocket 升级响应里就回 401（令牌被撤销时就是这样）。
        var factory = new RejectingConnectFactory();
        var client = new ControlNodeClient(
            factory,
            new ControlNodeCredentialProvider(CreateAuthService(), enrollment),
            new InMemoryStateStore(State()),
            new RecordingDispatcher(),
            Options(maxUnauthorized: 2),
            NullLogger<ControlNodeClient>.Instance,
            NullLogger<ControlNodeSession>.Instance,
            enrollment);

        using var source = new CancellationTokenSource(Timeout);
        var runTask = client.RunAsync(source.Token);

        // 旧实现把它当 ConnectionLost：这里永远等不到 Blocked，只会无限重连。
        await WaitForStateAsync(client, ControlNodeLinkStatus.Blocked, "unauthorized");

        // 计数确实记在"未授权"这条路上：连续两次被拒才停，而不是重试到天荒地老。
        Assert.Equal(2, factory.AttemptCount);

        await source.CancelAsync();
        await runTask;
    }

    [Fact]
    public async Task 真实WebSocket升级被403拒绝时归入未授权而不是连不上()
    {
        using var server = new RawHttpResponder("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        var factory = new WebSocketControlNodeTransportFactory(NullLogger<WebSocketControlNodeTransport>.Instance);

        // 这一步同时证明 ClientWebSocket 真的能读回升级响应的状态码（CollectHttpResponseDetails）。
        var exception = await Assert.ThrowsAsync<ControlNodeConnectRejectedException>(() => factory.ConnectAsync(
            new ControlNodeConnectRequest(server.Endpoint, NodeToken, "node-7", "grp_1"),
            TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);

        // 令牌不得出现在异常消息里（异常会进日志）。
        Assert.DoesNotContain(NodeToken, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 重启时不会出现两条循环()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var enrollment = new FakeEnrollmentStore();
        Assert.True(enrollment.Save(Record()));

        var factory = new GenerationTransportFactory();
        var store = new InMemoryStateStore(State());
        var client = new ControlNodeClient(
            factory,
            new ControlNodeCredentialProvider(CreateAuthService(), enrollment),
            store,
            new RecordingDispatcher(),
            Options(),
            NullLogger<ControlNodeClient>.Instance,
            NullLogger<ControlNodeSession>.Instance,
            enrollment);

        var service = new ControlNodeHostedService(
            client,
            NullLogger<ControlNodeHostedService>.Instance,
            authService: null,
            enrollmentStore: enrollment,
            stateStore: store,
            endpointStore: null,
            stopTimeout: TimeSpan.FromMilliseconds(150));

        await service.StartAsync(cancellationToken);
        await factory.WaitForConnectCountAsync(1, cancellationToken);
        Assert.Equal(1, factory.MaxConcurrentSessions);

        // 接入记录变化就会重启。第一条循环故意忽略取消令牌（模拟"停不下来"的会话），
        // 于是停止窗口必然超时——这正是旧实现并行启动第二条循环、被服务端互相 replaced 的场景。
        Assert.True(enrollment.Save(Record()));

        // 旧实现的第二条循环会在硬编码的 5 秒停止窗口结束时出现：等过那个时刻再断言。
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5.5);
        while (DateTime.UtcNow < deadline)
            await Task.Delay(50, cancellationToken);

        _output.WriteLine(
            $"重启后 {5.5:0.#} s：连接次数 {factory.ConnectCount}，并发会话峰值 {factory.MaxConcurrentSessions}，"
            + $"旧循环是否已退出 {(factory.FirstSessionReleased ? "是" : "否")}。");

        Assert.False(factory.FirstSessionReleased);
        Assert.Equal(1, factory.ConnectCount);
        Assert.Equal(1, factory.MaxConcurrentSessions);

        // 放行旧循环：它退出后新循环才开始连，并发连接数全程不超过 1。
        factory.ReleaseFirstSession();
        await factory.WaitForConnectCountAsync(2, cancellationToken);
        await Task.Delay(100, cancellationToken);
        Assert.Equal(1, factory.MaxConcurrentSessions);

        await service.StopAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- 测试替身

    private static ControlNodeState State(string? serverUrl = null) => new()
    {
        NodeId = "node-7",
        GroupId = "grp_1",
        ServerUrl = serverUrl ?? "ws://127.0.0.1:9/v1/node/connect",
        RemoteControlEnabled = true
    };

    private static ControlNodeClientOptions Options(int maxUnauthorized = 5) => new()
    {
        Platform = "windows",
        Version = "3.1.2",
        HostName = "sr-test-host",
        MaxConsecutiveUnauthorized = maxUnauthorized,
        InitialBackoff = TimeSpan.FromMilliseconds(50),
        MaxBackoff = TimeSpan.FromMilliseconds(200)
    };

    private static (ControlNodeClient Client, FakeTransportFactory Factory, InMemoryStateStore Store) CreateClient(
        INodeEnrollmentStore enrollment,
        SectlAuthService? authService = null)
    {
        var factory = new FakeTransportFactory();
        var store = new InMemoryStateStore(State());
        var client = new ControlNodeClient(
            factory,
            new ControlNodeCredentialProvider(authService ?? CreateAuthService(), enrollment),
            store,
            new RecordingDispatcher(),
            Options(),
            NullLogger<ControlNodeClient>.Instance,
            NullLogger<ControlNodeSession>.Instance,
            enrollment);

        return (client, factory, store);
    }

    private static async Task RunUntilConnectedAsync(ControlNodeClient client, FakeTransportFactory factory)
    {
        using var source = new CancellationTokenSource(Timeout);
        _ = client.RunAsync(source.Token);
        await WaitForConnectsAsync(factory, 1);
        await source.CancelAsync();
    }

    private static async Task WaitForConnectsAsync(FakeTransportFactory factory, int expected)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (factory.ConnectCount >= expected)
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException($"等待第 {expected} 次节点连接超时（当前 {factory.ConnectCount} 次）。");
    }

    private static async Task WaitForStatusAsync(ControlNodeClient client, ControlNodeLinkStatus status)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (client.LinkState.Status == status)
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException($"等待连接状态 {status} 超时（当前 {client.LinkState.Status}）。");
    }

    /// <summary>一个真实的、当前**没登录**的认证服务：节点通道不依赖它，这一点必须在它面前证明。</summary>
    private static SectlAuthService CreateAuthService()
    {
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(new MainConfigModel()));

        return new SectlAuthService(
            TestTokenStore.Create(),
            new StubHttpClientFactory(new HttpClient()),
            new DeviceUuidStore(configHandler, NullLogger<DeviceUuidStore>.Instance),
            NullLogger<SectlAuthService>.Instance,
            new LoopbackAuthRedirectBrokerFactory());
    }

    /// <summary>
    ///     真真切切地登录/退出一遍：写入令牌（<c>SectlAuthService.IsSignedIn</c> 正是看它），
    ///     再照真实的通知路径触发 <c>StateChanged</c>。这样测的是"登录状态变化"这个事实本身，
    ///     而不是我伪造的一个事件。
    /// </summary>
    private static void SetSignedIn(SectlAuthService auth, bool signedIn)
    {
        var tokenField = typeof(SectlAuthService).GetField("_token", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(tokenField);
        tokenField!.SetValue(auth, signedIn ? new SectlToken("access", "refresh", "user-1", 3600) : null);
        Assert.Equal(signedIn, auth.IsSignedIn);

        var changed = typeof(SectlAuthService).GetField("StateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(changed);

        // 启动前还没有订阅者（这时只是把"当前已登录"这个事实摆好）；
        // 启动后调用时必定有订阅者，否则连接数断言会直接失败。
        (changed!.GetValue(auth) as EventHandler)?.Invoke(auth, EventArgs.Empty);
    }

    private static NodeEnrollmentRecord Record() => new()
    {
        NodeId = "node-7",
        GroupId = "grp_1",
        GroupName = "高一（1）班",
        NodeToken = NodeToken,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
    };

    private sealed class FakeTransportFactory(Func<IControlNodeTransport>? createTransport = null)
        : IControlNodeTransportFactory
    {
        private readonly List<ControlNodeConnectRequest> _requests = [];
        private readonly object _gate = new();

        public IReadOnlyList<ControlNodeConnectRequest> Requests
        {
            get
            {
                lock (_gate)
                {
                    return _requests.ToList();
                }
            }
        }

        public int ConnectCount { get; private set; }

        public Task<IControlNodeTransport> ConnectAsync(
            ControlNodeConnectRequest request,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _requests.Add(request);
                ConnectCount++;
            }

            return Task.FromResult(createTransport?.Invoke() ?? (IControlNodeTransport)new FakeNodeTransport());
        }
    }

    /// <summary>握手后立刻回一个 unauthorized 错误帧：真实协议里"令牌被服务端拒绝"就是这么发生的。</summary>
    private sealed class RejectingNodeTransport : IControlNodeTransport
    {
        private readonly Channel<ControlFrame?> _incoming = Channel.CreateUnbounded<ControlFrame?>();

        public string? CloseReason => "unauthorized";

        public Task SendAsync(ControlFrame frame, CancellationToken cancellationToken)
        {
            if (frame.Type == ControlFrameTypes.Hello)
            {
                _incoming.Writer.TryWrite(new ControlFrame
                {
                    Type = ControlFrameTypes.Error,
                    Code = ControlErrorCodes.Unauthorized
                });
            }

            return Task.CompletedTask;
        }

        public async Task<ControlFrame?> ReceiveAsync(CancellationToken cancellationToken) =>
            await _incoming.Reader.ReadAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            _incoming.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>握手后一直不回 hello.ack、也不关连接：用来观察"建立了几条连接"。</summary>
    private sealed class FakeNodeTransport : IControlNodeTransport
    {
        public string? CloseReason => null;

        public Task SendAsync(ControlFrame frame, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<ControlFrame?> ReceiveAsync(CancellationToken cancellationToken)
        {
            // 连接保持开着：测试只关心"建立了几条连接"，不关心帧。
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    ///     每次连接都卡在一道闸门上、由测试决定它何时失败：用来精确观察退避的时间行为。
    /// </summary>
    private sealed class GatedTransportFactory : IControlNodeTransportFactory
    {
        private readonly object _gate = new();
        private readonly List<TaskCompletionSource> _attempts = [];

        public int AttemptCount
        {
            get
            {
                lock (_gate)
                {
                    return _attempts.Count;
                }
            }
        }

        public async Task<IControlNodeTransport> ConnectAsync(
            ControlNodeConnectRequest request,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource gate;
            lock (_gate)
            {
                gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _attempts.Add(gate);
            }

            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            throw new IOException("模拟连接中断。");
        }

        /// <summary>放行第 <paramref name="attempt" /> 次连接（从 1 开始），让它以失败告终。</summary>
        public void ReleaseAttempt(int attempt)
        {
            lock (_gate)
            {
                _attempts[attempt - 1].TrySetResult();
            }
        }

        public async Task WaitForAttemptsAsync(int expected, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (_gate)
                {
                    if (_attempts.Count >= expected)
                        return;
                }

                await Task.Delay(10, cancellationToken);
            }

            throw new TimeoutException($"等待第 {expected} 次连接超时（当前 {AttemptCount} 次）。");
        }
    }

    /// <summary>连接一建立就被服务端拒绝（真实路径里是升级响应的 401/403）。</summary>
    private sealed class RejectingConnectFactory : IControlNodeTransportFactory
    {
        private int _attempts;

        public int AttemptCount => Volatile.Read(ref _attempts);

        public Task<IControlNodeTransport> ConnectAsync(
            ControlNodeConnectRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            return Task.FromException<IControlNodeTransport>(
                new ControlNodeConnectRejectedException(HttpStatusCode.Unauthorized));
        }
    }

    /// <summary>回一个固定 HTTP 响应就断开的极简服务端：用来让真实的 WebSocket 升级失败。</summary>
    private sealed class RawHttpResponder : IDisposable
    {
        private readonly string _response;
        private readonly System.Net.Sockets.TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _loop;

        public RawHttpResponder(string response)
        {
            _response = response;
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Endpoint = $"ws://127.0.0.1:{port}/v1/node/connect";
            _loop = Task.Run(AcceptAsync);
        }

        public string Endpoint { get; }

        private async Task AcceptAsync()
        {
            while (true)
            {
                System.Net.Sockets.TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                using (client)
                {
                    try
                    {
                        var stream = client.GetStream();
                        var buffer = new byte[4096];
                        var read = 0;

                        // 先把请求头读完再回响应：客户端还没写完就被关掉的话，测到的就不是 403 了。
                        while (read < buffer.Length)
                        {
                            var count = await stream
                                .ReadAsync(buffer.AsMemory(read), CancellationToken.None)
                                .ConfigureAwait(false);

                            if (count <= 0)
                                break;

                            read += count;
                            if (Encoding.UTF8.GetString(buffer, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal))
                                break;
                        }

                        await stream.WriteAsync(Encoding.ASCII.GetBytes(_response)).ConfigureAwait(false);
                        await stream.FlushAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // 客户端半路断开也没关系。
                    }
                }
            }
        }

        public void Dispose()
        {
            try
            {
                _listener.Stop();
            }
            catch (Exception)
            {
                // 关不掉也无所谓。
            }
        }
    }

    /// <summary>
    ///     按"第几条连接"返回传输，并统计同时活着的会话数：用来证明重启不会并行起两条循环。
    /// </summary>
    private sealed class GenerationTransportFactory : IControlNodeTransportFactory
    {
        private readonly object _gate = new();
        private readonly List<StubbornNodeTransport> _transports = [];
        private int _connectCount;
        private int _live;
        private int _maxLive;

        public int ConnectCount
        {
            get
            {
                lock (_gate)
                {
                    return _connectCount;
                }
            }
        }

        public int MaxConcurrentSessions
        {
            get
            {
                lock (_gate)
                {
                    return _maxLive;
                }
            }
        }

        public bool FirstSessionReleased
        {
            get
            {
                lock (_gate)
                {
                    return _transports.Count > 0 && _transports[0].Released;
                }
            }
        }

        public Task<IControlNodeTransport> ConnectAsync(
            ControlNodeConnectRequest request,
            CancellationToken cancellationToken)
        {
            StubbornNodeTransport transport;
            lock (_gate)
            {
                _connectCount++;
                _live++;
                _maxLive = Math.Max(_maxLive, _live);
                transport = new StubbornNodeTransport(this, stubborn: _connectCount == 1);
                _transports.Add(transport);
            }

            return Task.FromResult<IControlNodeTransport>(transport);
        }

        public void EndSession()
        {
            lock (_gate)
            {
                _live--;
            }
        }

        public void ReleaseFirstSession()
        {
            lock (_gate)
            {
                _transports[0].Release();
            }
        }

        public async Task WaitForConnectCountAsync(int expected, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (ConnectCount >= expected)
                    return;

                await Task.Delay(10, cancellationToken);
            }

            throw new TimeoutException($"等待第 {expected} 次连接超时（当前 {ConnectCount} 次）。");
        }
    }

    /// <summary>
    ///     第一条连接**故意忽略取消令牌**：模拟"停止时卡在接收上"的会话，让停止窗口必然超时——
    ///     这正是旧实现会并行启动第二条循环（同 node_id 互相 replaced）的场景。
    /// </summary>
    private sealed class StubbornNodeTransport(GenerationTransportFactory owner, bool stubborn) : IControlNodeTransport
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? CloseReason => null;

        public bool Released => _release.Task.IsCompleted;

        public void Release() => _release.TrySetResult();

        public Task SendAsync(ControlFrame frame, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<ControlFrame?> ReceiveAsync(CancellationToken cancellationToken)
        {
            if (stubborn)
            {
                // 忽略取消令牌：停止窗口过后这条会话仍然活着。
                await _release.Task.ConfigureAwait(false);
            }
            else
            {
                try
                {
                    await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 正常停止路径。
                }
            }

            owner.EndSession();
            return null;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
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

    private sealed class InMemoryStateStore(ControlNodeState initial) : IControlNodeStateStore
    {
        private readonly object _gate = new();
        private ControlNodeState _current = initial;

        public ControlNodeState Current
        {
            get
            {
                lock (_gate)
                {
                    return _current;
                }
            }
        }

        public event EventHandler<ControlNodeState>? Changed;

        public void Update(Func<ControlNodeState, ControlNodeState> mutate)
        {
            ControlNodeState updated;
            lock (_gate)
            {
                updated = mutate(_current);
                if (updated == _current)
                    return;

                _current = updated;
            }

            Changed?.Invoke(this, updated);
        }
    }

    private sealed class RecordingDispatcher : IControlCommandDispatcher
    {
        public IReadOnlyList<string> DeclaredCapabilities { get; } = [];

        public bool CanExecute(string capability) => false;

        public Task<ControlCommandOutcome> ExecuteAsync(
            ControlCommandInvocation invocation,
            CancellationToken cancellationToken) =>
            Task.FromResult(ControlCommandOutcome.Success);
    }

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
                if (_record is null)
                    return false;

                _record = null;
                _status = NodeEnrollmentStatus.NotEnrolled;
                Generation++;
            }

            Changed?.Invoke(this, NodeEnrollmentStatus.NotEnrolled);
            return true;
        }
    }

    private static async Task WaitForStateAsync(ControlNodeClient client, ControlNodeLinkStatus status, string? detail)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var state = client.LinkState;
            if (state.Status == status && state.Detail == detail)
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException(
            $"等待连接状态 {status}/{detail} 超时（当前 {client.LinkState.Status}/{client.LinkState.Detail}）。");
    }

    /// <summary>本机回环上的极简节点服务端：只把握手请求头记下来，不参与协议。</summary>
    private sealed class LoopbackNodeServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;

        public LoopbackNodeServer()
        {
            using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            Endpoint = $"ws://127.0.0.1:{port}/v1/node/connect";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _loop = Task.Run(AcceptAsync);
        }

        public string Endpoint { get; }

        private TaskCompletionSource<Handshake> _handshake { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Handshake> WaitForConnectionAsync() => await _handshake.Task.WaitAsync(Timeout);

        private async Task AcceptAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }

                _handshake.TrySetResult(new Handshake(
                    context.Request.Headers["Authorization"],
                    context.Request.RawUrl ?? string.Empty,
                    context.Request.Url?.AbsolutePath ?? string.Empty));

                var socket = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                _ = CloseLaterAsync(socket.WebSocket);
            }
        }

        private static async Task CloseLaterAsync(WebSocket socket)
        {
            await Task.Delay(200).ConfigureAwait(false);
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
            catch (Exception)
            {
                // 客户端先走也没关系。
            }
            finally
            {
                socket.Dispose();
            }
        }

        public void Dispose()
        {
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch (Exception)
            {
                // 关不掉也无所谓。
            }

            _handshake.TrySetCanceled();
        }

        public sealed record Handshake(string? Authorization, string RawUrl, string Path);
    }
}
