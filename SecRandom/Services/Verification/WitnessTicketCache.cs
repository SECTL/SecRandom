using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SecRandom.Services.Verification;

/// <summary>
/// Holds one pre-signed server nonce so a visible draw never waits for a network request.
/// </summary>
public sealed class WitnessTicketCache(
    IWitnessClient witnessClient,
    ILogger<WitnessTicketCache> logger) : BackgroundService
{
    private readonly SemaphoreSlim _refresh = new(0, 1);
    private TicketLease? _ticket;

    public bool TryTake(out TicketLease ticket)
    {
        var current = Interlocked.Exchange(ref _ticket, null);
        if (current is not null && current.Ticket.ExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(2))
        {
            ticket = current;
            RequestRefresh();
            return true;
        }

        ticket = null!;
        RequestRefresh();
        return false;
    }

    public void RequestRefresh()
    {
        if (_ticket is null)
        {
            try
            {
                _refresh.Release();
            }
            catch (SemaphoreFullException)
            {
                // A refresh request is already queued.
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_ticket is null)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    var (ticket, token) = await witnessClient.CreateTicketAsync(timeout.Token).ConfigureAwait(false);
                    Interlocked.CompareExchange(ref _ticket, new TicketLease(ticket, token), null);
                    continue;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "服务器见证票据预取失败，将稍后重试。");
                }
            }

            try
            {
                await Task.WhenAny(
                    _refresh.WaitAsync(stoppingToken),
                    Task.Delay(TimeSpan.FromSeconds(15), stoppingToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}

public sealed record TicketLease(WitnessTicket Ticket, string Token);
