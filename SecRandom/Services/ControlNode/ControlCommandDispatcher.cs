using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.ViewModels.MainPages;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     集控节点能执行的 11 条 v4.1 命令。
/// </summary>
/// <remarks>
///     <para>
///         <b>只声明真的实现了的命令。</b>能力清单是节点对服务端的承诺：声明了却执行不了，
///         服务端就会下发注定失败的命令，控制台看到的是一条"设备已接受但失败"的运行故障。
///     </para>
///     <para>
///         命令词汇与协议 §2.5 一一对应；未知命令在这里返回 <c>capability_unsupported</c>，
///         不会落到"静默成功"。
///     </para>
/// </remarks>
public sealed class ControlCommandDispatcher(
    ControlBroadcastHandler broadcast,
    ControlLockScreenHandler lockScreen,
    ControlSwitchPageHandler switchPage,
    ControlRestartHandler restart,
    ControlDrawTriggerHandler drawTrigger,
    ControlDrawResetHandler drawReset,
    ControlMediaPlayHandler mediaPlay,
    ControlSettingsPatchHandler settingsPatch,
    ControlRosterPushHandler rosterPush,
    ControlRosterReadHandler rosterRead,
    ControlSettingsReadHandler settingsRead) : IControlCommandDispatcher
{
    public IReadOnlyList<string> DeclaredCapabilities { get; } =
    [
        ControlCapabilities.Broadcast,
        ControlCapabilities.LockScreen,
        ControlCapabilities.SwitchPage,
        ControlCapabilities.RestartClient,
        ControlCapabilities.DrawTrigger,
        ControlCapabilities.DrawReset,
        ControlCapabilities.MediaPlay,
        ControlCapabilities.RosterRead,
        ControlCapabilities.RosterWrite,
        ControlCapabilities.SettingsRead,
        ControlCapabilities.SettingsWrite
    ];

    public bool CanExecute(string capability) => capability switch
    {
        ControlCapabilities.Broadcast => true,
        ControlCapabilities.LockScreen => true,
        ControlCapabilities.SwitchPage => true,
        ControlCapabilities.RestartClient => true,
        ControlCapabilities.DrawTrigger => true,
        ControlCapabilities.DrawReset => true,
        ControlCapabilities.MediaPlay => true,
        ControlCapabilities.RosterRead => true,
        ControlCapabilities.RosterWrite => true,
        ControlCapabilities.SettingsRead => true,
        ControlCapabilities.SettingsWrite => true,
        _ => false
    };

    public async Task<ControlCommandOutcome> ExecuteAsync(
        ControlCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        // 改设置、换名单、重置与重启在抽取进行中一律拒绝（策略见 ControlDrawBusyGuard）：
        // 重置尤其不能边抽边清——那一轮的进度会被抹掉，结果与临时记录就对不上了。
        // 抽取本身（draw.trigger）自己会拒绝重入。
        if (ControlDrawBusyGuard.IsRefusedWhileDrawing(invocation.Capability)
            && await IsDrawInProgressAsync().ConfigureAwait(false))
        {
            return ControlCommandOutcome.Failure("busy", new { drawing = true });
        }

        return invocation.Capability switch
        {
            ControlCapabilities.Broadcast =>
                await broadcast.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.LockScreen =>
                await lockScreen.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.SwitchPage =>
                await switchPage.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.RestartClient =>
                await restart.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.DrawTrigger =>
                await drawTrigger.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.DrawReset =>
                await drawReset.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.MediaPlay =>
                await mediaPlay.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.SettingsWrite =>
                await settingsPatch.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.RosterWrite =>
                await rosterPush.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.RosterRead =>
                await rosterRead.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.SettingsRead =>
                await settingsRead.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            _ => ControlCommandOutcome.Failure(
                ControlRejectReasons.CapabilityUnsupported,
                new { capability = invocation.Capability, supported = DeclaredCapabilities })
        };
    }

    /// <summary>本机是否正在抽取。读的是页面 ViewModel 的真实状态，而不是猜测。</summary>
    /// <remarks>
    ///     三个抽取页都要问：点名声明的忙碌状态只覆盖点名页，抽奖页抽到一半时同样不能被改设置或清进度——
    ///     <c>draw.reset</c> 尤其不能边抽边清，那一轮的进度会被抹掉，结果与临时记录就对不上了。
    /// </remarks>
    private static async Task<bool> IsDrawInProgressAsync()
    {
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(
                () => IAppHost.GetService<QuickDrawPageViewModel>().IsDrawing
                      || IAppHost.GetService<RollCallPageViewModel>().IsDrawing
                      || IAppHost.GetService<LotteryPageViewModel>().IsDrawing);
        }
        catch (Exception)
        {
            // 取不到状态时不阻断命令：宁可让命令继续，也不要因为读不到界面状态就永久拒绝远程操作。
            return false;
        }
    }
}