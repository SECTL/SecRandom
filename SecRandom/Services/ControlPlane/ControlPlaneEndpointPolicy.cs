namespace SecRandom.Services.ControlPlane;

/// <summary>
///     控制面（集控 REST）基址的校验与规范化。
/// </summary>
/// <remarks>
///     <para>
///         这个地址**不是**一个普通字符串设置：控制面的每一次请求都会把本账号的 SECTL access token
///         作为 <c>Authorization</c> 头送到那里（见 <see cref="ControlPlaneClient" />）。因此规则比
///         "能不能连上"更严——明文 <c>http</c> 只允许回环（本机联调），用户信息、查询参数与片段一律拒绝
///         （它们要么会进访问日志，要么根本不是基址的一部分）。
///     </para>
///     <para>
///         允许带路径前缀（例如 <c>https://example.com/secrandom</c>）：自建部署常把服务挂在反向代理的
///         子路径下。规范化只去掉结尾的 <c>/</c>，路径本身逐字保留——请求路径是拼在基址后面的
///         （<c>{base}/v1/groups</c>），多一个斜杠就会打出 <c>//v1/groups</c>。
///     </para>
/// </remarks>
public static class ControlPlaneEndpointPolicy
{
    /// <param name="endpoint">用户输入或配置文件里的地址。</param>
    /// <param name="normalized">去掉结尾斜杠后的基址，校验通过时才有值。</param>
    /// <param name="error">稳定的错误码，用于界面文案与日志，不含参数。</param>
    public static bool TryValidate(string? endpoint, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            error = "empty_endpoint";
            return false;
        }

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var parsed)
            || string.IsNullOrEmpty(parsed.Host))
        {
            error = "invalid_endpoint";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment))
        {
            error = "endpoint_must_not_carry_credentials";
            return false;
        }

        switch (parsed.Scheme)
        {
            case "https":
                break;

            case "http" when IsLoopback(parsed.Host):
                break;

            case "http":
                // 明文出网会把 Bearer 令牌交给链路上的任何人。
                error = "plaintext_endpoint_requires_loopback";
                return false;

            default:
                error = "unsupported_endpoint_scheme";
                return false;
        }

        normalized = parsed.GetLeftPart(UriPartial.Path).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            error = "invalid_endpoint";
            return false;
        }

        return true;
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
        || string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "::1", StringComparison.Ordinal);
}
