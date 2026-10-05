using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.Notification;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     <c>media.play</c>：让这台教室机播报一句话，可选"先亮结果窗"与两个**临时**音量。
/// </summary>
/// <remarks>
///     <para>
///         载荷的格式与限制在 <see cref="ControlMediaPlayRequest" />（Core，可单测）；
///         "本机做不到的选项"在 <see cref="ControlMediaPlayPlatformSupport" />；
///         临时音量在 <see cref="TemporaryVoiceVolume" />。这里只负责执行顺序与线程。
///     </para>
///     <para>
///         执行顺序（§4.5.7）：解析 → 这台机器愿不愿意出声 → 有没有做不到的选项 →
///         **先显示窗口** → 应用临时音量 → 播报 → <c>finally</c> 恢复音量。
///         每一步都排在"产生副作用之前"该在的位置：拒绝必须发生在任何副作用之前，
///         否则教室里会先看到一次没人预期、也没人解释的弹窗。
///     </para>
/// </remarks>
public sealed class ControlMediaPlayHandler(
    IVoiceAnnouncementService voice,
    MainConfigHandler config,
    NotificationService notifications,
    ILogger<ControlMediaPlayHandler> logger)
{
    public async Task<ControlCommandOutcome> ExecuteAsync(
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        if (!ControlMediaPlayRequest.TryParse(payload, out var request, out var reason, out var hint) || request is null)
        {
            // 解析失败也有话说：文本超长要告诉管理员**超了多少**，动作不支持要告诉他
            // **支持哪些**，选项给了坏值要告诉他**是哪个字段、允许什么**。否则控制台上只有一个码，
            // 等于让人自己猜协议。
            //
            // hint 为 null 时整个键不会写出（ControlProtocolJson 的 WhenWritingNull 约定），
            // 所以既有的拒绝回执一字不变。
            return ControlCommandOutcome.Failure(
                reason,
                new
                {
                    supported_actions = new[] { ControlMediaPlayRequest.AnnounceAction },
                    max_text_length = ControlMediaPlayRequest.MaxTextLength,
                    hint
                });
        }

        // 老师关掉了语音＝这台机器不再出声。此时回"成功"会让控制台以为教室里已经播了，
        // 而真实情况是没人听到——远程操作最忌讳这种沉默失败。
        //
        // ⚠️ 必须**带回是哪一项关着**：只回 media_disabled 的话，管理员在控制台上
        // 只看到一个词，既不知道是设备没装语音、还是被谁关了，也不知道该怎么办。
        // 这个 detail 正好够控制台给出"打开语音"的下一步。
        if (!config.Data.VoiceSettings.VoiceEnable)
        {
            return ControlCommandOutcome.Failure(
                "media_disabled",
                new
                {
                    voice_enable = false,
                    // 打开它的写法直接给出来：控制台不需要自己拼协议路径。
                    fix = new { capability = "settings.write", patch = new Dictionary<string, object?> { ["voice.enable"] = true } }
                });
        }

        // "载荷合法但本机做不到"的选项必须在**任何副作用之前**拒绝：先开窗口再回 unsupported，
        // 教室里就会先看到一次没人预期、也没人解释的弹窗。
        //
        // 排在 media_disabled **之后**是有意的：这台机器压根不出声时，"先把语音打开"才是
        // 操作员该做的第一步，比起"这个字段本机不支持"更靠近问题本身。
        var unsupportedField = ControlMediaPlayPlatformSupport.FindUnsupportedField(
            request,
            ControlMediaPlayPlatformSupport.SystemVolumeSupported);
        if (unsupportedField is not null)
        {
            logger.LogWarning("集控播报拒绝：本机不支持字段 {Field}。", unsupportedField);
            return ControlCommandOutcome.Failure(
                ControlRejectReasons.InvalidCommand,
                new { unsupported_field = unsupportedField });
        }

        TemporaryVoiceVolume? temporaryVolume = null;
        try
        {
            // 顺序是协议要求（§4.5.7）：**先让教室里看见，再让教室里听见**。
            //
            // 窗口走 UI 线程：它要创建/激活窗口并驱动结果动画，在线程池线程上碰界面会让绑定层
            // 收到别的线程发来的通知（集控命令正是在线程池上执行的），照 ControlPageDrawExecutor 的写法。
            if (request.ShowQuickDrawWindow == true)
            {
                await Dispatcher.UIThread.InvokeAsync(() => notifications.ShowQuickDrawText(request.Text))
                    .GetTask()
                    .ConfigureAwait(false);

                // 窗口没开起来就让整条命令失败（下面的 catch 会回 execution_failed）：
                // 协议里没有"播报成功但窗口没开"这种半成功，回 ok 会让控制台以为全班看见了。
                logger.LogInformation("集控播报前已请求显示闪抽结果窗（{Length} 字）。", request.Text.Length);
            }

            // 音量在**播报前一刻**才动：窗口显示要先排队/布局，早改只会拉长"配置里的值
            // 与真实值不一致"的窗口期（语音设置页面开着时，那段窗口期里的改动会被那个页面写盘）。
            //
            // system_volume_percent 不在这里：本机没有实现，上面已经明确拒绝了。
            // 将来平台实现落地（ControlMediaPlayPlatformSupport.SystemVolumeSupported 打开）时，
            // 系统音量也要在这里应用、并在同一个 finally 里恢复原值——否则一次远程播报会把
            // 教室机的系统音量永久留在临时值上。
            temporaryVolume = TemporaryVoiceVolume.Apply(
                config.Data.VoiceSettings,
                request.VoiceVolumePercent);

            // ⚠️ 带音量选项时**必须等播报结束**再继续：
            //    VoiceAnnouncementService.SpeakAsync(waitForCompletion: false) 在
            //    VoiceWaitComplete == false 时会**立刻返回**（音频在后台播），这时 finally
            //    里的恢复会把音量改回原值，正在播的那一段就跟着变了音——"播完恢复"根本无从谈起。
            //    所以只要带了临时音量，就显式走"等朗读结束"那条路，不管 VoiceWaitComplete 是什么。
            //    （不带音量时保持老行为：不等朗读结束，一条命令不被十几秒的朗读占住。）
            await voice.SpeakAsync(
                    request.Text,
                    waitForCompletion: request.HasTemporaryVolume,
                    cancellationToken)
                .ConfigureAwait(false);

            // 只记长度，不记正文：播报内容多半是课堂口令，没必要落进日志文件。
            if (request.HasTemporaryVolume)
                logger.LogInformation("集控播报已完成（{Length} 字，临时音量将在返回前恢复）。", request.Text.Length);
            else
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
        finally
        {
            // 无条件恢复：朗读抛异常、TTS 失败、命令被取消、窗口没开起来——都不能把这台机器
            // 留在临时音量上。TemporaryVoiceVolume 自己是幂等的，没给过音量时也一个字都不动。
            temporaryVolume?.Dispose();
        }
    }
}
