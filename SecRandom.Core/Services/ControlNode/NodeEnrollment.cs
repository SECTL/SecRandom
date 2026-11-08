namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     设备接入记录的读边界（v4：地点 ID + 配对码换来的 <c>deviceId</c>+<c>secret</c>）。
/// </summary>
/// <remarks>
///     <para>
///         读接口**故意不给 secret 真值**：设置页、移动端、诊断只能看到"有没有接入、接在哪个地点/班级"。
///         真值只经 <see cref="TryGetCredential" /> 交给节点连接与设备树/分班请求。
///     </para>
///     <para>
///         <b>为什么定义在 Core 而不是界面程序集</b>：节点连接循环与界面层的加密实现都要用它，
///         而界面程序集引用 Core、Core 不引用界面程序集，契约只能住在 Core 这一侧；
///         具体怎么加密落盘属于界面层的实现细节。
///     </para>
/// </remarks>
public interface INodeEnrollmentStore
{
    NodeEnrollmentStatus Status { get; }

    /// <summary>凭据换代计数：连接层据此判断"凭据变了，要重连"。</summary>
    int Generation { get; }

    event EventHandler<NodeEnrollmentStatus>? Changed;

    /// <summary>取当前设备凭据；未接入、已清除、记录读不出来时回 <see langword="false" />。</summary>
    /// <remarks>默认实现回 <see langword="false" />：只读的测试替身不必为了编译而实现整个接口。</remarks>
    bool TryGetCredential(out NodeDeviceCredential? credential)
    {
        credential = null;
        return false;
    }

    /// <summary>v1 兼容：v4 没有 Bearer 令牌，永远回 <see langword="false" />。</summary>
    bool TryGetAccessToken(out string? accessToken)
    {
        accessToken = null;
        return false;
    }

    /// <summary>写入一次成功的接入结果。返回是否落盘成功。</summary>
    bool Save(NodeEnrollmentRecord record);

    /// <summary>更新设备被指派的班级（不改变凭据本身）。</summary>
    /// <remarks>默认实现什么也不做：只读的测试替身不必为了编译而实现整个接口。</remarks>
    bool UpdateClass(string? classId, string? className) => false;

    /// <summary>清除接入（用户显式操作，或记录已损坏）；返回是否确实清掉了什么。</summary>
    bool Clear();
}

/// <summary>v4 设备凭据：握手时放在 <c>hello</c> 载荷里，不进 URL、不进 Authorization 头。</summary>
public sealed record NodeDeviceCredential(
    string DeviceId,
    string Secret,
    string SiteId,
    string? SiteName = null,
    string? ClassId = null,
    string? ClassName = null);

/// <summary>一次接入的完整结果。v4 字段与 <c>POST /control/api/enroll</c> 的响应对应。</summary>
/// <remarks>
///     <c>NodeId</c>/<c>GroupId</c>/<c>NodeToken</c> 是 v1 的兼容槽位：旧测试与移动端仍会构造它们，
///     但 v4 的握手只读 <c>DeviceId</c>/<c>Secret</c>，它们不参与任何网络请求。
/// </remarks>
public sealed record NodeEnrollmentRecord
{
    /// <summary>v4：服务端分配的设备 ID（与 <see cref="NodeId" /> 同值）。</summary>
    public string DeviceId { get; init; } = string.Empty;

    /// <summary>v4：设备密钥。**任何界面与日志都不得回显**。</summary>
    public string Secret { get; init; } = string.Empty;

    /// <summary>v4：接入地点 ID（<c>SC-K7Q2M9</c>）。</summary>
    public string SiteId { get; init; } = string.Empty;

    public string? SiteName { get; init; }

    public string? ClassId { get; init; }

    public string? ClassName { get; init; }

    /// <summary>v1 兼容：节点 ID。</summary>
    public string NodeId
    {
        get => string.IsNullOrWhiteSpace(DeviceId) ? string.Empty : DeviceId;
        init => DeviceId = value;
    }

    /// <summary>v1 兼容：组 ID。v4 对应地点 ID。</summary>
    public string GroupId
    {
        get => SiteId;
        init
        {
            if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(SiteId))
                SiteId = value;
        }
    }

    /// <summary>v1 兼容：组显示名。</summary>
    public string? GroupName
    {
        get => SiteName;
        init => SiteName = value;
    }

    /// <summary>v1 兼容：Bearer 令牌。v4 永远为空串。</summary>
    public string NodeToken
    {
        get => string.Empty;
        init { }
    }

    /// <summary>v1 兼容：到期时间。v4 的接入不设到期。</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>接入时间（本机时钟，仅用于展示）。</summary>
    public DateTimeOffset EnrolledAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>对外暴露的接入状态：只有"有没有、接在哪、是谁"，没有 secret 真值。</summary>
public sealed record NodeEnrollmentStatus
{
    /// <summary>是否持有可用凭据。</summary>
    public bool HasCredential { get; init; }

    /// <summary>v1 兼容：等价于 <see cref="HasCredential" />。</summary>
    public bool HasToken
    {
        get => HasCredential;
        init => HasCredential = value;
    }

    /// <summary>v4 的接入没有本地过期时间；恒为 <see langword="false" />。</summary>
    public bool IsExpired { get; init; }

    /// <summary>接入记录存在但读不出来（被改过、换了机器、密钥丢了），已按"未接入"处理。</summary>
    public bool IsUnreadable { get; init; }

    public string? DeviceId { get; init; }

    /// <summary>v1 兼容：等价于 <see cref="DeviceId" />。</summary>
    public string? NodeId
    {
        get => DeviceId;
        init => DeviceId = value;
    }

    public string? SiteId { get; init; }

    /// <summary>v1 兼容：等价于 <see cref="SiteId" />。</summary>
    public string? GroupId
    {
        get => SiteId;
        init => SiteId = value;
    }

    public string? SiteName { get; init; }

    /// <summary>v1 兼容：等价于 <see cref="SiteName" />。</summary>
    public string? GroupName
    {
        get => SiteName;
        init => SiteName = value;
    }

    public string? ClassId { get; init; }

    public string? ClassName { get; init; }

    public string? DeviceName { get; init; }

    /// <summary>v1 兼容：v4 不设到期。</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public static NodeEnrollmentStatus NotEnrolled { get; } = new();
}

/// <summary>设备树：地点 + 年级/班级结构，以及本机当前分班。</summary>
public sealed record ControlDeviceTree
{
    public string? SiteId { get; init; }

    public string? SiteName { get; init; }

    public IReadOnlyList<ControlGradeNode> Grades { get; init; } = [];

    public string? DeviceId { get; init; }

    public string? DeviceName { get; init; }

    public string? ClassId { get; init; }

    /// <summary>把所有班级摊平，便于设置页按 ID 找名字。</summary>
    public IEnumerable<(string GradeId, string GradeName, string ClassId, string ClassName)> EnumerateClasses()
    {
        foreach (var grade in Grades)
        foreach (var classroom in grade.Classes)
            yield return (grade.Id, grade.Name, classroom.Id, classroom.Name);
    }
}

public sealed record ControlGradeNode(string Id, string Name, IReadOnlyList<ControlClassNode> Classes);

public sealed record ControlClassNode(string Id, string Name);