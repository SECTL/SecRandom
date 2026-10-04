using System.Globalization;
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

    /// <summary>
    ///     设备显示名。**由用户在这台机器上填写**；留空时上报主机名。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         服务端取值三态：字段缺失 / <c>null</c> ⇒ **保持原值**，空串 ⇒ **清除**，
    ///         非空 ⇒ 更新。因此"没有名字可报"时应当**整帧里不出现这个字段**，
    ///         而不是发空串——空串会把管理端预置的名字抹掉。
    ///         回落主机名由 <c>ControlNodeDisplayName.Resolve</c> 负责，发送侧不要各写一份。
    ///     </para>
    ///     <para>
    ///         规范化（零宽字符、内部空白折叠、长度上限）由服务端在入口完成；
    ///         客户端只提供用户真正想显示的名字。
    ///     </para>
    /// </remarks>
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    /// <summary>
    ///     当前班级。**客户端不再上报这个字段**，仅为容忍旧实现/其它客户端保留解析。
    /// </summary>
    /// <remarks>
    ///     班级名（点名单名称）属于教学场景里的隐私内容：控制平面只需要知道"哪台机器、
     ///     支持什么能力、开没开本机开关"，不需要知道这间教室在上哪个班的课。
    ///     因此发送侧（<c>hello</c> / <c>heartbeat</c>）不再填充它，也不要再填。
    /// </remarks>
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

    /// <summary>
    ///     <c>command.ack</c> / <c>command.result</c> 的结构化上下文（可选）。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         原因码只回答"哪一类失败"，这里回答"具体什么情况"：语音关着、文本 212 字超过上限、
    ///         哪一条设置路径不可写……没有它，控制台只能显示一个单词，管理员无从判断该怎么办。
    ///     </para>
    ///     <para>
    ///         刻意**不带人话文案**：文案要按看控制台的人的语言渲染，而不是按设备本机的语言。
    ///     </para>
    /// </remarks>
    [JsonPropertyName("detail")]
    public JsonElement? Detail { get; init; }

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

    /// <summary>
    ///     过期时间。**必须在执行之前检查**；已过则丢弃。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         反序列化是**宽容**的：无法解析的值会变成 <c>null</c> 而不是抛异常。
    ///         否则一个格式怪异的 <c>expires_at</c> 会让整帧解析失败，节点只能静默断开连接——
    ///         而协议要求的是"解析失败也拒绝这条命令"（回 <c>accepted: false</c>），
    ///         让控制台能看见设备到底拒绝还是掉线了。
    ///     </para>
    ///     <para>
    ///         因此**缺失与无法解析对节点是同一件事**：无法证明"还没过期"，就不能执行。
    ///     </para>
    /// </remarks>
    [JsonPropertyName("expires_at")]
    [JsonConverter(typeof(LenientDateTimeOffsetJsonConverter))]
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>握手被拒时的错误码，见 <see cref="ControlErrorCodes" />。</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }
}

/// <summary>
///     帧的序列化设置。
/// </summary><remarks>
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
    public static ControlDesiredState? ReadDesiredState(JsonElement? payload)    {
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

/// <summary>
///     <c>expires_at</c> 的宽容读取：解析不出来就是 <c>null</c>，绝不抛异常。
/// </summary>
/// <remarks>
///     若这里抛 <c>JsonException</c>，整帧反序列化会失败，节点只能静默断开 ——
///     而协议要求"解析失败也拒绝这条命令"，让控制台看到一次明确的 <c>accepted: false</c>。
/// </remarks>
internal sealed class LenientDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.String)
        {
            // 数字时间戳等其它形状：跳过而不报错，交给调用方按"无法确认未过期"拒绝。
            reader.Skip();
            return null;
        }

        var raw = reader.GetString();
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is { } timestamp)
            writer.WriteStringValue(timestamp);
        else
            writer.WriteNullValue();
    }
}
