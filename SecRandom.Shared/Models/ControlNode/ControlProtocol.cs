namespace SecRandom.Shared.Models.ControlNode;

/// <summary>
///     集控协议（<c>control-v1</c>）的节点能力名。
/// </summary>
/// <remarks>
///     <para>
///         能力名是**协议的一部分**：用字符串常量而不是枚举，任何语言都能实现，
///         且允许节点在不发服务端版本的前提下新增能力。未知能力一律忽略而不是报错。
///     </para>
///     <para>
///         节点只能声明**自己真的实现**的能力。声明了却执行不了，服务端就会下发
///         注定失败的命令，控制台看到的是一条"设备已接受但失败"的运行故障。
///     </para>
/// </remarks>
public static class ControlCapabilities
{
    /// <summary>读版本 / 在线 / 当前班级等非敏感状态。**所有平台的默认能力。**</summary>
    public const string StatusRead = "node.status.read";

    /// <summary>读本地证明列表。</summary>
    public const string ProofList = "proof.list";

    /// <summary>设置"是否允许当前抽取"（期望状态字段 <c>draw_locked</c>）。</summary>
    public const string DrawLock = "draw.lock";

    /// <summary>立即触发一次抽取（动作命令）。</summary>
    public const string DrawTrigger = "draw.trigger";

    /// <summary>
    ///     设备的 <c>draw.trigger</c> 支持 <c>conditions</c> 子对象（版本 1：标签筛选 + 发放对象范围）。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>这是一张"能力声明"，不是一条可下发的命令</b>：控制台必须在发 <c>conditions</c> **之前**
    ///         先看设备有没有声明它。原因很硬——本次改动之前的设备根本不认识 <c>conditions</c>，
    ///         会把它当成"没写"从而**静默按整池抽**，而"设了条件其实没生效"是本项目的红线。
    ///         版本号写在 payload 里挡不住这件事（老设备压根不看 payload），只有能力声明能挡。
    ///     </para>
    ///     <para>
    ///         升版本时**新增**能力名（例如 <c>draw.trigger.conditions.v2</c>），不要改这一条的含义。
    ///     </para>
    /// </remarks>
    public const string DrawTriggerConditions = "draw.trigger.conditions";

    /// <summary>
    ///     清空"本轮临时记录"（动作命令）。
    /// </summary>
    /// <remarks>
    ///     <b>只清抽取进度，不碰历史记录</b>：历史（<c>data/history/**</c>）是名单的长期账本，
    ///     远程能清的只有"这一轮抽到谁"的临时状态。名字与文档都必须说到这一点，
    ///     否则"重置"很容易被理解成"清历史"。
    /// </remarks>
    public const string DrawReset = "draw.reset";

    /// <summary>显示结果 / 播报。</summary>
    public const string MediaPlay = "media.play";

    /// <summary>读名单。名单含学生姓名，权限高于状态读取。</summary>
    public const string RosterRead = "roster.read";

    /// <summary>
    ///     读设置。**只读**：按分类返回设置及其类型/范围/是否可远程写，**不含任何凭据**。
    /// </summary>
    /// <remarks>
    ///     权限低于 <see cref="SettingsWrite" />：看得见和改得动是两件事，
    ///     老师应该能看见自己这台机器的配置，改它才需要管理员。
    /// </remarks>
    public const string SettingsRead = "settings.read";

    /// <summary>写名单。</summary>
    public const string RosterWrite = "roster.write";

    /// <summary>改抽取与通知设置。</summary>
    public const string SettingsWrite = "settings.write";

    /// <summary>
    ///     <c>node.restart</c>：**已被服务端主动否决**，故意不在授权表里。
    /// </summary>
    /// <remarks>
    ///     常量保留是为了让控制台能明确显示"本平台已移除"，而不是让管理员以为是自己没权限。
    ///     **客户端不得实现这条能力**：它既不会通过服务端授权，实现它也只是多出一条攻击面。
    /// </remarks>
    public const string RestartRemoved = "node.restart";

    /// <summary>集控节点当前可能声明的能力全集（不含已否决的 <see cref="RestartRemoved" />）。</summary>
    /// <remarks>
    ///     跟着 <c>ControlCommandDispatcher.DeclaredCapabilities</c> 走：那张表才是"今天真的声明了什么"。
    ///     这里漏一项不会让功能坏掉（没有任何生产代码按它过滤），但会让下一个读代码的人
    ///     以为某个能力不存在——它已经这样误导过一次。
    /// </remarks>
    public static IReadOnlyList<string> Known { get; } =
    [
        StatusRead,
        ProofList,
        DrawLock,
        DrawTrigger,
        DrawTriggerConditions,
        MediaPlay,
        RosterRead,
        RosterWrite,
        SettingsRead,
        SettingsWrite
    ];
}

/// <summary>帧的 <c>type</c> 判别值。</summary>
public static class ControlFrameTypes
{
    // 节点 → 服务端
    public const string Hello = "hello";
    public const string Heartbeat = "heartbeat";
    public const string Ack = "command.ack";
    public const string Result = "command.result";

    // 服务端 → 节点
    public const string HelloAck = "hello.ack";
    public const string Command = "command";
    public const string DesiredState = "desired_state";
    public const string Error = "error";
}

/// <summary>
///     握手失败的错误码。服务端会**先发一帧可读错误再关闭连接**。
/// </summary>
/// <remarks>
///     必须区分"值得重试"与"重试一万次也不会成功"：
///     <see cref="Unauthorized" /> 值得刷新凭据后重试，
///     其余三个都是**不要重试**的终态，反复重连只会刷日志、打满服务端。
/// </remarks>
public static class ControlErrorCodes
{
    /// <summary>凭据无效或缺失。刷新凭据后重试，不要死循环。</summary>
    public const string Unauthorized = "unauthorized";

    /// <summary>不是 WebSocket / 首帧不是 <c>hello</c> / 缺 <c>node_id</c> 或 <c>group_id</c>。**客户端 bug，不要重试。**</summary>
    public const string InvalidRequest = "invalid_request";

    /// <summary>组不存在，**或你不是该组成员**。两者共用同一码是有意为之：不泄漏"这个组是否存在"。</summary>
    public const string GroupNotFound = "group_not_found";

    /// <summary>该组下没有这个 <c>node_id</c>：先去控制台注册。</summary>
    public const string NodeNotFound = "node_not_found";
}

/// <summary><c>command.ack</c> 里 <c>accepted: false</c> 的原因码。</summary>
public static class ControlRejectReasons
{
    /// <summary>命令已过期。**丢弃，绝不补执行。**</summary>
    public const string Expired = "expired";

    /// <summary>本机开关关闭：这是设备自己的闸，与服务端判定无关。</summary>
    public const string LocalRemoteDisabled = "local_remote_disabled";

    /// <summary>本机不支持该能力。</summary>
    public const string CapabilityUnsupported = "capability_unsupported";

    /// <summary>命令缺少 <c>command_id</c> 或载荷无效。</summary>
    public const string InvalidCommand = "invalid_command";

    /// <summary>
    ///     命令到达得太快，超过了节点自己的限流窗口。
    /// </summary>
    /// <remarks>
    ///     协议 §12 明确说明服务端**没有命令级限流**，并建议客户端自行防御。节点自己数得清
    ///     每条连接的命令密度，那就不该让一个失控的控制台把"立即抽取"变成连续触发。
    /// </remarks>
    public const string RateLimited = "rate_limited";

    /// <summary>执行失败（运行故障，不是拒绝）。</summary>
    public const string ExecutionFailed = "execution_failed";
}

/// <summary>服务端下发的命令种类。</summary>
public static class ControlCommandKinds
{
    /// <summary>动作型：执行一次即刻行为，**必须带过期时间**。</summary>
    public const string Action = "action";

    /// <summary>期望状态：不是"做一次"，而是"应该是什么样"，靠 <c>revision</c> 单调收敛。</summary>
    public const string SetDesiredState = "set_desired_state";

    /// <summary>查询型：读一次状态，答案在 <c>command.result.result_payload</c> 里回来，存活期很短。</summary>
    /// <remarks>
    ///     客户端**不按 <c>kind</c> 分派**（服务端也不校验它），而是按能力分派；
    ///     这条常量是为了让载荷契约能被写清楚，不是为了在这里做分支。
    /// </remarks>
    public const string Query = "query";
}

/// <summary>
///     <c>desired_state</c> 的载荷。当前协议只定义了 <c>draw_locked</c> 一个字段，
///     **其它字段一律忽略**——服务端不做投机性设计，加新字段时会有明确的协议说明。
/// </summary>
/// <param name="DrawLocked">是否禁止当前抽取。</param>
public sealed record ControlDesiredState(bool DrawLocked)
{
    /// <summary>从未设置过期望状态时的判定基础：协议保留 <c>0</c>，真实 revision 从 1 开始。</summary>
    public const long NeverSetRevision = 0;
}
