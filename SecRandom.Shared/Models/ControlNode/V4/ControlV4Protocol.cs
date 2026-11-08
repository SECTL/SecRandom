using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecRandom.Shared.Models.ControlNode.V4;

/// <summary>
///     SECTL 集控协议 v4.1 的帧与载荷常量。
/// </summary>
/// <remarks>
///     <para>
///         v4 是 <c>req</c>/<c>res</c>/<c>evt</c> 三分信封：设备发 <c>req</c>，服务端回同 <c>id</c> 的
///         <c>res</c>；服务端主动下发的帧一律是 <c>evt</c>（<c>cmd</c>、<c>desired_state</c>、<c>evicted</c>）。
///         与 v1 的"共用一个宽松 DTO"不同，这里把信封与载荷分开：信封字段固定，载荷按 <c>topic</c> 解析。
///     </para>
///     <para>
///         所有时间戳（<c>expires_at</c>、<c>server_time</c>）都是**毫秒**的整数，不是 ISO 字符串。
///         这是 v4 与 v1 最容易接错的字段之一。
///     </para>
/// </remarks>
public static class ControlV4Protocol
{
    /// <summary>本实现实现的协议版本。低于 4 的客户端会被服务端以 <c>protocol_too_old</c> 拒绝。</summary>
    public const int Version = 4;

    /// <summary>平台标识固定为 <c>secrandom</c>。</summary>
    public const string Platform = "secrandom";

    /// <summary>单帧的 UTF-8 字节上限。</summary>
    public const int MaxFrameBytes = 64 * 1024;

    /// <summary>查询载荷的预算，留出信封余量。</summary>
    public const int PayloadBudgetBytes = MaxFrameBytes - 4 * 1024;

    public const string FrameTypeRequest = "req";
    public const string FrameTypeResponse = "res";
    public const string FrameTypeEvent = "evt";

    public static class Topics
    {
        public const string Hello = "hello";
        public const string Ping = "ping";
        public const string Command = "cmd";
        public const string CommandAck = "cmd.ack";
        public const string CommandResult = "cmd.result";
        public const string ClassSelect = "class.select";
        public const string ClassRelease = "class.release";
        public const string DesiredState = "desired_state";
        public const string Evicted = "evicted";
        public const string Error = "error";
    }

    /// <summary>命令词汇。与能力名相同，控制台与设备靠这一张表对齐。</summary>
    public static class Commands
    {
        public const string Broadcast = "broadcast";
        public const string LockScreen = "lockScreen";
        public const string RestartClient = "restartClient";
        public const string SwitchPage = "switchPage";
        public const string DrawTrigger = "draw.trigger";
        public const string DrawReset = "draw.reset";
        public const string MediaPlay = "media.play";
        public const string RosterRead = "roster.read";
        public const string RosterWrite = "roster.write";
        public const string SettingsRead = "settings.read";
        public const string SettingsWrite = "settings.write";
    }

    /// <summary>握手与命令里会出现的稳定错误码。</summary>
    public static class ErrorCodes
    {
        public const string BadRequest = "bad_request";
        public const string BadSecret = "bad_secret";
        public const string DeviceRevoked = "device_revoked";
        public const string ProtocolTooOld = "protocol_too_old";
        public const string NotAuthed = "not_authed";
        public const string Expired = "expired";
        public const string Rejected = "rejected";
        public const string Busy = "busy";
        public const string PayloadTooLarge = "payload_too_large";
        public const string UnknownTopic = "unknown_topic";
        public const string CapabilityUnsupported = "capability_unsupported";
        public const string LocalRemoteDisabled = "local_remote_disabled";
        public const string RateLimited = "rate_limited";
        public const string PageUnavailable = "page_unavailable";
        public const string ExecutionFailed = "execution_failed";
        public const string InvalidCommand = "invalid_command";
    }

    /// <summary>收到就必须停止重连、等用户重新接入或升级客户端的错误码。</summary>
    public static bool IsNoRetryCode(string? code) => code is
        ErrorCodes.BadSecret or ErrorCodes.DeviceRevoked or ErrorCodes.ProtocolTooOld or ErrorCodes.NotAuthed;
}

/// <summary>v4 信封。未知字段一律忽略，缺失 <c>type</c> 的帧视为无效。</summary>
public sealed record ControlV4Frame
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("topic")]
    public string? Topic { get; init; }

    [JsonPropertyName("ok")]
    public bool? Ok { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }
}

/// <summary>v4 帧的序列化设置。</summary>
public static class ControlV4Json
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(ControlV4Frame frame) => JsonSerializer.Serialize(frame, Options);

    public static int MeasureBytes(object payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, Options).Length;

    /// <summary>解析信封；无法解析或缺 <c>type</c> 时返回 <c>null</c>。</summary>
    public static ControlV4Frame? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var frame = JsonSerializer.Deserialize<ControlV4Frame>(json, Options);
            return string.IsNullOrWhiteSpace(frame?.Type) ? null : frame;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static JsonElement ToElement(object payload) =>
        JsonSerializer.SerializeToElement(payload, Options);
}

/// <summary><c>hello</c> 的载荷。</summary>
public sealed record ControlV4Hello
{
    [JsonPropertyName("protocol")]
    public int Protocol { get; init; } = ControlV4Protocol.Version;

    [JsonPropertyName("platform")]
    public string Platform { get; init; } = ControlV4Protocol.Platform;

    [JsonPropertyName("version")]
    public string Version { get; init; } = "0.0.0";

    [JsonPropertyName("device_name")]
    public string DeviceName { get; init; } = string.Empty;

    [JsonPropertyName("device_id")]
    public string DeviceId { get; init; } = string.Empty;

    [JsonPropertyName("secret")]
    public string Secret { get; init; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    [JsonPropertyName("applied_state_revision")]
    public long AppliedStateRevision { get; init; }
}

/// <summary><c>hello</c> 的应答载荷。</summary>
public sealed record ControlV4HelloAck
{
    public int? Protocol { get; init; }

    public int HeartbeatSeconds { get; init; } = 25;

    public int OfflineAfterSeconds { get; init; } = 75;

    public long DesiredStateRevision { get; init; }

    /// <summary>服务端下发的当前抽取锁。**缺失按 <c>true</c> 处理**（fail-closed）。</summary>
    public bool DrawLocked { get; init; } = true;

    public string? DeviceId { get; init; }

    public string? DeviceName { get; init; }

    /// <summary>从 <c>res.payload</c> 解析；字段缺失/类型不对时走安全默认值。</summary>
    public static ControlV4HelloAck Parse(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } root)
            return new ControlV4HelloAck();

        var ack = new ControlV4HelloAck
        {
            Protocol = ReadInt(root, "protocol"),
            HeartbeatSeconds = PositiveOr(ReadInt(root, "heartbeat_seconds"), 25),
            OfflineAfterSeconds = PositiveOr(ReadInt(root, "offline_after_seconds"), 75),
            DesiredStateRevision = ReadLong(root, "desired_state_revision") ?? 0,
            // 缺失或类型不对一律视为锁定：无法证明服务端允许抽取，就不能放开。
            DrawLocked = ReadBool(root, "draw_locked") ?? true
        };

        if (root.TryGetProperty("device", out var device) && device.ValueKind == JsonValueKind.Object)
        {
            ack = ack with
            {
                DeviceId = ReadString(device, "id"),
                DeviceName = ReadString(device, "name")
            };
        }

        return ack;
    }

    private static int PositiveOr(int? value, int fallback) => value is > 0 ? value.Value : fallback;

    internal static int? ReadInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    internal static long? ReadLong(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var parsed)
            ? parsed
            : null;

    internal static bool? ReadBool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    internal static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary><c>evt cmd</c> 的载荷。</summary>
public sealed record ControlV4Command
{
    public string CommandId { get; init; } = string.Empty;

    public string Cmd { get; init; } = string.Empty;

    public JsonElement? Args { get; init; }

    public string? Issuer { get; init; }

    /// <summary>过期时间（毫秒）。缺失或无法解析为 <c>null</c>，调用方必须拒绝。</summary>
    public long? ExpiresAt { get; init; }

    public int? DeadlineMs { get; init; }

    public static bool TryParse(JsonElement? payload, out ControlV4Command command)
    {
        command = new ControlV4Command();
        if (payload is not { ValueKind: JsonValueKind.Object } root)
            return false;

        var commandId = ControlV4HelloAck.ReadString(root, "command_id");
        var cmd = ControlV4HelloAck.ReadString(root, "cmd");
        if (string.IsNullOrWhiteSpace(commandId) || string.IsNullOrWhiteSpace(cmd))
            return false;

        command = new ControlV4Command
        {
            CommandId = commandId,
            Cmd = cmd,
            Args = root.TryGetProperty("args", out var args) ? args.Clone() : null,
            Issuer = ControlV4HelloAck.ReadString(root, "issuer"),
            ExpiresAt = ControlV4HelloAck.ReadLong(root, "expires_at"),
            DeadlineMs = ControlV4HelloAck.ReadInt(root, "deadline_ms")
        };
        return true;
    }
}

/// <summary><c>evt desired_state</c> 的载荷；未知字段忽略，但仍推进 revision。</summary>
public sealed record ControlV4DesiredState
{
    public long Revision { get; init; }

    public string? ClassId { get; init; }

    public string? Platform { get; init; }

    /// <summary><c>null</c> 表示服务端没带这个字段——调用方必须按 <c>true</c> 处理。</summary>
    public bool? DrawLocked { get; init; }

    public static ControlV4DesiredState? TryParse(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } root)
            return null;

        // 没有 revision 的期望状态无法判新旧：直接丢弃，绝不当成 revision 0 应用。
        if (ControlV4HelloAck.ReadLong(root, "revision") is not { } revision)
            return null;

        return new ControlV4DesiredState
        {
            Revision = revision,
            ClassId = ControlV4HelloAck.ReadString(root, "class_id"),
            Platform = ControlV4HelloAck.ReadString(root, "platform"),
            DrawLocked = ControlV4HelloAck.ReadBool(root, "draw_locked")
        };
    }
}

/// <summary><c>evt evicted</c> 的载荷。</summary>
public sealed record ControlV4Evicted(string? ClassId, string? Platform, string? By, string? Reason);