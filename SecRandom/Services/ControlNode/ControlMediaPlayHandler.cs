using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     <c>media.play</c>：让这台教室机播报一句话。
/// </summary>
/// <remarks>
///     载荷的解析与限制在 <see cref="ControlMediaPlayRequest" />（Core，可单测）；
///     这里只负责"这台机器此刻能不能出声"和真正的播报动作。
/// </remarks>
public sealed class ControlMediaPlayHandler(
    IVoiceAnnouncementService voice,
    MainConfigHandler config,
    ILogger<ControlMediaPlayHandler> logger)
{
    public async Task<ControlCommandOutcome> ExecuteAsync(
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        if (!ControlMediaPlayRequest.TryParse(payload, out var request, out var reason) || request is null)
            return ControlCommandOutcome.Failure(reason);

        // 老师关掉了语音＝这台机器不再出声。此时回"成功"会让控制台以为教室里已经播了，
        // 而真实情况是没人听到——远程操作最忌讳这种沉默失败。
        if (!config.Data.VoiceSettings.VoiceEnable)
            return ControlCommandOutcome.Failure("media_disabled");

        try
        {
            // 不等朗读结束：一条命令不该被一段十几秒的朗读占住（结果对控制台也不重要）。
            // 返回成功表示**已开始播报**，播放本身失败由语音服务记录。
            await voice.SpeakAsync(request.Text, waitForCompletion: false, cancellationToken)
                .ConfigureAwait(false);

            // 只记长度，不记正文：播报内容多半是课堂口令，没必要落进日志文件。
            logger.LogInformation("集控播报已开始（{Length} 字）。", request.Text.Length);
            return ControlCommandOutcome.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "集控播报执行失败。");
            return ControlCommandOutcome.Failure(ControlRejectReasons.ExecutionFailed);
        }
    }
}
