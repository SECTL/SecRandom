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
///     <c>roster.write</c>：把服务端的一份名单落到这台机器上。
/// </summary>
/// <remarks>
///     <para>
///         载荷解析与合并规则在 <see cref="ControlRosterPushRequest" /> / <see cref="ControlRosterMerge" />
///         （Core，可单测）。这里负责有副作用的部分：
///     </para>
///     <list type="bullet">
///         <item>写之前先原子备份上一版——回滚靠它，而不是靠"记得改了什么"。</item>
///         <item><c>activate</c> 默认 <c>false</c>：换名单**不会**顺手切换本机当前点名名单，
///         免得正在上课的机器被远程换人。</item>
///         <item>名单名走与本地导入完全相同的校验，杜绝远程把文件写到名单目录之外。</item>
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
                    max_students = ControlRosterPushRequest.MaxStudents
                });
        }

        try
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ProfileConfigBase.IsValidProfileName(request.ListName))
                    return ControlCommandOutcome.Failure("invalid_list_name", new { list_name = request.ListName });

                var backupPath = BackupExistingList(request.ListName);

                var students = string.Equals(request.Mode, RosterWriteModes.Merge, StringComparison.Ordinal)
                    ? ControlRosterMerge.Merge(catalog.LoadStudentList(request.ListName), request.Students)
                    : request.Students;

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
            });
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

    /// <summary>把现有名单文件复制到 <c>data/backup/roster</c>，返回备份路径（原本不存在时为 null）。</summary>
    private string? BackupExistingList(string listName)
    {
        try
        {
            var source = Utils.GetFilePath("list", "roll_call_list", $"{listName}.json");
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
