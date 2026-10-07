using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.Auth;
using SecRandom.Services.ControlPlane;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     把集控节点客户端挂到 Host 生命周期上。
/// </summary>
/// <remarks>
///     <para>
///         <b>已接入自建集控时，账号登录状态与这条长连接无关</b>：凭据由接入令牌提供，
///         登录/退出 SECTL 都不会（也不该）动它，因此这里不会因为登录状态变化而断开重连。
///     </para>
///     <para>
///         <b>未接入时</b>仍然沿用原来的行为：退出登录立刻断开，重新登录重新连上。
///         客户端自己也有 30 秒的等待窗口兜底，这里只是让它更快收敛。
///     </para>
///     <para>
///         接入状态本身的变化（接入成功 / 清除接入）会重启客户端，让新的令牌立刻生效。
///     </para>
///     <para>
///         <b>重启不会出现两条循环</b>：停止是尽力而为（最多等 <c>_stopTimeout</c>），
///         但下一条循环挂在上一条循环后面启动，所以同一 <c>node_id</c> 任何时刻只有一条连接——
///         两条会被服务端互相 <c>replaced</c>，看起来就是"刚连上就掉线"。
///     </para>
/// </remarks>
public sealed class ControlNodeHostedService(
    ControlNodeClient client,
    ILogger<ControlNodeHostedService> logger,
    SectlAuthService? authService = null,
    INodeEnrollmentStore? enrollmentStore = null,
    IControlNodeStateStore? stateStore = null,
    IControlPlaneEndpointStore? endpointStore = null,
    TimeSpan? stopTimeout = null) : IHostedService
{
    private readonly SemaphoreSlim _restartGate = new(1, 1);

    /// <summary>等待旧循环退出的上限：进程退出不该被一个卡死的连接挂住。</summary>
    private readonly TimeSpan _stopTimeout = stopTimeout ?? TimeSpan.FromSeconds(5);

    private CancellationTokenSource? _sessionSource;
    private Task? _runTask;
    private bool? _lastSignedIn;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (authService is not null)
        {
            _lastSignedIn = authService.IsSignedIn;
            authService.StateChanged += OnAuthStateChanged;
        }

        if (enrollmentStore is not null)
            enrollmentStore.Changed += OnEnrollmentChanged;

        // 冷启动就把节点通道地址对齐到自建集控的基址：老版本或手工填错过协议时（http:// 的实例配
        // wss:// 的通道），不修这一次就会一直连不上，用户还得先打开设置页才能被救回来。
        NormalizeNodeEndpoint();

        StartClient();
        return Task.CompletedTask;
    }

    /// <summary>把节点通道地址对齐到控制面基址；没有可写状态或不需要修正时什么都不做。</summary>
    private void NormalizeNodeEndpoint()
    {
        if (stateStore is null || endpointStore is null)
            return;

        var resolved = ControlNodeEndpointResolver.Resolve(
            endpointStore.Current,
            stateStore.Current.ServerUrl,
            out var corrected);

        if (!corrected)
            return;

        logger.LogInformation("集控节点通道地址按控制面基址修正：{Endpoint}", resolved);
        stateStore.Update(state => state with { ServerUrl = resolved });
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (authService is not null)
            authService.StateChanged -= OnAuthStateChanged;

        if (enrollmentStore is not null)
            enrollmentStore.Changed -= OnEnrollmentChanged;

        await _restartGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopClientAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _restartGate.Release();
        }
    }

    private void OnAuthStateChanged(object? sender, EventArgs e)
    {
        // 接入令牌在用时，登录状态变化与节点连接无关：不重启（重启只会白白踢掉一条好连接）。
        if (enrollmentStore?.Status.HasToken == true)
            return;

        var signedIn = authService?.IsSignedIn ?? false;
        if (_lastSignedIn == signedIn)
            return;

        _lastSignedIn = signedIn;
        _ = RestartAsync();
    }

    private void OnEnrollmentChanged(object? sender, NodeEnrollmentStatus status)
    {
        // 接入/清除接入都要让连接层立刻看到新凭据（令牌换代），不能等下一次 401。
        if (_lastSignedIn is null)
            _lastSignedIn = authService?.IsSignedIn;

        _ = RestartAsync();
    }

    private async Task RestartAsync()
    {
        try
        {
            await _restartGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopClientAsync(CancellationToken.None).ConfigureAwait(false);
                StartClient();
            }
            finally
            {
                _restartGate.Release();
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "集控节点客户端重启失败。");
        }
    }

    private void StartClient()
    {
        // 下一条循环**挂在上一条后面**，不并行启动：旧循环可能卡在握手/接收上没能及时退出，
        // 而同一 node_id 的两条连接会被服务端互相 replaced，表现就是"刚连上就掉线"的循环。
        var previous = _runTask;
        _sessionSource = new CancellationTokenSource();
        var token = _sessionSource.Token;

        if (previous is { IsCompleted: false })
            logger.LogInformation("集控节点下一条循环会等上一条退出后再启动（避免同 node_id 两条连接互相替换）。");

        _runTask = Task.Run(
            async () =>
            {
                if (previous is not null)
                {
                    try
                    {
                        await previous.ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        // 上一条循环自己已经记过失败日志，这里只等它退出。
                        logger.LogDebug(exception, "集控节点上一条循环以异常结束。");
                    }
                }

                if (token.IsCancellationRequested)
                    return;

                await client.RunAsync(token).ConfigureAwait(false);
            },
            CancellationToken.None);
    }

    private async Task StopClientAsync(CancellationToken cancellationToken)
    {
        if (_sessionSource is null)
            return;

        await _sessionSource.CancelAsync().ConfigureAwait(false);

        if (_runTask is not null)
        {
            // 停止是尽力而为：一个卡在连接/接收上的客户端不应阻塞进程退出。
            // 但超时后**不丢掉 _runTask**：它还活着，StartClient 必须挂在它后面等它退出，
            // 否则同一个 node 会出现两条并行的循环。
            await Task.WhenAny(_runTask, Task.Delay(_stopTimeout, cancellationToken)).ConfigureAwait(false);

            if (!_runTask.IsCompleted)
                logger.LogWarning(
                    "集控节点客户端 {Seconds:0.###} 秒内未停止，下一条循环会等它退出后再启动（同一 node 不会出现两条循环）。",
                    _stopTimeout.TotalSeconds);
        }

        _sessionSource.Dispose();
        _sessionSource = null;
    }
}
