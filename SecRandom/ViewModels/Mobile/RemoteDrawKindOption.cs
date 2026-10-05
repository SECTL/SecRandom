using SecRandom.Core.Services.ControlNode;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.ViewModels.Mobile;

/// <summary>
///     手机端远程抽取页上的"抽取类型"选项：点名或抽奖。
/// </summary>
/// <remarks>
///     <para>
///         用 <see cref="Target" /> 而不是显示文本做判定：显示文本会随界面语言变化，
///         拿它去比较等于把"选中了哪一项"绑在某一国语言上，换个语言就选不中。
///         <see cref="Target" /> 与协议里的 <c>draw.trigger.target</c> 是同一组常量，
///         选中的那一项可以直接作为命令参数下发。
///     </para>
///     <para>
///         标签每次现取（<see cref="CreateRollCall" /> 而不是静态字段）：界面语言可以在运行中切换，
///         把文案缓存在静态字段里就等于把它冻结在首次访问时的那一国语言上。
///     </para>
/// </remarks>
/// <param name="Target">协议目标：<see cref="ControlDrawTriggerRequest.TargetRollCall" /> 或 <see cref="ControlDrawTriggerRequest.TargetLottery" />。</param>
/// <param name="Label">下拉框里显示的短标签。</param>
public sealed record RemoteDrawKindOption(string Target, string Label)
{
    public static RemoteDrawKindOption CreateRollCall() =>
        new(ControlDrawTriggerRequest.TargetRollCall, LR.RD_KindRollCall);

    public static RemoteDrawKindOption CreateLottery() =>
        new(ControlDrawTriggerRequest.TargetLottery, LR.RD_KindLottery);
}
