namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     接入令牌（自建集控的节点凭据）的读边界。
/// </summary>
/// <remarks>
///     <para>
///         读接口**故意不给令牌真值**：设置页、移动端、诊断都只能看到"有没有接入、什么时候到期"，
///         拿不到 <c>srn_…</c> 本身。真值只经 <see cref="TryGetAccessToken" /> 交给节点连接与 REST 发送边界，
///         因此"界面回显令牌"这种事在类型上就写不出来。
///     </para>
///     <para>
///         <b>为什么定义在 Core 而不是界面程序集</b>：这段契约同时被节点连接循环
///         （<see cref="ControlNodeClient" />，"取不到凭据"要分辨"未登录账号"还是"未接入"）
///         和界面层的加密实现使用，而界面程序集引用 Core、Core 不引用界面程序集，
///         契约只能住在 Core 这一侧；具体怎么加密落盘属于界面层的实现细节。
///     </para>
/// </remarks>
public interface INodeEnrollmentStore
{
    /// <summary>当前接入状态（不含令牌真值）。</summary>
    NodeEnrollmentStatus Status { get; }

    /// <summary>令牌换代计数：连接层据此判断"令牌变了，要重连"。</summary>
    int Generation { get; }

    /// <summary>接入状态变化（接入成功/清除/损坏后丢弃）。</summary>
    event EventHandler<NodeEnrollmentStatus>? Changed;

    /// <summary>
    ///     取当前可用的节点令牌；未接入、已清除、记录读不出来时回 <see langword="null" />。
    /// </summary>
    /// <remarks>
    ///     过期与否**由服务端判定**：本地过 <c>expires_at</c> 时照样把令牌交出去，
    ///     让服务端回 401——本地时钟不可信，凭它单方面断连会把只是时钟偏了的教室机踢下线。
    /// </remarks>
    bool TryGetAccessToken(out string? accessToken);

    /// <summary>写入一次成功的接入结果。返回是否落盘成功。</summary>
    bool Save(NodeEnrollmentRecord record);

    /// <summary>清除接入（用户显式操作，或记录已损坏）；返回是否确实清掉了什么。</summary>
    bool Clear();
}

/// <summary>一次接入的完整结果。字段与 <c>POST /v1/node/enroll</c> 的响应一一对应。</summary>
public sealed record NodeEnrollmentRecord
{
    /// <summary>服务端分配的节点 ID（与接入前本机自报的那个可以不同，以服务端为准）。</summary>
    public required string NodeId { get; init; }

    /// <summary>节点所属组 ID。</summary>
    public required string GroupId { get; init; }

    /// <summary>组显示名（仅用于界面展示）。</summary>
    public string? GroupName { get; init; }

    /// <summary>节点令牌真值（<c>srn_…</c>）。**任何界面与日志都不得回显**。</summary>
    public required string NodeToken { get; init; }

    /// <summary>服务端给出的到期时间；为空表示服务端没有设期限。</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>接入时间（本机时钟，仅用于展示）。</summary>
    public DateTimeOffset EnrolledAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
///     对外暴露的接入状态：**只有"有没有、什么时候到期、是谁"**，没有令牌真值。
/// </summary>
public sealed record NodeEnrollmentStatus
{
    /// <summary>是否持有可用的接入令牌。</summary>
    public bool HasToken { get; init; }

    /// <summary>本地看是否已过 <c>expires_at</c>（仅供界面提示；是否真的失效由服务端说了算）。</summary>
    public bool IsExpired { get; init; }

    /// <summary>接入记录存在但读不出来（被改过、换了机器、密钥丢了），已按"未接入"处理。</summary>
    public bool IsUnreadable { get; init; }

    /// <summary>接入到的节点 ID。</summary>
    public string? NodeId { get; init; }

    /// <summary>接入到的组 ID。</summary>
    public string? GroupId { get; init; }

    /// <summary>接入到的组显示名。</summary>
    public string? GroupName { get; init; }

    /// <summary>服务端给出的到期时间。</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>未接入。</summary>
    public static NodeEnrollmentStatus NotEnrolled { get; } = new();
}
