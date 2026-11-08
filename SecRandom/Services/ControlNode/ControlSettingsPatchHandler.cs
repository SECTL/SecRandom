using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     <c>settings.write</c>：按设备在 <c>hello</c> 里上报的 schema 改写设置。
/// </summary>
/// <remarks>
///     <para>
///         允许哪些键、值怎么校验，全部在 <see cref="ControlSettingsSchemaBuilder" />（Core，可单测）里；
///         这里只负责三件有副作用的事：<b>先整体校验、回到 UI 线程应用、走配置处理器保存</b>。
///     </para>
///     <para>
///         未知键与非法值直接以 <c>unknown_setting</c>/<c>bad_setting_value</c> 拒绝，并带
///         <c>field</c>：控制台要能把错误标在具体那一行，而不是只显示"写失败了"。
///         回执里的 <c>values</c> 是应用后的完整快照，控制台据此刷新所有控件（含未提交的项）。
///     </para>
///     <para>
///         安全设置、集控自身设置、桌面集成与更新设置不在 schema 里，且**永远不进**：
///         前两者是设备所有权（能关掉密码／能把自己接到别的组），后两者是持久化与运行面入口。
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
        if (!ControlSettingsSchemaBuilder.TryPlanPatch(
                config.Data, payload, out var changes, out var reason, out var field))
        {
            return ControlCommandOutcome.Failure(reason, new { field, reason });
        }

        try
        {
            // 配置对象是 ObservableObject：改它必须回到 UI 线程，否则绑定到界面的属性
            // 会从后台线程收到 PropertyChanged（集控命令正是在线程池上执行的）。
            var values = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ControlSettingsCatalog.Apply(config.Data, changes);

                // 走配置处理器保存：它自己会做原子替换，并与设置完整性指纹联动。
                config.Save();

                return ControlSettingsSchemaBuilder.ReadValues(config.Data);
            });

            logger.LogInformation(
                "集控已应用设置变更：{Paths}",
                string.Join(", ", changes.Select(change => change.Path)));

            return ControlCommandOutcome.SuccessWith(new { values });
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