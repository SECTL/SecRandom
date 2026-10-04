using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     <c>roster.read</c>：把本机的点名名单 / 抽奖奖池读给控制台。
/// </summary>
/// <remarks>
///     <para>
///         名单含学生姓名，所以这是**权限最高的一条只读通道**（服务端按 Admin 授权）。
///         读操作本身不改任何东西，切名单、切奖池都不会发生：控制台要的是"这台机器上有什么"。
///     </para>
///     <para>
///         返回按帧上限截断（<c>truncated</c>），不尝试分页：控制台要做的是"看到有哪些名单、
///         大概多少人"，需要全量明细时应该用导出，而不是把几千个名字塞进一条命令。
///     </para>
/// </remarks>
public sealed class ControlRosterReadHandler(
    IProfileCatalogManager catalog,
    MainConfigHandler config,
    ILogger<ControlRosterReadHandler> logger)
{
    public async Task<ControlCommandOutcome> ExecuteAsync(
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        if (!ControlRosterReadRequest.TryParse(payload, out var request, out var reason) || request is null)
        {
            return ControlCommandOutcome.Failure(
                reason,
                new
                {
                    supported_roster_kinds = new[]
                    {
                        ControlRosterReadRequest.Students, ControlRosterReadRequest.Prizes
                    }
                });
        }

        try
        {
            // 读文件走 UI 线程之外也行，但名单服务与配置对象都是界面绑定的 ObservableObject，
            // 统一在 UI 线程上取快照，避免"读的时候正在被界面改"的半截状态。
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var students = string.Equals(
                    request.RosterKind, ControlRosterReadRequest.Students, StringComparison.Ordinal);

                var lists = students ? ReadStudentLists(request) : ReadPrizeLists(request);
                var response = ControlRosterReadResponse.TrimToBudget(
                    new ControlRosterReadResponse(request.RosterKind, lists));

                logger.LogInformation(
                    "集控已读取名单：{Kind}（{Lists} 份，{Members} 名成员，按名单过滤：{Filter}）",
                    request.RosterKind,
                    response.Lists.Count,
                    response.Lists.Sum(list => list.Members.Count),
                    request.ListName ?? "无");

                return ControlCommandOutcome.SuccessWith(response);
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "集控读取名单失败。");
            return ControlCommandOutcome.Failure(ControlRejectReasons.ExecutionFailed);
        }
    }

    private List<ControlRosterListPayload> ReadStudentLists(ControlRosterReadRequest request)
    {
        var defaultName = config.Data.RollCallSettings.DefaultClass;
        var payloads = new List<ControlRosterListPayload>();

        foreach (var name in catalog.GetStudentListNames())
        {
            if (request.ListName is { } filter && !string.Equals(filter, name, StringComparison.Ordinal))
                continue;

            if (payloads.Count >= ControlRosterReadRequest.MaxLists)
                break;

            var list = catalog.LoadStudentList(name);
            if (list is null)
                continue;

            var candidates = list.Students
                .Where(student => request.IncludeDisabled || student.Exists)
                .ToList();

            var members = candidates
                .Take(ControlRosterReadRequest.MaxMembersPerList)
                .Select(ControlRosterMemberPayload.FromStudent)
                .ToList();

            payloads.Add(new ControlRosterListPayload(
                name,
                string.Equals(defaultName, name, StringComparison.Ordinal),
                members.Count,
                candidates.Count,
                candidates.Count > members.Count,
                members));
        }

        return payloads;
    }

    private List<ControlRosterListPayload> ReadPrizeLists(ControlRosterReadRequest request)
    {
        var defaultPool = config.Data.LotterySettings.DefaultPool;
        var payloads = new List<ControlRosterListPayload>();

        foreach (var name in catalog.GetPrizeListNames())
        {
            if (request.ListName is { } filter && !string.Equals(filter, name, StringComparison.Ordinal))
                continue;

            if (payloads.Count >= ControlRosterReadRequest.MaxLists)
                break;

            var list = catalog.LoadPrizeList(name);
            if (list is null)
                continue;

            var candidates = list.Prizes
                .Where(prize => request.IncludeDisabled || prize.Exists)
                .ToList();

            var members = candidates
                .Take(ControlRosterReadRequest.MaxMembersPerList)
                .Select(ControlRosterMemberPayload.FromPrize)
                .ToList();

            payloads.Add(new ControlRosterListPayload(
                name,
                string.Equals(defaultPool, name, StringComparison.Ordinal),
                members.Count,
                candidates.Count,
                candidates.Count > members.Count,
                members));
        }

        return payloads;
    }
}
