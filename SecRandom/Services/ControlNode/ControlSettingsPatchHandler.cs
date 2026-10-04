using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     <c>settings.write</c>：改这台机器的设置，**只允许白名单内的路径**。
/// </summary>
/// <remarks>
///     <para>
///         允许哪些路径、值怎么校验，全部在 <see cref="ControlSettingsWhitelist" />（Core，可单测）里；
///         这里只负责三件有副作用的事：<b>先整体校验、回到 UI 线程应用、走配置处理器保存</b>。
///     </para>
///     <para>
///         安全设置、集控自身设置、桌面集成、更新设置不在白名单里，且**永远不进**：
///         前两者是设备所有权（能关掉密码／能把自己接到别的组、把地址指向别的服务器），
///         后两者是持久化与运行面入口（远程开自启＝远程拿到开机启动权）。
///     </para>
/// </remarks>
public sealed class ControlSettingsPatchHandler(
    MainConfigHandler config,
    ILogger<ControlSettingsPatchHandler> logger)
{
    public async Task<ControlCommandOutcome> ExecuteAsync(
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        // 整份 patch 先全部校验通过才动手：改了一半的设备比整体失败更难排查
        // ——老师看到的是"有些设置变了、有些没变"。
        if (!ControlSettingsWhitelist.TryPlan(payload, out var changes, out var reason))
        {
            // 拒绝时把**可写清单**一起给出去：控制台能直接列出"这台机器允许改哪些设置"，
            // 管理员不用去翻协议文档，也不用猜是自己名字写错了还是设备不支持。
            return ControlCommandOutcome.Failure(
                reason,
                new { writable_paths = ControlSettingsWhitelist.WritablePaths });
        }

        try
        {
            // 配置对象是 ObservableObject：改它必须回到 UI 线程，否则绑定到界面的属性
            // 会从后台线程收到 PropertyChanged（集控命令正是在线程池上执行的）。
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ControlSettingsWhitelist.Apply(config.Data, changes);

                // 走配置处理器保存：它自己会做原子替换，并与设置完整性指纹联动。
                config.Save();
            });

            logger.LogInformation(
                "集控已应用设置变更：{Paths}",
                string.Join(", ", changes.Select(change => change.Path)));
            return ControlCommandOutcome.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "集控设置变更执行失败。");
            return ControlCommandOutcome.Failure(ControlRejectReasons.ExecutionFailed);
        }
    }
}
