using System.Text.Json;

namespace SecRandom.Shared.Models.ControlCenter;

/// <summary>
///     v4 集控控制面的只读响应模型。
/// </summary>
/// <remarks>
///     <para>
///         这一层只描述"服务端会回什么"，不含任何网络、UI 或状态判断：
///         app 层的 <c>IControlCenterClient</c> 负责取数，ViewModel 负责决定展示。
///     </para>
///     <para>
///         与已断代的 v1 <c>ControlPlaneModels</c> 的区别是**身份模型换了**：v1 是"组 × 节点"，
///         v4 是"地点 → 年级 → 班级 → 设备"，命令也按班级下发而不是按节点。
///     </para>
/// </remarks>
public sealed record ControlCenterSite(
    string Id,
    string PublicId,
    string Name,
    string Role,
    int GradeCount,
    int ClassCount,
    int DeviceCount,
    int OnlineDeviceCount);

public sealed record ControlCenterPlatform(
    string Id,
    string Name,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<ControlCenterCommand> Commands);

public sealed record ControlCenterCommand(string Id, string Label, bool NeedsConfirm);

public sealed record ControlCenterMe(
    string? UserId,
    string? UserName,
    IReadOnlyList<ControlCenterSite> Sites,
    IReadOnlyList<ControlCenterPlatform> Platforms);

public sealed record ControlCenterGrade(
    string Id,
    string Name,
    int ClassCount,
    IReadOnlyList<string> Floors);

public sealed record ControlCenterClass(
    string Id,
    string Name,
    string GradeId,
    string GradeName,
    string? Floor,
    int DeviceCount,
    IReadOnlyList<string> Projects,
    ControlCenterOccupiedBy? OccupiedBy);

public sealed record ControlCenterOccupiedBy(string DeviceId, string DeviceName);

public sealed record ControlCenterDevice(
    string Id,
    string Name,
    IReadOnlyList<string> Platforms,
    string? ClassId,
    bool Online,
    IReadOnlyList<string> Capabilities,
    DateTimeOffset? LastSeenAt);

public sealed record ControlCenterSiteRef(string PublicId, string Name);

/// <summary>班级详情：<c>GET /control/api/classes/:classId</c> 的 data。</summary>
/// <param name="Occupancy">平台 → 当前占班设备 ID；没有占用的平台不出现。</param>
public sealed record ControlCenterClassDetail(
    ControlCenterClass Class,
    ControlCenterSiteRef Site,
    IReadOnlyList<ControlCenterDevice> Devices,
    IReadOnlyDictionary<string, string> Occupancy,
    IReadOnlyList<string> OnlineDeviceIds,
    bool DrawLocked);

public sealed record ControlCenterDrawnMember(string? Id, string? Name);

/// <summary>
///     设备上可选的点名名单 / 抽奖奖池名称（<c>roster.list</c> 的返回）。
/// </summary>
/// <remarks>
///     只含名称，不含成员：成员明细需要 <c>roster.read</c>（admin）。<c>Default*</c> 是设备当前的默认名单，
///     没有默认时为空；控制台选名单时优先用它，避免"看起来选了但其实没选"。
/// </remarks>
public sealed record ControlCenterRosterList(
    IReadOnlyList<string> RollCall,
    IReadOnlyList<string> Lottery,
    string? DefaultRollCall,
    string? DefaultLottery);

/// <summary>命令回执：成功时 payload 是设备 <c>cmd.result</c> 带回的 data。</summary>
public sealed record ControlCenterCommandResult(
    bool Ok,
    JsonElement? Payload,
    double? Elapsed,
    string? Ack,
    string? CommandId);

/// <summary>账号接入（<c>POST /control/api/device/enroll</c>）返回的设备凭据。</summary>
/// <remarks><c>Secret</c> 只在这一次响应里出现，之后由接入记录加密保存。</remarks>
public sealed record ControlCenterAccountEnrollment(
    string DeviceId,
    string Secret,
    string SitePublicId,
    string SiteName,
    string? ClassId,
    string? ClassName,
    int HeartbeatSeconds,
    int OfflineAfterSeconds);

public sealed record ControlCenterDrawLock(bool Locked, string Platform, bool Pushed, long Revision);