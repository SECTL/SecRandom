using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary><c>broadcast</c>：把一段文本全屏显示在这台教室机上。</summary>
public sealed class ControlBroadcastHandler(
    ControlBroadcastPresenter presenter,
    ILogger<ControlBroadcastHandler> logger)
{
    public async Task<ControlCommandOutcome> ExecuteAsync(
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        if (!ControlBroadcastRequest.TryParse(payload, out var request, out var hint) || request is null)
        {
            return ControlCommandOutcome.Failure(ControlRejectReasons.InvalidCommand, new
            {
                hint,
                max_text_length = ControlBroadcastRequest.MaxTextCodePoints
            });
        }

        try
        {
            await presenter.ShowAsync(request.Text, request.Seconds, cancellationToken).ConfigureAwait(false);
            return ControlCommandOutcome.SuccessWith(new
            {
                seconds = request.Seconds,
                text_length = ControlTextLimits.CountCodePoints(request.Text)
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "集控广播显示失败。");
            return ControlCommandOutcome.Failure(ControlRejectReasons.ExecutionFailed);
        }
    }
}