using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary><c>restartClient</c>：重启这台教室机上的客户端。</summary>
/// <remarks>
///     <para>
///         <b>必须先回执、再重启</b>：进程一退出，回执就再也发不出去，控制台上只会显示超时。
///         因此这里把重启排进后台延时任务，方法本身立刻返回成功；延时至少 1 秒，给发送队列留出时间。
///     </para>
///     <para>
///         重启路径**不经过交互式安全验证**：远程命令不可能有人在教室机前点确认。
///         真正的保护是"抽取进行中拒绝重启"（分发层的 <c>busy</c>），避免把正在展示的结果打断。
///     </para>
/// </remarks>
public sealed class ControlRestartHandler(ILogger<ControlRestartHandler> logger)
{
    public Task<ControlCommandOutcome> ExecuteAsync(
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        if (!ControlRestartRequest.TryParse(payload, out var request, out var hint) || request is null)
        {
            return Task.FromResult<ControlCommandOutcome>(ControlCommandOutcome.Failure(
                ControlRejectReasons.InvalidCommand,
                new { hint, max_delay_seconds = ControlRestartRequest.MaxDelaySeconds }));
        }

        var delaySeconds = Math.Max(1, request.DelaySeconds);
        _ = RestartAfterDelayAsync(delaySeconds);

        return Task.FromResult<ControlCommandOutcome>(ControlCommandOutcome.SuccessWith(new
        {
            restart_in_seconds = delaySeconds
        }));
    }

    private async Task RestartAfterDelayAsync(int delaySeconds)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                logger.LogInformation("执行集控重启客户端。");
                App.Current.Restart();
            }).GetTask().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "集控重启客户端失败。");
        }
    }
}