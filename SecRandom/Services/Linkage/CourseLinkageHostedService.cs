using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Avalonia.Threading;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.ViewModels.MainPages;

namespace SecRandom.Services.Linkage;

public sealed class CourseLinkageHostedService(
    CourseLinkageService linkageService,
    IDrawTemporaryRecordService temporaryRecordService,
    ILogger<CourseLinkageHostedService> logger) : BackgroundService
{
    private readonly HashSet<string> _performedResets = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        linkageService.StateChanged += LinkageServiceOnStateChanged;
        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        linkageService.StateChanged -= LinkageServiceOnStateChanged;
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await linkageService.RefreshAsync(stoppingToken).ConfigureAwait(false);
                TryPerformPreClassReset();
                var delay = linkageService.GetNextRefreshDelay();
                using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var nextRefresh = Task.Delay(
                    delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay,
                    waitCancellation.Token);
                var signal = _refreshSignal.WaitAsync(waitCancellation.Token);
                var completed = await Task.WhenAny(nextRefresh, signal).ConfigureAwait(false);
                if (completed == signal)
                    await signal.ConfigureAwait(false);
                waitCancellation.Cancel();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "课程联动调度失败，将在稍后重试。");
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private void LinkageServiceOnStateChanged(object? sender, EventArgs e)
    {
        try
        {
            _refreshSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A refresh is already queued; coalesce additional state notifications.
        }
    }

    private void TryPerformPreClassReset()
    {
        if (!linkageService.IsPreClassResetDue(out var resetKey) || !_performedResets.Add(resetKey))
            return;

        temporaryRecordService.ClearAll();
        Dispatcher.UIThread.Post(() =>
        {
            IAppHost.TryGetService<RollCallPageViewModel>()?.ResetForCourseLinkage();
            IAppHost.TryGetService<QuickDrawPageViewModel>()?.ResetForCourseLinkage();
            IAppHost.TryGetService<LotteryPageViewModel>()?.ResetForCourseLinkage();
        });
        logger.LogInformation("已执行课前联动重置：课程={CourseName}。", linkageService.Snapshot.NextCourse?.Name);
    }
}
