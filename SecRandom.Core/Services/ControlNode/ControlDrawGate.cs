namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     集控期望状态对本地抽取的闸门。
/// </summary>
/// <remarks>
///     <para>
///         服务端通过期望状态字段 <c>draw_locked</c> 表达"现在是否允许抽取"。它必须
///         **在本机生效**，否则这个能力就只是控制台上的一行字：老师锁了抽取，教室机照抽不误。
///     </para>
///     <para>
///         闸门只在**本机允许被集控**（<see cref="ControlNodeState.RemoteControlEnabled" />）时生效：
///         老师关掉本机开关就等于收回远控权，之前下发的锁定不应继续影响本机。
///     </para>
/// </remarks>
public interface IControlDrawGate
{
    /// <summary>当前是否被集控锁定抽取。</summary>
    bool IsDrawLocked { get; }
}

/// <summary>
///     没有本地集控节点的宿主使用的闸门：永远不锁定。
/// </summary>
/// <remarks>
///     <para>
///         手机端只是集控的<b>控制台侧</b>（<c>MobileRemoteDrawViewModel</c> 把 <c>draw.trigger</c> 下发给
///         桌面节点），本机没有出站节点通道，也就不会有服务端下发的 <c>draw_locked</c>。
///     </para>
///     <para>
///         但 <c>LinkageDrawCoordinator</c> 在桌面与手机上是同一份注册，它<b>必须</b>能解析到
///         <see cref="IControlDrawGate" />：缺失时不是"少个可选功能"，而是整台 Host 起不来——
///         <c>Host.StartAsync</c> 解析 <c>IEnumerable&lt;IHostedService&gt;</c> 时会构造
///         <c>GlobalShortcutService</c>，它注入的抽取页 ViewModel 一路拉到这个闸门。
///     </para>
///     <para>
///         桌面端<b>刻意不使用</b>这个实现：真实闸门（<c>ControlDrawGateService</c>）的注册一旦丢失，
///         应该当场失败，而不是悄悄放行每一次抽取。
///     </para>
/// </remarks>
public sealed class UnlockedControlDrawGate : IControlDrawGate
{
    public bool IsDrawLocked => false;
}

/// <summary>
///     节点通道地址校验。
/// </summary>
/// <remarks>
///     凭据走 <c>Authorization</c> 头，因此地址本身不携带秘密；但明文 <c>ws://</c> 会把
///     Bearer token 暴露在链路上，所以只允许 <c>wss://</c>，以及**仅回环**的开发联调明文地址。
/// </remarks>
public static class ControlEndpointPolicy
{
    public static bool TryValidate(string? endpoint, out Uri? uri, out string? error)
    {
        uri = null;
        error = null;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            error = "empty_endpoint";
            return false;
        }

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var parsed))
        {
            error = "invalid_endpoint";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
        {
            // 查询参数会进访问日志；用户信息与片段同样不该出现在节点地址里。
            error = "endpoint_must_not_carry_credentials";
            return false;
        }

        switch (parsed.Scheme)
        {
            case "wss":
                uri = parsed;
                return true;

            case "ws" when IsLoopback(parsed.Host):
                uri = parsed;
                return true;

            case "ws":
                error = "plaintext_endpoint_requires_loopback";
                return false;

            default:
                error = "unsupported_endpoint_scheme";
                return false;
        }
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
        || string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "::1", StringComparison.Ordinal);
}
