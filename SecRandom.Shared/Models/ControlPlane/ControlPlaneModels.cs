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

    /// <summary>当前账号在这个组里的角色：<c>owner</c> / <c>admin</c> / <c>operator</c> / 其它。</summary>
    [JsonPropertyName("role")] public string? Role { get; init; }

    [JsonPropertyName("owner_user_id")] public string? OwnerUserId { get; init; }

    [JsonPropertyName("owner_display_name")] public string? OwnerDisplayName { get; init; }

    [JsonPropertyName("member_count")] public int? MemberCount { get; init; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(Name) ? GroupId ?? string.Empty : Name!;

    /// <summary>
    ///     当前账号在这个组里的权限等级：<c>owner</c>/<c>admin</c> = 2，<c>operator</c> = 1，
    ///     已知但只读的角色 = 0；**不认识的取值是 <c>null</c>（不作判断）**。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         线上返回的角色里有 <c>owner</c>（组所有者）。早先这里只认 <c>admin</c>/<c>operator</c>，
    ///         于是组所有者的手机看到的是一台台"权限不足"的机器——账号明明是对的。
    ///         角色取值域归服务端所有，客户端只能按已知取值判断，"不认识"不等于"没权限"。
    ///     </para>
    ///     <para>
    ///         不认识的取值按"不拦"处理：拦错了合法账号就用不了，而放行了服务端照样会回 403，
    ///         界面再如实显示"当前账号没有权限"。权威判定留给服务端。
    ///     </para>
    /// </remarks>
    public int? RoleRank => Role?.Trim().ToLowerInvariant() switch
    {
        "owner" or "admin" => 2,
        "operator" => 1,
        "viewer" or "readonly" or "read-only" or "member" or "guest" => 0,
        _ => null
    };

    /// <summary>能否读名单（<c>roster.read</c> 需要管理员）。未知角色不在这里拦。</summary>
    public bool CanReadRoster => RoleRank is null or >= 2;

    /// <summary>能否对组内节点下发命令（<c>operator</c> 及以上）。未知角色不在这里拦。</summary>
    public bool CanOperateNodes => RoleRank is null or >= 1;

    /// <summary>角色取值已知且确实不够：界面据此给出"权限不足"的解释。</summary>
    public bool IsKnownInsufficientRole => RoleRank == 0;

    public bool IsAdmin => RoleRank >= 2;

    /// <summary>至少是 <c>operator</c>（含管理员与所有者）。</summary>
    public bool IsOperator => RoleRank >= 1;
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

    /// <summary>
    ///     执行结果对象：设备侧的成功回执本体（<c>{ target, list_name, count, drawn[] }</c>）。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         为什么要在三个字段里找、还要认"JSON 装在字符串里"：设备回的是
    ///         <c>command.result</c> 的 <c>detail</c>，服务端把它落到 REST 的哪个字段、
    ///         是当对象存还是当字符串存，客户端无从约定。线上出现过"点名其实抽成功了，
    ///         手机却显示没有符合条件的人"——回执取不到就是那条假象的来源，
    ///         因此这里对**所有合理形状**都取一次：<c>result_detail</c> → <c>result_payload</c> → <c>result_context</c>，
    ///         对象或字符串（字符串里再解一层 JSON）都认。
    ///     </para>
    ///     <para>
    ///         只读不猜：找不到就返回 <c>false</c>，由调用方决定说什么，绝不编一个空结果。
    ///     </para>
    /// </remarks>
    public bool TryGetResultObject(out JsonElement result)
    {
        foreach (var candidate in new[] { ResultDetail, ResultPayload, ResultContext })
        {
            if (TryResolveObject(candidate, out result))
                return true;
        }

        result = default;
        return false;
    }

    /// <summary>回执原样 JSON，仅在日志/排查时使用（可能包含名单名，不进遥测）。</summary>
    public string? ResultDiagnostics
    {
        get
        {
            var candidate = ResultDetail ?? ResultPayload ?? ResultContext;
            return candidate is { } element ? element.GetRawText() : null;
        }
    }

    /// <summary>抽到了谁；点名回执的 <c>drawn[]</c>。</summary>
    public IReadOnlyList<NodeCommandDrawnMember> DrawnMembers()
    {
        if (!TryGetResultObject(out var result)
            || !result.TryGetProperty("drawn", out var drawn)
            || drawn.ValueKind != JsonValueKind.Array)
            return [];

        var members = new List<NodeCommandDrawnMember>();
        foreach (var element in drawn.EnumerateArray())
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    members.Add(new NodeCommandDrawnMember(
                        ReadString(element, "id"),
                        ReadString(element, "name")));
                    break;

                // 极端情况下服务端可能只留下一个标识（学号或姓名）：当成"只有主标签"的一条，
                // 总比把整次成功的抽取显示成"没有符合条件的人"强。
                case JsonValueKind.String:
                    members.Add(new NodeCommandDrawnMember(element.GetString(), null));
                    break;
            }
        }

        return members;
    }

    /// <summary>把候选字段解析成对象；字符串形式（JSON 文本）也会再解一层。</summary>
    private static bool TryResolveObject(JsonElement? candidate, out JsonElement result)
    {
        result = default;
        if (candidate is not { } element)
            return false;

        if (element.ValueKind == JsonValueKind.Object)
        {
            result = element;
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
            return false;

        var text = element.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            // 文档随 using 释放，因此把内容克隆出来再返回。
            result = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public string? ResultListName => ResultString("list_name");

    public int? ResultCount =>
        ResultDetail is { ValueKind: JsonValueKind.Object } detail
        && detail.TryGetProperty("count", out var count)
        && count.ValueKind == JsonValueKind.Number
        && count.TryGetInt32(out var value)
            ? value
            : null;

    private bool Matches(IReadOnlySet<string> statuses) =>
        !string.IsNullOrWhiteSpace(Status) && statuses.Contains(Status!.Trim());

    private string? ReadDetailString(string name) =>
        TryGetResultObject(out var result) ? ReadString(result, name) : null;

    private string? ResultString(string name) => ReadDetailString(name);

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
