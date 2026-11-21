using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     <c>roster.list</c>：把本机有哪些点名名单 / 奖池的名称读给控制台。
/// </summary>
/// <remarks>
///     <para>
///         与 <c>roster.read</c> 的区别是**只回名称**：这是操作员（operator）选名单所需的最小信息，
///         成员明细仍然按 admin 授权。权限在服务端 <c>authz.mjs</c> 的能力表里，处理器不做角色判断。
///     </para>
///     <para>
///         与 <c>roster.read</c> 一样在 UI 线程上取快照：名单服务与配置对象是界面绑定的 ObservableObject，
///         边读边被界面改会拿到半截状态。
///     </para>
/// </remarks>
public sealed class ControlRosterListHandler(
    IProfileCatalogManager catalog,
    MainConfigHandler config,
    ILogger<ControlRosterListHandler> logger)
{
    public async Task<ControlCommandOutcome> ExecuteAsync(
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        // 命令没有参数：允许 null 与空对象（服务端校验后下发的是 `{}`），其余载荷明确拒绝而不是"当作没传"。
        if (payload is { } element && element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            && (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Any()))
        {
            return ControlCommandOutcome.Failure(ControlRejectReasons.InvalidCommand, new
            {
                reason = "unexpected_payload"
            });
        }

        try
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var rollCall = ControlRosterListResponse.Normalize(catalog.GetStudentListNames());
                var lottery = ControlRosterListResponse.Normalize(catalog.GetPrizeListNames());

                var response = new ControlRosterListResponse(
                    rollCall,
                    lottery,
                    ResolveDefault(config.Data.RollCallSettings.DefaultClass, rollCall),
                    ResolveDefault(config.Data.LotterySettings.DefaultPool, lottery));

                logger.LogInformation(
                    "集控已列出名单名称：{RollCall} 份点名名单、{Lottery} 个奖池。",
                    response.RollCall.Count,
                    response.Lottery.Count);

                return ControlCommandOutcome.SuccessWith(response);
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "集控列出名单名称失败。");
            return ControlCommandOutcome.Failure(ControlRejectReasons.ExecutionFailed);
        }
    }

    /// <summary>默认名单只回**确实存在**的那一个，配置里残留的旧名字不往控制台传。</summary>
    private static string? ResolveDefault(string? configured, IReadOnlyList<string> names)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;

        return names.Contains(configured, StringComparer.Ordinal) ? configured : null;
    }
}