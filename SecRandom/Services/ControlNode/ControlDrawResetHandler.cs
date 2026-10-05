using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     <c>draw.reset</c>：清空"本轮临时记录"，让下一轮从头开始。
/// </summary>
/// <remarks>
///     <para>
///         <b>只清临时记录，绝不碰历史。</b>临时记录是"这一轮谁已经被抽到过"
///         （<see cref="IDrawTemporaryRecordService" />，落在 <c>data/list/temporary*</c> 一类的进度文件里），
///         历史记录是名单的长期账本（<c>data/history/**</c>）。远程"重置"在控制台看来很像"清记录"，
///         所以这条边界必须写在代码、测试与 UI 文案三处：清掉的只是进度，历史一个字节都不动。
///     </para>
///     <para>
///         走 <c>Reset*</c> 而不是 <c>Clear*</c>：<c>Reset</c> 是"把这一份的进度原子覆盖为空"，
///         <c>Clear*</c> 那套带破坏性语义（删除文件/只清一次），不是这个能力要表达的意思。
///     </para>
///     <para>
///         必须在 UI 线程执行：临时记录服务被界面绑定（点名/抽奖页的剩余数量就是它算出来的），
///         从线程池改它会让绑定层收到别的线程发来的通知。
///     </para>
/// </remarks>
public sealed class ControlDrawResetHandler(
    IDrawTemporaryRecordService temporaryRecords,
    IProfileCatalogManager catalog,
    ILogger<ControlDrawResetHandler> logger)
{
    public async Task<ControlCommandOutcome> ExecuteAsync(
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        if (!ControlDrawResetRequest.TryParse(payload, out var request, out var reason))
        {
            return ControlCommandOutcome.Failure(
                reason,
                new
                {
                    supported_targets = new[]
                    {
                        ControlDrawResetRequest.TargetRollCall,
                        ControlDrawResetRequest.TargetQuick,
                        ControlDrawResetRequest.TargetLottery
                    }
                });
        }

        try
        {
            return await Dispatcher.UIThread.InvokeAsync(() => Apply(request));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "集控远程重置执行失败。");
            return ControlCommandOutcome.Failure(ControlRejectReasons.ExecutionFailed);
        }
    }

    /// <summary>
    ///     真正执行重置（同步、无界面依赖，便于单测直接调用）。
    /// </summary>
    /// <remarks>
    ///     给了 <c>list_name</c> 就只清那一份；没给就把这一类**全部名单的桶**都清掉。
    ///     <c>cleared</c> 报的是清掉的临时记录条数——它是"这一轮抽到过几个人/几个奖品"，
    ///     与历史记录条数无关。
    /// </remarks>
    public ControlCommandOutcome Apply(ControlDrawResetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var listName = request.ListName;
        if (listName is not null && !Exists(request, listName))
        {
            return ControlCommandOutcome.Failure(
                "invalid_value:list_name:not_found",
                new { field = "list_name", why = "not_found" });
        }

        var names = listName is not null
            ? [listName]
            : request.ClearsPrizes
                ? catalog.GetPrizeListNames()
                : catalog.GetStudentListNames();

        var cleared = 0;
        foreach (var name in names)
        {
            cleared += request.ClearsPrizes ? ResetPrizes(name) : ResetStudents(name);
        }

        logger.LogInformation(
            "集控远程重置：目标={Target}，名单={List}，清掉临时记录={Cleared}",
            request.Target,
            listName ?? "(全部)",
            cleared);

        // 成功回执按协议给 detail；同时把"不动历史"写进 detail，免得消费方猜。
        return new ControlCommandOutcome(
            true,
            null,
            ControlCommandOutcome.ToDetail(new ControlDrawResetDetail(request.Target, listName, cleared)));
    }

    private bool Exists(ControlDrawResetRequest request, string listName) =>
        request.ClearsPrizes ? catalog.PrizeListExists(listName) : catalog.StudentListExists(listName);

    private int ResetStudents(string listName)
    {
        // 空的条件＝整份名单的桶（临时记录只属于名单，不属于某次 (性别, 分组) 过滤）。
        var counts = temporaryRecords.GetStudentCounts(listName, string.Empty, string.Empty);
        var cleared = counts.Values.Sum();
        temporaryRecords.ResetStudentList(listName);
        return cleared;
    }

    private int ResetPrizes(string listName)
    {
        var counts = temporaryRecords.GetPrizeCounts(listName);
        var cleared = counts.Values.Sum();
        temporaryRecords.ResetPrizeList(listName);
        return cleared;
    }
}
