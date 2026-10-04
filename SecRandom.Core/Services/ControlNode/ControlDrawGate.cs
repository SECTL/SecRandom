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
