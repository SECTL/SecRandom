using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     <c>settings.read</c>：按设备在 <c>hello</c> 里上报的 schema 读当前值。
/// </summary>
/// <remarks>
///     <para>
///         回执只有 <c>values</c>，且**只含 schema 里有的键**：控制台把 schema 当表单定义，
///         读回一份更大的目录只会让"这台机器允许改哪些设置"出现第二个答案。
///     </para>
///     <para>
///         读设置**不受"正在抽取"限制**：看配置是只读动作，课堂进行中恰恰是最需要看的时候。
///         但仍在 UI 线程取快照——配置对象是界面绑定的，读一半被人改了会给出自相矛盾的值。
///     </para>
/// </remarks>
public sealed class ControlSettingsReadHandler(
    MainConfigHandler config,
    ILogger<ControlSettingsReadHandler> logger)
{
    public async Task<ControlCommandOutcome> ExecuteAsync(
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        _ = payload;

        try
        {
            var values = await Dispatcher.UIThread.InvokeAsync(
                () => ControlSettingsSchemaBuilder.ReadValues(config.Data));

            logger.LogInformation("集控已读取设置：{Count} 项。", values.Count);

            var response = new { values };

            // 预算自查。**必须在返回之前量**：超限的帧在传输层被丢掉，那里除了丢做不了别的，
            // 控制台只会看到一条永远不会到达结果的查询——而"读设置读不到"是无法从日志看出来的。
            var bytes = ControlProtocolJson.MeasureBytes(response);
            if (bytes > ControlProtocolJson.PayloadBudgetBytes)
            {
                logger.LogWarning(
                    "集控读取设置的回执超过载荷预算（{Bytes} > {Budget} 字节），已拒绝而不是发一个超限帧。",
                    bytes, ControlProtocolJson.PayloadBudgetBytes);

                return ControlCommandOutcome.Failure("payload_too_large", new
                {
                    limit = ControlProtocolJson.MaxFrameBytes,
                    budget = ControlProtocolJson.PayloadBudgetBytes,
                    bytes
                });
            }

            return ControlCommandOutcome.SuccessWith(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "集控读取设置失败。");
            return ControlCommandOutcome.Failure(ControlRejectReasons.ExecutionFailed);
        }
    }
}