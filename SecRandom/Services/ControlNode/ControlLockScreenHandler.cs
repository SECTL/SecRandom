using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary><c>lockScreen</c>：锁住教室机屏幕，等远程解除、倒计时或本机收回远控权。</summary>
public sealed class ControlLockScreenHandler(
    ControlLockScreenService lockScreen,
    ILogger<ControlLockScreenHandler> logger)
{
    public async Task<ControlCommandOutcome> ExecuteAsync(
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        if (!ControlLockScreenRequest.TryParse(payload, out var request, out var hint)
            || request is null)
        {
            return ControlCommandOutcome.Failure(ControlRejectReasons.InvalidCommand, new
            {
                hint,
                max_message_length = ControlLockScreenRequest.MaxMessageCodePoints
            });
        }

        try
        {
            await lockScreen.LockAsync(request, cancellationToken).ConfigureAwait(false);
            return ControlCommandOutcome.SuccessWith(new
            {
                locked = true,
                unlock_after_seconds = request.UnlockAfterSeconds
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "集控锁屏失败。");
            return ControlCommandOutcome.Failure(ControlRejectReasons.ExecutionFailed);
        }
    }
}