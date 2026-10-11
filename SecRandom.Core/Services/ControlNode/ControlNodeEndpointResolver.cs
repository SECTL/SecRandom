namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     由集控平台基址推导节点通道地址。
/// </summary>
/// <remarks>
///     <para>
///         自建集控的接入码只换回令牌与身份，响应里**没有**节点通道地址，所以通道必须从控制面基址推出来：
///         <c>http</c> → <c>ws</c>、<c>https</c> → <c>wss</c>，主机与端口原样保留，路径固定 <c>/v1/node/connect</c>。
///     </para>
///     <para>
///         为什么必须推导而不是让用户自己填：基址 <c>http://127.0.0.1:8792</c> 配 <c>wss://127.0.0.1:8792/…</c>
///         时 WebSocket 会先去和明文端口做 TLS 握手，必然失败——客户端只会不停重连，服务端既看不到 hello
///         也看不到心跳，于是控制台里这台机器永远离线、<c>local_remote_allowed</c> 也永远停在 false。
///     </para>
///     <para>
///         用户已经把通道指到**别的**主机上时以用户为准：那种情况下他要连的本来就不是这台集控。
///     </para>
/// </remarks>
public static class ControlNodeEndpointResolver
{
    /// <summary>节点通道在集控平台上的固定路径。</summary>
    public const string NodeChannelPath = "/v1/node/connect";

    /// <summary>把控制面基址映射成节点通道地址；基址不是绝对的 http/https 时返回 <c>null</c>。</summary>
    public static string? TryDeriveFromControlPlane(string? controlPlaneBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(controlPlaneBaseUrl))
            return null;

        if (!Uri.TryCreate(controlPlaneBaseUrl.Trim(), UriKind.Absolute, out var parsed))
            return null;

        var scheme = parsed.Scheme switch
        {
            "http" => "ws",
            "https" => "wss",
            _ => null
        };

        if (scheme is null || string.IsNullOrWhiteSpace(parsed.Authority))
            return null;

        return $"{scheme}://{parsed.Authority}{NodeChannelPath}";
    }

    /// <summary>
    ///     算出真正该用的节点通道地址。
    /// </summary>
    /// <param name="controlPlaneBaseUrl">控制面基址（自建集控就是用户填的那个地址）。</param>
    /// <param name="configuredServerUrl">节点状态里已保存的通道地址。</param>
    /// <param name="corrected">为 <c>true</c> 时表示应当把返回值写回节点状态。</param>
    public static string Resolve(string? controlPlaneBaseUrl, string? configuredServerUrl, out bool corrected)
    {
        var configured = string.IsNullOrWhiteSpace(configuredServerUrl)
            ? ControlNodeClientOptions.DefaultEndpoint
            : configuredServerUrl.Trim();

        corrected = false;

        var derived = TryDeriveFromControlPlane(controlPlaneBaseUrl);
        if (derived is null)
            return configured;

        // 还是出厂默认值 ⇒ 用户从没手工指定过节点通道，跟着这台集控走。
        if (string.Equals(configured, ControlNodeClientOptions.DefaultEndpoint, StringComparison.Ordinal))
        {
            corrected = !string.Equals(derived, configured, StringComparison.Ordinal);
            return corrected ? derived : configured;
        }

        // 已经指向同一台集控、只是协议/路径不对（典型：http 基址存成了 wss 通道）⇒ 按基址修正。
        if (Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri)
            && Uri.TryCreate(controlPlaneBaseUrl!.Trim(), UriKind.Absolute, out var baseUri)
            && string.Equals(configuredUri.Authority, baseUri.Authority, StringComparison.OrdinalIgnoreCase))
        {
            corrected = !string.Equals(derived, configured, StringComparison.Ordinal);
            return corrected ? derived : configured;
        }

        // 指到别的主机：用户是有意为之，不覆盖。
        return configured;
    }
}
