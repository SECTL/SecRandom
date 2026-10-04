using System.Text.Json;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     一个帧超过协议上限（64 KiB）——它**根本发不出去**，不是网络故障。
/// </summary>
/// <remarks>
///     单独一个异常类型，是为了让会话能把"这一帧太大"与"连接坏了"分开：
///     前者可以改回一条最小的失败回执（它一定装得下），让对端立刻知道答案没能给；
///     后者只能失败并等重连。靠比字符串来判断这件事，迟早会因为一句文案改了就失效。
/// </remarks>
public sealed class ControlFrameTooLargeException(string frameType, int bytes, int limit)
    : InvalidOperationException($"集控帧超过 {limit} 字节上限：{frameType}（实测 {bytes} 字节）")
{
    public string FrameType { get; } = frameType;

    public int Bytes { get; } = bytes;

    public int Limit { get; } = limit;
}

/// <summary>
///     集控节点通道的传输边界。
/// </summary>
/// <remarks>
///     协议引擎只依赖这个接口，不依赖 <c>ClientWebSocket</c>：会话状态机因此可以在
///     测试里用一个内存传输驱动，而不需要真的起一个 WebSocket 服务端。
/// </remarks>
public interface IControlNodeTransport : IAsyncDisposable
{
    Task SendAsync(ControlFrame frame, CancellationToken cancellationToken);

    /// <summary>接收一帧。返回 <c>null</c> 表示连接已经结束。</summary>
    Task<ControlFrame?> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     对端关闭连接时给出的原因，例如服务端接管同一 <c>node_id</c> 时的 <c>replaced</c>。
    /// </summary>
    /// <remarks>
    ///     协议里多数断开是静默的（服务端不发错误帧），但"同一 <c>node_id</c> 被新连接接管"
    ///     这一种带 close reason。把它透出来，"莫名掉线"才能变成"有另一台机器在用同一个 node_id"
    ///     这种可以直接排查的结论。没有原因时为 <c>null</c>。
    /// </remarks>
    string? CloseReason { get; }
}

/// <summary>建立一条节点连接。带凭据的地址由实现负责，凭据**不得进入 URL**。</summary>
public interface IControlNodeTransportFactory
{
    Task<IControlNodeTransport> ConnectAsync(ControlNodeConnectRequest request, CancellationToken cancellationToken);
}

/// <param name="Endpoint">服务端地址，例如 <c>wss://secrandom-control.sectl.cn/v1/node/connect</c>。</param>
/// <param name="BearerToken">SECTL access token。**走 Authorization 头，不放查询参数**（查询参数会进访问日志）。</param>
/// <param name="NodeId">节点身份。</param>
/// <param name="GroupId">节点所属组。</param>
public sealed record ControlNodeConnectRequest(string Endpoint, string BearerToken, string NodeId, string GroupId);

/// <summary>
///     取当前可用的 SECTL 凭据。
/// </summary>
/// <remarks>
///     节点通道使用 <c>SectlBearer</c> 方案（直接出示 SECTL access token），
///     与控制台浏览器的会话 cookie 无关。未登录时必须返回 <c>null</c>，
///     让客户端等待登录而不是拿一个空 token 去撞 <c>unauthorized</c>。
/// </remarks>
public interface IControlNodeCredentialProvider
{
    Task<string?> TryGetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken);
}

/// <summary>
///     集控节点的本地状态：身份、本机开关、已应用的期望状态。
/// </summary>
/// <remarks>
///     <para>
///         <b>本机开关（<see cref="RemoteControlEnabled" />）是设备自己的闸。</b>
///         服务端只读它，永远不会改它；关闭时无论收到什么帧都不执行动作命令。
///         它刻意**不放在 <c>settings.json</c> 里**：设置备份/导入能改 settings，
///         而"是否允许被远控"不该被一次导入悄悄打开。
///     </para>
///     <para>
///         <see cref="AppliedDesiredStateRevision" /> 必须落盘：重启后归零会让服务端
///         补投的旧期望状态被当成新状态应用。<c>0</c> 是保留值，真实 revision 从 1 开始。
///     </para>
/// </remarks>
public sealed record ControlNodeState
{
    public string NodeId { get; init; } = string.Empty;

    public string GroupId { get; init; } = string.Empty;

    /// <summary>节点通道地址。默认线上地址，开发联调可指向本机服务端。</summary>
    public string ServerUrl { get; init; } = ControlNodeClientOptions.DefaultEndpoint;

    public bool RemoteControlEnabled { get; init; }

    /// <summary>
    ///     控制台里显示的名称。**由用户在这台机器上填写**；留空时上报主机名。
    /// </summary>
    /// <remarks>
    ///     取值本身保持原样（含首尾空白），规范化只发生在两个边界：落盘时统一成"空白即无"，
    ///     上报时由 <see cref="ControlNodeDisplayName.Resolve" /> 解析。
    ///     在每次按键时 Trim 会跟用户的输入光标打架（想在词中间打空格都做不到）。
    /// </remarks>
    public string? DisplayName { get; init; }

    public long AppliedDesiredStateRevision { get; init; }

    /// <summary>最近一次应用的期望状态字段，离线期间也据此保持一致。</summary>
    public bool DrawLocked { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(NodeId) && !string.IsNullOrWhiteSpace(GroupId);
}

/// <summary>本地节点状态的持久化边界。</summary>
public interface IControlNodeStateStore
{
    ControlNodeState Current { get; }

    /// <summary>原子地更新并落盘，随后触发 <see cref="Changed" />。</summary>
    void Update(Func<ControlNodeState, ControlNodeState> mutate);

    /// <summary>状态变化（含本机开关、期望状态）。用于立刻上报一次心跳。</summary>
    event EventHandler<ControlNodeState>? Changed;
}


/// <param name="CommandId">命令 ID。幂等键。</param>
/// <param name="Capability">命令声明的能力。</param>
/// <param name="Kind">命令种类，见 <see cref="ControlCommandKinds" />。</param>
/// <param name="Payload">能力自描述的载荷，服务端不理解其内部结构。</param>
public sealed record ControlCommandInvocation(
    string CommandId,
    string Capability,
    string? Kind,
    JsonElement? Payload);

/// <param name="Ok">执行是否成功。拒绝（未执行）不走这个结果。</param>
/// <param name="Reason">
///     失败原因码。**只是一个稳定的码**，不带参数——参数走 <paramref name="Detail" />。
/// </param>
/// <param name="Detail">
///     结构化上下文（可选）。原因码回答"哪一类失败"，这里回答"具体是什么情况"，
///     例如 <c>media_disabled</c> 配上 <c>{ "voice_enable": false }</c>。
/// </param>
/// <param name="Payload">
///     <c>query</c> 类命令返回的数据本体（可选）。服务端与中转都不解释它，只搬运。
/// </param>
/// <remarks>
///     <para>
///         为什么不让设备直接回一句人话：文案要按**看控制台的人**的语言渲染，
///         而不是按设备本机的语言。设备只回"码 + 结构化事实"，控制台/服务端负责翻译，
///         否则一台日语教室机就会把日语提示塞给中文管理员。
///     </para>
///     <para>
///         也不把参数编进原因码（<c>not_writable:xxx</c>）：那样控制台只能靠切字符串猜，
///         码本身也不再稳定。参数永远放 <paramref name="Detail" />。
///     </para>
/// </remarks>
public readonly record struct ControlCommandOutcome(
    bool Ok,
    string? Reason,
    JsonElement? Detail = null,
    JsonElement? Payload = null)
{
    public static ControlCommandOutcome Success { get; } = new(true, null);

    /// <summary>查询类命令的成功返回：带上数据本体。</summary>
    public static ControlCommandOutcome SuccessWith(object payload) =>
        new(true, null, null, ToDetail(payload));

    public static ControlCommandOutcome Failure(string reason, object? detail = null) =>
        new(false, reason, ToDetail(detail));

    /// <summary>把匿名对象/字典转成 detail；<c>null</c> 表示没有额外上下文。</summary>
    public static JsonElement? ToDetail(object? detail) => detail switch
    {
        null => null,
        JsonElement element => element,
        _ => JsonSerializer.SerializeToElement(detail, ControlProtocolJson.Options)
    };
}

/// <summary><c>command.ack</c>：我**愿意**执行吗？</summary>
public readonly record struct ControlCommandAck(bool Accepted, string? Reason, JsonElement? Detail = null);

/// <summary><c>command.result</c>：我**执行成功**了吗？</summary>
public readonly record struct ControlCommandResult(
    bool Ok,
    string? Reason,
    JsonElement? Detail = null,
    JsonElement? Payload = null);

/// <summary>
///     一条命令的完整回执。
/// </summary>
/// <remarks>
///     <c>ack</c> 与 <c>result</c> 是两帧，回答的是不同问题：把两者合成一个"成功"
///     会掩盖"设备拒绝了"（配置问题）与"设备接受了但失败了"（运行故障）的区别。
/// </remarks>
public sealed record ControlCommandRecord(ControlCommandAck Ack, ControlCommandResult Result);

/// <summary>
///     节点能执行的能力。
/// </summary>
/// <remarks>
///     <para>
///         <see cref="DeclaredCapabilities" /> 是 <c>hello</c>/<c>heartbeat</c> 里上报给服务端的
///         能力清单——**只能包含本机真的实现了的能力**；声明了却执行不了，服务端就会下发
///         注定失败的命令。未知能力一律忽略而不是报错（协议允许新增）。
///     </para>
///     <para>
///         <see cref="CanExecute" /> 单独存在是因为能力有两种类型：<c>draw.lock</c> 是**期望状态**，
///         它靠 <c>desired_state</c> 的 revision 单调收敛，不接受动作命令；因此声明它、
///         但对一条 <c>draw.lock</c> 动作命令回 <c>capability_unsupported</c>。
///     </para>
/// </remarks>
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
    /// <summary>本机开关关闭：不连接，也不执行任何动作命令。</summary>
    Disabled,

    /// <summary>尚未配置 <c>node_id</c>/<c>group_id</c>，或尚未登录 SECTL 账号。</summary>
    Idle,

    Connecting,

    Connected,

    /// <summary>连接失败，正在退避重试。</summary>
    WaitingToRetry,

    /// <summary>终态：未注册、不是组成员、请求非法或凭据不可用。重试没有意义。</summary>
    Blocked
}

/// <param name="Status">当前连接状态。</param>
/// <param name="Detail">面向诊断的原因（错误码或异常摘要），不是用户文案。</param>
/// <param name="RetryDelay">下一次重试的等待时间（<see cref="ControlNodeLinkStatus.WaitingToRetry" /> 时有效）。</param>
public sealed record ControlNodeLinkState(ControlNodeLinkStatus Status, string? Detail = null, TimeSpan? RetryDelay = null);

/// <summary>节点客户端的连接参数。</summary>
public sealed record ControlNodeClientOptions
{
    public const string DefaultEndpoint = "wss://secrandom-control.sectl.cn/v1/node/connect";

    /// <summary>协议里的平台标识，例如 <c>windows</c>。</summary>
    public string Platform { get; init; } = "windows";

    /// <summary>上报给服务端的客户端版本。</summary>
    public string Version { get; init; } = "0.0.0";

    /// <summary>
    ///     设备显示名的**回落值**：用户没在设置页填名字时上报它。
    /// </summary>
    /// <remarks>
    ///     默认取主机名。放在这里而不是各处直接读 <see cref="Environment.MachineName" />，
    ///     是为了让会话状态机在测试里能注入一个确定的值，也保证"设置页的提示"与
    ///     "真正上报的值"永远是同一个来源。
    /// </remarks>
    public string HostName { get; init; } = Environment.MachineName;

    /// <summary>等待 <c>hello.ack</c> 的上限。服务端等 <c>hello</c> 的上限是 15 秒。</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>服务端未下发 <c>heartbeat_seconds</c> 时的兜底间隔。正常情况下**必须用服务端下发的值**。</summary>
    public int FallbackHeartbeatSeconds { get; init; } = 25;

    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>退避上限。上百台设备同时重连会把服务端打满，所以退避是必须的。</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>连续 <c>unauthorized</c> 次数上限：超过则停止重试，等用户重新登录。</summary>
    public int MaxConsecutiveUnauthorized { get; init; } = 5;
}

/// <summary>单条连接结束的原因。</summary>
public enum ControlSessionEndReason
{
    /// <summary>连接断开（网络、对端关闭、心跳发送失败）。值得退避重试。</summary>
    ConnectionLost,

    /// <summary>凭据被拒。刷新凭据后重试。</summary>
    Unauthorized,

    /// <summary>重试也没有意义的终态：<c>invalid_request</c> / <c>node_not_found</c> / <c>group_not_found</c>。</summary>
    Terminal,

    /// <summary>本地要求停止（开关关闭、配置变化、进程退出）。</summary>
    Stopped
}

/// <param name="Reason">结束原因。</param>
/// <param name="Detail">错误码或异常摘要。</param>
/// <param name="HandshakeCompleted">是否已经收到过 <c>hello.ack</c>。</param>
public sealed record ControlSessionResult(
    ControlSessionEndReason Reason,
    string? Detail = null,
    bool HandshakeCompleted = false);
