using System.Text.Json;
using System.Text.Json.Serialization;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     <c>draw.reset</c> 的载荷：清空哪一类、哪一份名单的"本轮临时记录"。
/// </summary>
/// <remarks>
///     <para>
///         <b>语义边界（硬约束）</b>：这里清的是**抽取进度**——某个学生/奖品这一轮是否已被抽到过
///         （<c>DrawTemporaryRecordService</c> 里的临时记录），目的是让下一轮重新开始。
///         它**绝不触碰历史记录**（<c>data/history/**</c>）：历史是名单的长期账本，
///         "重置"在控制台上看起来很像"清空记录"，一旦被误解成清历史，老师丢掉的是不可再生的数据。
///     </para>
///     <para>
///         <c>roll_call</c> 与 <c>quick</c> 清的是**同一份学生临时记录**（沿用既有约定：
///         点名与快抽共用一个池），<c>lottery</c> 清奖品临时记录。
///     </para>
/// </remarks>
/// <param name="Target">清哪一类：<see cref="TargetRollCall" />（默认）/ <see cref="TargetQuick" /> / <see cref="TargetLottery" />。</param>
/// <param name="ListName">只清这一份名单/奖池的桶；空表示清全部名单的桶。</param>
public sealed record ControlDrawResetRequest(string Target, string? ListName)
{
    public const string TargetRollCall = "roll_call";
    public const string TargetQuick = "quick";
    public const string TargetLottery = "lottery";

    /// <summary>不带载荷时的默认目标：点名（最常用的那一类）。</summary>
    public static ControlDrawResetRequest Default { get; } = new(TargetRollCall, null);

    /// <summary>这次重置清的是奖品临时记录（奖池），否则是学生临时记录。</summary>
    public bool ClearsPrizes => string.Equals(Target, TargetLottery, StringComparison.Ordinal);

    public static bool TryParse(JsonElement? payload, out ControlDrawResetRequest request, out string reason)
    {
        request = Default;
        reason = ControlRejectReasons.InvalidCommand;

        // 没有载荷＝按默认目标清（点名）。缺省值写在协议里，控制台可以只发一条空动作。
        if (payload is not { } element || element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            reason = string.Empty;
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return false;

        var target = TargetRollCall;
        if (element.TryGetProperty("target", out var targetElement))
        {
            if (targetElement.ValueKind != JsonValueKind.String)
            {
                reason = "invalid_value:target:type_mismatch";
                return false;
            }

            target = (targetElement.GetString() ?? string.Empty).Trim();
            if (target is not (TargetRollCall or TargetQuick or TargetLottery))
            {
                reason = "invalid_value:target:unsupported";
                return false;
            }
        }

        string? listName = null;
        if (element.TryGetProperty("list_name", out var nameElement))
        {
            if (nameElement.ValueKind != JsonValueKind.String)
            {
                reason = "invalid_value:list_name:type_mismatch";
                return false;
            }

            var candidate = (nameElement.GetString() ?? string.Empty).Trim();
            listName = candidate.Length == 0 ? null : candidate;
        }

        reason = string.Empty;
        request = new ControlDrawResetRequest(target, listName);
        return true;
    }
}

/// <summary>
///     抽取进行中一律拒绝的能力。
/// </summary>
/// <remarks>
///     <para>
///         这三件事都会动到"正在抽的那一轮"赖以成立的东西：改设置改的是规则，换名单改的是候选人，
///      <b>重置直接抹掉那一轮的进度</b>——边抽边清会让结果与临时记录对不上，历史里出现一个
///     "抽到了但进度里没有"的矛盾状态。因此它们统一在分发层被 <c>busy</c> 挡回，
///     而不是各自去判断"现在能不能改"。
///     </para>
///     <para>
///         抽成纯函数是为了能单测这条策略：它是协议行为（控制台据此显示"设备正在抽取"），
///         埋在 <c>ControlCommandDispatcher</c> 里的 if 条件没法单独钉住。
///     </para>
/// </remarks>
public static class ControlDrawBusyGuard
{
    public static bool IsRefusedWhileDrawing(string capability) =>
        capability is ControlCapabilities.SettingsWrite
            or ControlCapabilities.RosterWrite
            or ControlCapabilities.DrawReset;
}

/// <summary><c>draw.reset</c> 成功回执的 detail。</summary>
/// <param name="Target">实际清的目标。</param>
/// <param name="ListName">实际清的名单；清全部时为 <c>null</c>。</param>
/// <param name="Cleared">清掉的临时记录条数（抽取进度，不是历史记录）。</param>
public sealed record ControlDrawResetDetail(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("list_name")] string? ListName,
    [property: JsonPropertyName("cleared")] int Cleared);
