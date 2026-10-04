using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecRandom.Shared.Models.ControlNode;

/// <summary>
///     集控节点通道的一帧。
/// </summary>
/// <remarks>
///     <para>
///         两个方向共用同一个形状：协议里所有帧都用 <c>type</c> 判别，且
///         **字段缺失与 <c>null</c> 等价、未知字段必须忽略**（前向兼容）。
///         因此这里用一个宽松的 DTO，而不是两个强类型的层次结构：
///         收到不认识的新字段/新帧类型时只会被忽略，不会反序列化失败。
///     </para>
///     <para>
///         <see cref="Type" /> 故意**不是** <c>required</c>：缺 <c>type</c> 的帧应当被
///         丢弃，而不是让整条连接因为一个反序列化异常断掉。
///     </para>
///     <para>
///         <c>desired_state_revision</c> 用 <see cref="long" /> 接收：服务端按
///         <c>max(旧值 + 1, 当前毫秒)</c> 生成，数值约 1.7×10¹²，**32 位整数会溢出**。
///     </para>
/// </remarks>
public sealed record ControlFrame
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    // ---- 节点 → 服务端：握手与心跳 ------------------------------------------

    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }

    [JsonPropertyName("group_id")]
    public string? GroupId { get; init; }

    [JsonPropertyName("platform")]
    public string? Platform { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>节点**实际支持**的能力。未知能力由服务端授权门拒绝，这里不过滤。</summary>
    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string>? Capabilities { get; init; }

    /// <summary>本机是否允许被集控。**由设备上报，服务端只读。**</summary>
    [JsonPropertyName("local_remote_allowed")]
    public bool? LocalRemoteAllowed { get; init; }

    [JsonPropertyName("current_class")]
    public string? CurrentClass { get; init; }

    /// <summary>节点已应用的期望状态 revision。</summary>
    [JsonPropertyName("desired_state_revision")]
    public long? DesiredStateRevision { get; init; }

    // ---- 节点 → 服务端：命令回执 --------------------------------------------

    [JsonPropertyName("command_id")]
    public string? CommandId { get; init; }

    /// <summary><c>command.ack</c>：我**愿意**执行吗？</summary>
    [JsonPropertyName("accepted")]
    public bool? Accepted { get; init; }

    /// <summary><c>command.result</c>：我**执行成功**了吗？</summary>
    [JsonPropertyName("ok")]
    public bool? Ok { get; init; }

    /// <summary>拒绝或失败的原因码。</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    // ---- 服务端 → 节点：握手结果与命令 --------------------------------------

    /// <summary>心跳间隔（秒）。**必须用服务端下发的值，不要写死。**</summary>
    [JsonPropertyName("heartbeat_seconds")]
    public int? HeartbeatSeconds { get; init; }

    /// <summary>服务端的离线判定阈值（秒）。</summary>
    [JsonPropertyName("offline_after_seconds")]
    public int? OfflineAfterSeconds { get; init; }

    [JsonPropertyName("capability")]
    public string? Capability { get; init; }

    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }

    /// <summary>过期时间。**必须在执行之前检查**，已过则丢弃。</summary>
    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>握手被拒时的错误码，见 <see cref="ControlErrorCodes" />。</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }
}

/// <summary>
///     帧的序列化设置。
/// </summary>
/// <remarks>
///     所有字段都带显式 <see cref="JsonPropertyNameAttribute" />，因此命名策略不参与；
///     写出的空字段一律省略（与服务端"字段缺失与 <c>null</c> 等价"的约定一致），
///     读取时大小写不敏感，单帧上限由传输层按协议设为 64 KiB。
/// </remarks>
public static class ControlProtocolJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(ControlFrame frame) => JsonSerializer.Serialize(frame, Options);

    /// <summary>
    ///     解析一帧。无法解析或缺 <c>type</c> 时返回 <c>null</c>——调用方应当忽略该帧。
    /// </summary>
    public static ControlFrame? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var frame = JsonSerializer.Deserialize<ControlFrame>(json, Options);
            return string.IsNullOrWhiteSpace(frame?.Type) ? null : frame;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>读取 <c>desired_state</c> 载荷中的 <c>draw_locked</c>；其它字段一律忽略。</summary>
    public static ControlDesiredState? ReadDesiredState(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } element)
            return null;

        if (!element.TryGetProperty("draw_locked", out var drawLocked))
            return null;

        return drawLocked.ValueKind switch
        {
            JsonValueKind.True => new ControlDesiredState(true),
            JsonValueKind.False => new ControlDesiredState(false),
            _ => null
        };
    }
}
