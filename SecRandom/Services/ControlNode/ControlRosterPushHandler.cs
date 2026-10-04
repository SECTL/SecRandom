using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     <c>roster.write</c>：把服务端的一份名单或奖池落到这台机器上。
/// </summary>
/// <remarks>
///     <para>
///         载荷解析与合并规则在 <see cref="ControlRosterPushRequest" /> / <see cref="ControlRosterMerge" />
///         （Core，可单测）。这里负责有副作用的部分：
///     </para>
///     <list type="bullet">
///         <item>写之前先原子备份上一版——回滚靠它，而不是靠"记得改了什么"。</item>
///         <item><c>activate</c> 默认 <c>false</c>：换名单**不会**顺手切换本机当前点名名单（或奖池），
///         免得正在上课的机器被远程换人。</item>
///         <item>名单名走与本地导入完全相同的校验，杜绝远程把文件写到名单目录之外。</item>
///         <item>点名名单写 <c>DefaultClass</c>、奖池写 <c>DefaultPool</c>：两者各归各位，
///         把奖池名写进默认班级会让下一次点名面对一个空名单。</item>
///     </list>
/// </remarks>
public sealed class ControlRosterPushHandler(
    IProfileCatalogManager catalog,
    IProfileService profileService,
    ILogger<ControlRosterPushHandler> logger)
{
    public async Task<ControlCommandOutcome> ExecuteAsync(
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        if (!ControlRosterPushRequest.TryParse(payload, out var request, out var reason) || request is null)
        {
            return ControlCommandOutcome.Failure(
                reason,
                new
                {
                    supported_modes = new[] { RosterWriteModes.Replace, RosterWriteModes.Merge },
                    supported_roster_kinds = new[]
                    {
                        ControlRosterPushRequest.StudentsKind, ControlRosterPushRequest.PrizesKind
                    },
                    max_students = ControlRosterPushRequest.MaxStudents
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
            logger.LogWarning(exception, "集控下发名单执行失败。");
            return ControlCommandOutcome.Failure(ControlRejectReasons.ExecutionFailed);
        }
    }

    /// <summary>把一份**已解析**的载荷落到本机的名单或奖池上，返回回执。</summary>
    /// <remarks>
    ///     <para>
    ///         <b>必须在 UI 线程上调用</b>：名单与配置对象都是界面绑定的 ObservableObject，
    ///         在别的线程上改它们会把界面绑到错误的线程上。投递由 <see cref="ExecuteAsync" /> 负责，
    ///         这里只做判断与落盘。
    ///     </para>
    ///     <para>
    ///         之所以把这一步单独露出来：单测进程里没有 Avalonia 消息循环，而**跨线程投递的作业
    ///         只有 Dispatcher 自己那根线程能执行**（<c>RunJobs</c> 也会在别的线程上抛
    ///         "different thread owns it"）。生产路径一行投递、测试路径直接调它，
    ///         比在测试里伪造一根 UI 线程可靠得多。
    ///     </para>
    /// </remarks>
    public ControlCommandOutcome Apply(ControlRosterPushRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ProfileConfigBase.IsValidProfileName(request.ListName))
            return ControlCommandOutcome.Failure("invalid_list_name", new { list_name = request.ListName });

        // 备份要按名单类型取源文件：奖池在 lottery_list 下、点名名单在 roll_call_list 下，
        // 但备份目录与文件名规则两条通道共用（docs/client-protocol.md §4.5.6）。
        var backupPath = BackupExistingList(request.ListName, request.IsPrizeRoster);

        return request.IsPrizeRoster
            ? ApplyPrizes(request, backupPath)
            : ApplyStudents(request, backupPath);
    }

    /// <summary>落一份点名名单（<c>replace</c> 重建 / <c>merge</c> 合并），需要时切成当前点名名单。</summary>
    private ControlCommandOutcome ApplyStudents(ControlRosterPushRequest request, string? backupPath)
    {
        // replace 是按载荷重建整份名单，所以没下发 tags 的行在设备上就是没有标签：
        // ToStudent 把它显式落成空串。merge 走 Merge，只在本次下发了标签时才覆盖。
        var students = string.Equals(request.Mode, RosterWriteModes.Merge, StringComparison.Ordinal)
            ? ControlRosterMerge.Merge(catalog.LoadStudentList(request.ListName), request.Students)
            : request.Students.Select(student => student.ToStudent()).ToList();

        if (!catalog.ReplaceStudents(request.ListName, students))
        {
            return ControlCommandOutcome.Failure(
                "roster_write_failed",
                new { list_name = request.ListName, count = students.Count });
        }

        if (request.Activate)
        {
            catalog.SetDefaultStudentList(request.ListName);
            profileService.LoadStudentProfile(request.ListName);
        }

        logger.LogInformation(
            "集控已下发名单：{List}（{Mode}，{Count} 人，备份 {Backup}，已激活 {Activate}）",
            request.ListName, request.Mode, students.Count, backupPath ?? "无", request.Activate);

        return ControlCommandOutcome.Success;
    }

    /// <summary>落一份奖池（同一条能力，数组名换成 <c>prizes</c>），需要时切成当前奖池。</summary>
    /// <remarks>与 <see cref="ApplyStudents" /> 逐条对应：备份 → 重建/合并 → 激活。</remarks>
    private ControlCommandOutcome ApplyPrizes(ControlRosterPushRequest request, string? backupPath)
    {
        // 载荷类型上 Prizes 可空（点名载荷没有这个数组），走到这里必定是奖池载荷。
        var incoming = request.Prizes ?? [];

        // 与名单同一条规则：没下发 tags 的奖品在 replace 下就是没有标签，merge 下才保留原值。
        var prizes = string.Equals(request.Mode, RosterWriteModes.Merge, StringComparison.Ordinal)
            ? ControlRosterMerge.MergePrizes(catalog.LoadPrizeList(request.ListName), incoming)
            : incoming.Select(prize => prize.ToPrize()).ToList();

        if (!catalog.ReplacePrizes(request.ListName, prizes))
        {
            return ControlCommandOutcome.Failure(
                "roster_write_failed",
                new { list_name = request.ListName, count = prizes.Count });
        }

        if (request.Activate)
        {
            catalog.SetDefaultPrizePool(request.ListName);
            profileService.LoadPrizeProfile(request.ListName);
        }

        logger.LogInformation(
            "集控已下发奖池：{List}（{Mode}，{Count} 项奖品，备份 {Backup}，已激活 {Activate}）",
            request.ListName, request.Mode, prizes.Count, backupPath ?? "无", request.Activate);

        return ControlCommandOutcome.Success;
    }

    /// <summary>把现有名单文件复制到 <c>data/backup/roster</c>，返回备份路径（原本不存在时为 null）。</summary>
    /// <param name="prizeRoster">奖池存在 <c>lottery_list</c> 下，点名名单存在 <c>roll_call_list</c> 下。</param>
    private string? BackupExistingList(string listName, bool prizeRoster)
    {
        try
        {
            var source = Utils.GetFilePath(
                "list", prizeRoster ? "lottery_list" : "roll_call_list", $"{listName}.json");
            if (!File.Exists(source))
                return null;

            var target = Utils.GetFilePath(
                "backup", "roster", $"{Sanitize(listName)}_{DateTime.Now:yyyyMMdd-HHmmss}.json");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
            return target;
        }
        catch (Exception exception)
        {
            // 备份失败不阻断写入（名单本身仍然有效），但**必须留痕**：
            // 出问题时"这次没有备份"要能在日志里被看见。
            logger.LogWarning(exception, "集控下发名单前的备份失败：{List}", listName);
            return null;
        }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string([.. name.Select(character => invalid.Contains(character) ? '_' : character)]);
    }
}
