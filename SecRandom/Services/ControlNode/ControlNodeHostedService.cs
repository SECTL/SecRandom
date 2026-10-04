using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.Auth;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     把集控节点客户端挂到 Host 生命周期上。
/// </summary>
/// <remarks>
///     登录状态变化会重启客户端：退出登录后必须**立刻断开**节点长连接，而不是等下一次
///     401；重新登录则要重新连上。客户端本身也有 30 秒的等待窗口兜底，因此这里只是让它更快收敛。
/// </remarks>
public sealed class ControlNodeHostedService(
    ControlNodeClient client,
    SectlAuthService authService,
    ILogger<ControlNodeHostedService> logger) : IHostedService
{
    private readonly SemaphoreSlim _restartGate = new(1, 1);
    private CancellationTokenSource? _sessionSource;
    private Task? _runTask;
    private bool? _lastSignedIn;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lastSignedIn = authService.IsSignedIn;
        authService.StateChanged += OnAuthStateChanged;
        StartClient();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        authService.StateChanged -= OnAuthStateChanged;

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
        var signedIn = authService.IsSignedIn;
        if (_lastSignedIn == signedIn)
            return;

        _lastSignedIn = signedIn;
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
        _sessionSource = new CancellationTokenSource();
        var token = _sessionSource.Token;
        _runTask = Task.Run(() => client.RunAsync(token), CancellationToken.None);
    }

    private async Task StopClientAsync(CancellationToken cancellationToken)
    {
        if (_sessionSource is null)
            return;

        await _sessionSource.CancelAsync().ConfigureAwait(false);

        if (_runTask is not null)
        {
            // 停止是尽力而为：一个卡在连接/接收上的客户端不应阻塞进程退出。
            await Task.WhenAny(_runTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(false);
        }

        _sessionSource.Dispose();
        _sessionSource = null;
        _runTask = null;
    }
}
