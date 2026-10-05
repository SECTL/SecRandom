using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.ControlPlane;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.ViewModels.Mobile;

/// <summary>
///     远程抽取页上的一台可选设备：一个"组 × 节点"的组合。
/// </summary>
/// <remarks>
///     <para>
///         为什么按组展开而不是先选组再选设备：同一台教室机可能同时属于多个组，
///         而老师要回答的问题是"我要控制哪台机器"，不是"它挂在哪个组下面"。
///         扁平列表让在线/离线、本机是否允许远控、能不能抽一眼看全，少一次来回。
///     </para>
///     <para>
///         <b>不能抽的设备照样列出来、但置灰并写出原因。</b>把不可用的机器直接藏掉，
///         老师只会以为"我的设备没连上"，然后反复刷新；写清楚"未声明名单读取，无法选名单"
///         才能让人知道下一步该做什么。
///     </para>
/// </remarks>
/// <param name="Group">这一行挂在哪个组下面。</param>
/// <param name="Node">节点本身。</param>
/// <param name="IsSelf">
///     这台设备就是本机（按 <c>node_id</c> 比对）。**只做标识，不做过滤**：本机照样留在列表里，
///     因为"我要给这台机器本身下发命令"是合法需求，藏起来只会让人以为设备没连上。
/// </param>
public sealed record DeviceRow(GroupDto Group, NodeDto Node, bool IsSelf = false)
{
    public string GroupId => Group.GroupId ?? string.Empty;

    public string NodeId => Node.NodeId ?? string.Empty;

    /// <summary>本机徽章文案（不是本机时为空串；显隐由 <see cref="IsSelf" /> 控制）。</summary>
    public string SelfBadgeText => IsSelf ? LR.RD_SelfBadge : string.Empty;

    /// <summary>
    ///     两个 <c>node_id</c> 是不是同一台设备。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         大小写与首尾空白都按"同一个"处理：node_id 是服务端与本机各自持有的稳定标识，
    ///         两边格式化方式不同（例如一方补了空白、一方改过大小写）不该让"本机"这个标识漏掉——
    ///         而它正是用来防止"手滑把命令发给自己"的，漏标就等于没有。
    ///     </para>
    ///     <para>
    ///         任一侧为空即"不是本机"：本机没配置节点身份（手机根本没有节点）时，一台都不标，
    ///         而不是把所有空 id 的行都标成"本机"。
    ///     </para>
    /// </remarks>
    public static bool IsSameNode(string? ownNodeId, string? candidateNodeId) =>
        !string.IsNullOrWhiteSpace(ownNodeId)
        && !string.IsNullOrWhiteSpace(candidateNodeId)
        && string.Equals(ownNodeId.Trim(), candidateNodeId.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>设备名（没填名字时回落节点 id，绝不留空）。</summary>
    public string DeviceLabel => Node.DisplayLabel;

    public string GroupLabel => Group.DisplayLabel;

    public bool IsOnline => Node.IsOnline;

    /// <summary>这台机器自己的远控开关被关掉了：命令一定被拒，因此不可用。</summary>
    public bool IsRemoteDisabled => !Node.IsLocalRemoteAllowed;

    public bool SupportsRosterRead => Node.Supports(ControlCapabilities.RosterRead);

    public bool SupportsDraw => Node.Supports(ControlCapabilities.DrawTrigger);

    /// <summary>现在能不能用它完成"读名单 + 抽取"整条流程。</summary>
    public bool IsUsable => !IsRemoteDisabled && SupportsRosterRead && SupportsDraw;

    /// <summary>在线状态：离线仍然可以选——命令会在它上线后补投。</summary>
    public string StatusText => IsRemoteDisabled
        ? LR.RD_RemoteDisabled
        : IsOnline
            ? LR.RD_Online
            : LR.RD_OfflineQueued;

    /// <summary>置灰的原因；可用时为 <c>null</c>。</summary>
    public string? UnavailableReason
    {
        get
        {
            if (IsRemoteDisabled)
                return LR.RD_RemoteDisabled;

            if (!SupportsRosterRead)
                return LR.RD_MissingRosterCapability;

            return SupportsDraw ? null : LR.RD_MissingDrawCapability;
        }
    }

    public bool HasUnavailableReason => UnavailableReason is not null;

    /// <summary>顶部常驻的那一行：设备名 · 状态 · 组名。</summary>
    public string Summary => $"{DeviceLabel} · {StatusText} · {GroupLabel}";

    public bool Matches(string? groupId, string? nodeId) =>
        string.Equals(GroupId, groupId, StringComparison.Ordinal)
        && string.Equals(NodeId, nodeId, StringComparison.Ordinal);
}
