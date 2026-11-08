using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Core.Services.SimpleMode;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.ControlNode.V4;

namespace SecRandom.Services.ControlNode;

/// <summary><c>switchPage</c>：把教室机主界面切到点名/抽奖页。</summary>
/// <remarks>
///     <para>
///         <b>快抽不是一个主界面页</b>：它是独立浮窗，打开它的入口会直接开始一次抽取。
///         远程"切页"必须是纯导航，不能用一个会顺手开抽的入口来冒充，所以 <c>quickDraw</c>
///         明确回 <c>page_unavailable</c>，把"这台机器没有这个页面"讲清楚。
///     </para>
///     <para>
///         抽奖页被关闭（<see cref="IFeatureAvailabilityService.IsLotteryEnabled" />）时同样回
///         <c>page_unavailable</c>：请求一个不存在的页面不是执行失败，控制台可以据此改发点名。
///     </para>
/// </remarks>
public sealed class ControlSwitchPageHandler(ILogger<ControlSwitchPageHandler> logger)
{
    private const string RollCallPageId = "main.rollCall";
    private const string LotteryPageId = "main.lottery";

    public async Task<ControlCommandOutcome> ExecuteAsync(
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        if (!ControlSwitchPageRequest.TryParse(payload, out var request, out var hint) || request is null)
        {
            return ControlCommandOutcome.Failure(ControlRejectReasons.InvalidCommand, new
            {
                hint,
                supported_pages = ControlSwitchPageRequest.SupportedPages
            });
        }

        var pageId = request.Page switch
        {
            ControlSwitchPageRequest.RollCallPage => RollCallPageId,
            ControlSwitchPageRequest.LotteryPage => LotteryPageId,
            _ => null
        };

        if (pageId is null)
        {
            return ControlCommandOutcome.Failure(ControlV4Protocol.ErrorCodes.PageUnavailable, new
            {
                page = request.Page,
                supported_pages = new[] { ControlSwitchPageRequest.RollCallPage, ControlSwitchPageRequest.LotteryPage },
                reason = "quick_draw_is_a_floating_window"
            });
        }

        if (pageId == LotteryPageId
            && IAppHost.GetService<IFeatureAvailabilityService>() is { IsLotteryEnabled: false })
        {
            return ControlCommandOutcome.Failure(ControlV4Protocol.ErrorCodes.PageUnavailable, new
            {
                page = request.Page,
                supported_pages = new[] { ControlSwitchPageRequest.RollCallPage },
                reason = "lottery_disabled"
            });
        }

        try
        {
            await Dispatcher.UIThread
                .InvokeAsync(() => App.ShowMainWindowForRemoteDrawAsync(pageId))
                .ConfigureAwait(false);

            // 简洁模式会把请求归一到默认页；这里报告**实际**切过去的页，控制台就不会以为抽奖页被打开了。
            var reportedPage = SimpleModePolicy.NormalizeMainPageId(pageId);
            logger.LogInformation("集控切页：{Requested} → {Actual}", request.Page, reportedPage);

            return ControlCommandOutcome.SuccessWith(new
            {
                page = request.Page,
                page_id = reportedPage
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "集控切页失败：{Page}", request.Page);
            return ControlCommandOutcome.Failure(ControlRejectReasons.ExecutionFailed);
        }
    }
}

