using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SecRandom.Services.Auth;

/// <summary>
/// Keeps the server-side device activity record current while an OAuth session is active.
/// </summary>
public sealed class SectlHeartbeatService(
    SectlAuthService authService,
    ILogger<SectlHeartbeatService> logger) : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly SemaphoreSlim _sendSignal = new(0, 1);

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        authService.StateChanged += AuthServiceOnStateChanged;
        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        authService.StateChanged -= AuthServiceOnStateChanged;
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogDebug("SECTL OAuth heartbeat service started.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await SendHeartbeatIfSignedInAsync(stoppingToken).ConfigureAwait(false);
                await WaitForNextHeartbeatAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            logger.LogDebug("SECTL OAuth heartbeat service stopped.");
        }
    }

    public override void Dispose()
    {
        authService.StateChanged -= AuthServiceOnStateChanged;
        _sendSignal.Dispose();
        base.Dispose();
    }

    private async Task SendHeartbeatIfSignedInAsync(CancellationToken stoppingToken)
    {
        if (!authService.IsSignedIn)
            return;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(RequestTimeout);
            if (!await authService.SendHeartbeatAsync(timeout.Token).ConfigureAwait(false))
                logger.LogDebug("SECTL OAuth heartbeat was rejected or failed.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("SECTL OAuth heartbeat timed out.");
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "SECTL OAuth heartbeat failed.");
        }
    }

    private async Task WaitForNextHeartbeatAsync(CancellationToken stoppingToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Task interval = Task.Delay(HeartbeatInterval, waitCancellation.Token);
        Task signal = _sendSignal.WaitAsync(waitCancellation.Token);
        Task completed = await Task.WhenAny(interval, signal).ConfigureAwait(false);
        if (completed == signal)
            await signal.ConfigureAwait(false);
        waitCancellation.Cancel();
    }

    private void AuthServiceOnStateChanged(object? sender, EventArgs e)
    {
        if (!authService.IsSignedIn)
            return;

        try
        {
            _sendSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A heartbeat is already queued; coalesce additional account state updates.
        }
    }
}
