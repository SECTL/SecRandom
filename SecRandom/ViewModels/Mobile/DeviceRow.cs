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
public sealed record DeviceRow(GroupDto Group, NodeDto Node)
{
    public string GroupId => Group.GroupId ?? string.Empty;

    public string NodeId => Node.NodeId ?? string.Empty;

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
