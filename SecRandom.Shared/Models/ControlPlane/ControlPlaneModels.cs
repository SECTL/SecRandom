using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecRandom.Shared.Models.ControlPlane;

/// <summary>
///     控制面（集控 REST API）的只读契约。
/// </summary>
/// <remarks>
///     <para>
///         这一层只描述"服务端会回什么"，不做任何网络、UI 或状态判断：手机端的 <c>IControlPlaneClient</c>
///         是唯一发请求的地方，而这些记录是它的返回类型，也是单测的断言对象。
///     </para>
///     <para>
///         <b>字段全部可空、未知字段忽略</b>：控制面还在演进，手机端比服务端更新得更快是常态，
///         旧客户端读到新字段必须当没看见，新客户端读到旧服务端缺字段必须自己给默认值——
///         否则一次服务端发版就会让所有教室的手机打不开抽取页。
///     </para>
/// </remarks>
public sealed record GroupDto
{
    [JsonPropertyName("group_id")] public string? GroupId { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>当前账号在这个组里的角色：<c>admin</c> / <c>operator</c> / 其它。</summary>
    [JsonPropertyName("role")] public string? Role { get; init; }

    [JsonPropertyName("owner_user_id")] public string? OwnerUserId { get; init; }

    [JsonPropertyName("owner_display_name")] public string? OwnerDisplayName { get; init; }

    [JsonPropertyName("member_count")] public int? MemberCount { get; init; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(Name) ? GroupId ?? string.Empty : Name!;

    public bool IsAdmin => IsRole("admin");

    public bool IsOperator => IsRole("operator");

    /// <summary>能否对组内节点下发命令（管理员天然可以）。</summary>
    public bool CanOperateNodes => IsAdmin || IsOperator;

    /// <summary>能否读名单（<c>roster.read</c> 需要 Admin）。</summary>
    public bool CanReadRoster => IsAdmin;

    private bool IsRole(string role) => string.Equals(Role, role, StringComparison.OrdinalIgnoreCase);
}

/// <summary>组内的一台设备（节点）。</summary>
public sealed record NodeDto
{
    [JsonPropertyName("node_id")] public string? NodeId { get; init; }

    [JsonPropertyName("group_id")] public string? GroupId { get; init; }

    [JsonPropertyName("platform")] public string? Platform { get; init; }

    [JsonPropertyName("version")] public string? Version { get; init; }

    [JsonPropertyName("capabilities")] public IReadOnlyList<string>? Capabilities { get; init; }

    /// <summary>这台机器自己的"允许远程控制"开关。</summary>
    [JsonPropertyName("local_remote_allowed")] public bool? LocalRemoteAllowed { get; init; }

    [JsonPropertyName("display_name")] public string? DisplayName { get; init; }

    [JsonPropertyName("online")] public bool? Online { get; init; }

    [JsonPropertyName("draw_locked")] public bool? DrawLocked { get; init; }

    /// <summary>设备名（用户在本机填的名字），没填就退回节点 id——控制台里不该只显示一串随机 id。</summary>
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(DisplayName) ? NodeId ?? string.Empty : DisplayName!;

    public bool IsOnline => Online is true;

    public bool IsDrawLocked => DrawLocked is true;

    public bool IsLocalRemoteAllowed => LocalRemoteAllowed is not false;

    /// <summary>这台机器声明的能力清单里有没有某一项。</summary>
    /// <remarks>
    ///     大小写敏感：能力名是协议常量（<c>draw.trigger</c>），不是用户输入。
    /// </remarks>
    public bool Supports(string capability) =>
        !string.IsNullOrEmpty(capability)
        && Capabilities is { Count: > 0 }
        && Capabilities.Contains(capability, StringComparer.Ordinal);
}

/// <summary>一条命令的当前状态。</summary>
/// <remarks>
///     <para>
///         服务端的取值是开放的，因此这里只把**明确表示"结束了"**的那些当作终态：
///         控制台轮询到终态就停，把没见过的状态当成"还要等"，只会让手机多转一会儿；
///         反过来把等待当成终态，用户会拿到一个"没有结果的结果"。
///     </para>
/// </remarks>
public sealed record NodeCommandDto
{
    [JsonPropertyName("command_id")] public string? CommandId { get; init; }

    [JsonPropertyName("capability")] public string? Capability { get; init; }

    [JsonPropertyName("node_id")] public string? NodeId { get; init; }

    [JsonPropertyName("status")] public string? Status { get; init; }

    /// <summary>执行详情。抽取成功时就是 <c>{ target, list_name, count, drawn[] }</c>。</summary>
    [JsonPropertyName("result_detail")] public JsonElement? ResultDetail { get; init; }

    [JsonPropertyName("result_context")] public JsonElement? ResultContext { get; init; }

    /// <summary>查询类命令（<c>roster.read</c> 等）的返回本体。</summary>
    [JsonPropertyName("result_payload")] public JsonElement? ResultPayload { get; init; }

    [JsonPropertyName("error")] public string? Error { get; init; }

    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; init; }

    public bool IsSucceeded => Matches(NodeCommandStatuses.Succeeded);

    public bool IsTerminal => IsSucceeded || Matches(NodeCommandStatuses.Terminal);

    /// <summary>失败原因码：优先取服务端给的 <c>error</c>，再退回详情里的 <c>reason</c>。</summary>
    public string? FailureCode =>
        !string.IsNullOrWhiteSpace(Error)
            ? Error
            : ReadDetailString("reason") ?? ReadDetailString("code");

    /// <summary>拒绝里出问题的字段名（设备侧把 <c>invalid_value:&lt;字段&gt;:&lt;为什么&gt;</c> 拆出来）。</summary>
    public string? FailureField => ReadDetailString("field");

    /// <summary>拒绝的细因（<c>not_found</c> / <c>not_in_list</c> / <c>out_of_range</c> …）。</summary>
    public string? FailureWhy => ReadDetailString("why");

    /// <summary>
    ///     详情里的 <c>reason</c>。
    /// </summary>
    /// <remarks>
    ///     与 <see cref="FailureCode" /> 分开：设备侧会把 <c>draw_denied</c> 这种笼统的原因码放在
    ///     <c>error</c> 里，把"到底是需要本机验证还是上课时间"放在详情的 <c>reason</c> 里。
    ///     两者都读，界面才能说人话。
    /// </remarks>
    public string? DetailReason => ReadDetailString("reason");

    /// <summary>抽到了谁；点名回执的 <c>drawn[]</c>。</summary>
    public IReadOnlyList<NodeCommandDrawnMember> DrawnMembers()
    {
        if (ResultDetail is not { ValueKind: JsonValueKind.Object } detail
            || !detail.TryGetProperty("drawn", out var drawn)
            || drawn.ValueKind != JsonValueKind.Array)
            return [];

        var members = new List<NodeCommandDrawnMember>();
        foreach (var element in drawn.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                continue;

            members.Add(new NodeCommandDrawnMember(
                ReadString(element, "id"),
                ReadString(element, "name")));
        }

        return members;
    }

    public string? ResultListName => ReadDetailString("list_name");

    public int? ResultCount =>
        ResultDetail is { ValueKind: JsonValueKind.Object } detail
        && detail.TryGetProperty("count", out var count)
        && count.ValueKind == JsonValueKind.Number
        && count.TryGetInt32(out var value)
            ? value
            : null;

    private bool Matches(IReadOnlySet<string> statuses) =>
        !string.IsNullOrWhiteSpace(Status) && statuses.Contains(Status!.Trim());

    private string? ReadDetailString(string name)
    {
        if (ResultDetail is not { ValueKind: JsonValueKind.Object } detail)
            return null;

        return ReadString(detail, name);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>回执里"抽到了谁"的一条记录（设备侧可能只给学号或只给姓名）。</summary>
public sealed record NodeCommandDrawnMember(string? Id, string? Name)
{
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Name) ? Id ?? string.Empty : Name!;

    public string? SecondaryLabel => string.IsNullOrWhiteSpace(Name) ? null : Id;
}

/// <summary>命令状态里"结束"与"成功"的取值集合。</summary>
/// <remarks>
///     两套集合分开：<c>expired</c>/<c>rejected</c> 是结束但不是成功。
///     比较忽略大小写并去除首尾空白，服务端的取值风格（<c>succeeded</c> 还是 <c>Success</c>）不影响判定。
/// </remarks>
public static class NodeCommandStatuses
{
    public static readonly IReadOnlySet<string> Succeeded = new HashSet<string>(
        ["succeeded", "success", "ok", "completed", "complete", "done"],
        StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlySet<string> Terminal = new HashSet<string>(
        [
            "succeeded", "success", "ok", "completed", "complete", "done",
            "failed", "failure", "error", "rejected", "denied", "expired", "cancelled", "canceled", "timeout"
        ],
        StringComparer.OrdinalIgnoreCase);
}

/// <summary>对某个节点下发一条命令的请求体。</summary>
/// <remarks>
///     <c>kind</c> 默认 <c>action</c>：查询类命令（<c>roster.read</c>）也走同一个端点，
///     但语义是"问一次"而不是"改一次"，服务端按 <c>capability</c> 判权限与通道。
/// </remarks>
public sealed record ControlPlaneCommandRequest
{
    [JsonPropertyName("capability")] public required string Capability { get; init; }

    [JsonPropertyName("kind")] public string Kind { get; init; } = "action";

    [JsonPropertyName("payload")] public JsonElement? Payload { get; init; }
}
