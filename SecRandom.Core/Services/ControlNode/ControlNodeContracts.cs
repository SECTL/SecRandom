using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>一个载荷超过协议上限（64 KiB）——它根本发不出去。</summary>
public sealed class ControlFrameTooLargeException(string frameType, int bytes, int limit)
    : InvalidOperationException($"集控帧超过 {limit} 字节上限：{frameType}（实测 {bytes} 字节）")
{
    public string FrameType { get; } = frameType;

    public int Bytes { get; } = bytes;

    public int Limit { get; } = limit;
}

/// <summary>
///     集控节点通道的传输边界：交换的是**已经序列化好的 v4 信封文本**。
/// </summary>
/// <remarks>
///     协议引擎只依赖这个接口，不依赖 <c>ClientWebSocket</c>：会话状态机因此可以在测试里
///     用一个内存传输驱动，而不需要真的起一个 WebSocket 服务端。
/// </remarks>
public interface IControlNodeTransport : IAsyncDisposable
{
    Task SendAsync(string frameJson, CancellationToken cancellationToken);

    /// <summary>接收一帧原文。返回 <c>null</c> 表示连接已经结束。</summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>对端关闭连接时给出的原因（例如 <c>replaced</c>）；没有时为 <c>null</c>。</summary>
    string? CloseReason { get; }

    /// <summary>WebSocket 关闭码（4003/4006 表示不要重连）；没有时为 <c>null</c>。</summary>
    int? CloseCode { get; }
}

/// <summary>建立一条节点连接。地址里**不得携带凭据**：v4 的凭据在第一帧 <c>hello</c> 的载荷里。</summary>
public interface IControlNodeTransportFactory
{
    Task<IControlNodeTransport> ConnectAsync(ControlNodeConnectRequest request, CancellationToken cancellationToken);
}

/// <param name="Endpoint">服务端地址，例如 <c>wss://sectl.cn/control/ws</c>。</param>
public sealed record ControlNodeConnectRequest(string Endpoint)
{
    /// <summary>v1 兼容槽位，v4 永远为 <c>null</c>。</summary>
    public string? BearerToken { get; init; }
}

/// <summary>连接**在建立阶段**就被拒绝（升级失败/服务端关闭）：凭据或协议问题，不是网络问题。</summary>
public sealed class ControlNodeConnectRejectedException : Exception
{
    public ControlNodeConnectRejectedException(HttpStatusCode statusCode, Exception? innerException = null)
        : base($"集控节点连接被服务端拒绝（HTTP {(int)statusCode}）。", innerException)
    {
        StatusCode = statusCode;
        Code = "http_" + (int)statusCode;
        IsTerminal = statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    }

    /// <summary>v4 里拒绝发生在 <c>hello</c> 的 <c>res.ok:false</c>，<paramref name="code" /> 是稳定错误码。</summary>
    public ControlNodeConnectRejectedException(string code, bool terminal = true, Exception? innerException = null)
        : base($"集控节点握手被拒绝：{code}。", innerException)
    {
        Code = code;
        IsTerminal = terminal;
    }

    public HttpStatusCode? StatusCode { get; }

    public string Code { get; }

    /// <summary>为 <c>true</c> 时重试没有意义（<c>bad_secret</c>/<c>device_revoked</c>/<c>protocol_too_old</c>）。</summary>
    public bool IsTerminal { get; }
}

/// <summary>
///     <c>node.deregister</c> 的载荷。v1 遗留类型，v4 服务端没有注销通道；
///     保留它只为不破坏仍在编译的旧测试与旧文档，运行路径不再发送。
/// </summary>
public sealed record ControlDeregisterRequest(
    [property: JsonPropertyName("group_id")] string GroupId)
{
    public JsonElement ToPayload() =>
        JsonSerializer.SerializeToElement(this, ControlProtocolJson.Options);

    public static bool TryParse(JsonElement? payload, out ControlDeregisterRequest? request, out string reason)
    {
        request = null;
        reason = string.Empty;

        if (payload is not { } element || element.ValueKind is not JsonValueKind.Object)
        {
            reason = "invalid_value:group_id:type_mismatch";
            return false;
        }

        if (!element.TryGetProperty("group_id", out var groupElement)
            || groupElement.ValueKind != JsonValueKind.String)
        {
            reason = "invalid_value:group_id:type_mismatch";
            return false;
        }

        var groupId = (groupElement.GetString() ?? string.Empty).Trim();
        if (groupId.Length == 0)
        {
            reason = "invalid_value:group_id:empty";
            return false;
        }

        request = new ControlDeregisterRequest(groupId);
        return true;
    }
}

/// <summary>
///     v1 遗留的凭据读取接口。v4 的节点凭据是接入记录里的 <c>device_id</c>+<c>secret</c>，
///     不再走 Authorization，也不再回落到 SECTL 账号令牌；保留接口只为旧测试源码兼容。
/// </summary>
public interface IControlNodeCredentialProvider
{
    Task<string?> TryGetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken);
}

/// <summary>"取不到凭据"时上报给界面的细节码。</summary>
public static class ControlNodeCredentialDetailCodes
{
    public const string NotSignedIn = "not_signed_in";

    public const string EnrollmentUnavailable = "not_enrolled";
}

/// <summary>
///     集控节点的本地状态：接入身份、本机开关、已应用的期望状态。
/// </summary>
/// <remarks>
///     <para>
///         <b>本机开关（<see cref="RemoteControlEnabled" />）是设备自己的闸。</b>
///         服务端只读它，永远不会改它；关闭时无论收到什么帧都不执行动作命令。
///         它刻意不放在 <c>settings.json</c> 里：设置导入不该能悄悄打开远控。
///     </para>
///     <para>
///         <see cref="AppliedDesiredStateRevision" /> 必须落盘：重启后归零会让服务端补投的
///         旧期望状态被当成新状态应用。<c>0</c> 是保留值。
///     </para>
/// </remarks>
public sealed record ControlNodeState
{
    /// <summary>v1 的节点身份。v4 不再使用，保留字段以免旧状态文件读不出来。</summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>v1 的组身份。v4 不再使用。</summary>
    public string GroupId { get; init; } = string.Empty;

    /// <summary>接入地点 ID（<c>SC-K7Q2M9</c> 形态），用于设置页展示；未接入为 <c>null</c>。</summary>
    public string? SiteId { get; init; }

    /// <summary>接入地点的显示名。</summary>
    public string? SiteName { get; init; }

    /// <summary>设备当前被指派的班级；<c>null</c> 表示未分班。</summary>
    public string? ClassId { get; init; }

    /// <summary>节点通道地址。默认线上地址，内网联调可指向本机服务端。</summary>
    public string ServerUrl { get; init; } = ControlNodeClientOptions.DefaultEndpoint;

    public bool RemoteControlEnabled { get; init; }

    /// <summary>本机是否在主界面显示"远程抽取"页。默认关闭，只能由用户在这台机器上打开。</summary>
    public bool RemoteDrawPageEnabled { get; init; }

    /// <summary>控制台里显示的名称。留空时上报主机名。</summary>
    public string? DisplayName { get; init; }

    public long AppliedDesiredStateRevision { get; init; }

    /// <summary>最近一次应用的期望状态字段，离线期间也据此保持一致。</summary>
    public bool DrawLocked { get; init; }

    /// <summary>
    ///     最近一次上报给服务端的设置 schema revision。v4.2 要求它单调递增：
    ///     只有序列化后的 schema 内容变化时才 +1，重连/重启复用同一个值。
    ///     <c>0</c> 表示还没上报过（旧状态文件读回来也是 0）。
    /// </summary>
    public int SettingsSchemaRevision { get; init; }

    /// <summary>
    ///     最近一次上报的 schema 内容哈希（SHA-256 小写十六进制）。
    ///     与 revision 一起落盘，重启后才能判断"内容没变就复用 revision"。
    /// </summary>
    public string? SettingsSchemaHash { get; init; }

    /// <summary>通道地址是否有效。v4 的"已接入"以接入记录（<see cref="NodeDeviceCredential" />）为准。</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ServerUrl);
}

/// <summary>本地节点状态的持久化边界。</summary>
public interface IControlNodeStateStore
{
    ControlNodeState Current { get; }

    void Update(Func<ControlNodeState, ControlNodeState> mutate);

    event EventHandler<ControlNodeState>? Changed;
}

/// <param name="CommandId">命令 ID。幂等键。</param>
/// <param name="Capability">命令名（v4 里 <c>cmd</c> 与能力名同一套词汇）。</param>
/// <param name="Kind">v1 遗留字段，v4 恒为 <c>null</c>。</param>
/// <param name="Payload">命令参数（v4 的 <c>args</c>）。</param>
public sealed record ControlCommandInvocation(
    string CommandId,
    string Capability,
    string? Kind,
    JsonElement? Payload);

/// <summary>命令执行结果。失败原因码稳定、不带参数；参数走 <c>Detail</c>。</summary>
public readonly record struct ControlCommandOutcome(
    bool Ok,
    string? Reason,
    JsonElement? Detail = null,
    JsonElement? Payload = null)
{
    public static ControlCommandOutcome Success { get; } = new(true, null);

    public static ControlCommandOutcome SuccessWith(object payload) =>
        new(true, null, null, ToDetail(payload));

    public static ControlCommandOutcome Failure(string reason, object? detail = null) =>
        new(false, reason, ToDetail(detail));

    public static JsonElement? ToDetail(object? detail) => detail switch
    {
        null => null,
        JsonElement element => element,
        _ => JsonSerializer.SerializeToElement(detail, ControlProtocolJson.Options)
    };
}

/// <summary><c>cmd.ack</c>：我**愿意**执行吗？</summary>
public readonly record struct ControlCommandAck(bool Accepted, string? Reason, JsonElement? Detail = null);

/// <summary><c>cmd.result</c>：我**执行成功**了吗？</summary>
public readonly record struct ControlCommandResult(
    bool Ok,
    string? Reason,
    JsonElement? Detail = null,
    JsonElement? Payload = null);

/// <summary>一条命令的完整回执：<c>ack</c> 与 <c>result</c> 回答的是不同问题。</summary>
public sealed record ControlCommandRecord(ControlCommandAck Ack, ControlCommandResult Result);

/// <summary>节点能执行的能力。</summary>
public interface IControlCommandDispatcher
{
    IReadOnlyList<string> DeclaredCapabilities { get; }

    bool CanExecute(string capability);

    Task<ControlCommandOutcome> ExecuteAsync(
        ControlCommandInvocation invocation,
        CancellationToken cancellationToken);
}

/// <summary>节点连接的对外状态，供设置页展示。</summary>
public enum ControlNodeLinkStatus
{
    Disabled,
    Idle,
    Connecting,
    Connected,
    WaitingToRetry,
    Blocked
}

public sealed record ControlNodeLinkState(ControlNodeLinkStatus Status, string? Detail = null, TimeSpan? RetryDelay = null);

/// <summary>节点客户端的连接参数。</summary>
public sealed record ControlNodeClientOptions
{
    /// <summary>线上集控 Hub 的 WebSocket 端点。v4 固定这个路径，不再用 <c>/v1/node/connect</c>。</summary>
    public const string DefaultEndpoint = "wss://sectl.cn/control/ws";

    public string Platform { get; init; } = "windows";

    public string Version { get; init; } = "0.0.0";

    public string HostName { get; init; } = Environment.MachineName;

    /// <summary>等待 <c>hello</c> 应答的上限。服务端等 <c>hello</c> 的上限是 10 秒。</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>服务端未下发 <c>heartbeat_seconds</c> 时的兜底间隔。正常情况下必须用服务端下发的值。</summary>
    public int FallbackHeartbeatSeconds { get; init; } = 25;

    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>连续被拒次数上限（v1 兼容）。v4 的 <c>bad_secret</c> 一次就停。</summary>
    public int MaxConsecutiveUnauthorized { get; init; } = 5;

    /// <summary>退避抖动比例：±20%。</summary>
    public double BackoffJitterRatio { get; init; } = 0.2;
}

/// <summary>单条连接结束的原因。</summary>
public enum ControlSessionEndReason
{
    ConnectionLost,
    Unauthorized,
    Terminal,
    Stopped
}

public sealed record ControlSessionResult(
    ControlSessionEndReason Reason,
    string? Detail = null,
    bool HandshakeCompleted = false);