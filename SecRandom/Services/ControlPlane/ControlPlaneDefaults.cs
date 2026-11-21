namespace SecRandom.Services.ControlPlane;

/// <summary>
///     集控平台地址常量。**控制台与服务端 API 都在这个站点上**，只是路由不同：
///     网页控制台是主站 SPA 的 <c>/dashboard/control</c>，控制面 REST 前缀是 <c>/control/api</c>。
/// </summary>
/// <remarks>
///     <para>
///         v1 的 <c>ControlPlaneClient</c> 已随旧 <c>/v1/*</c> 契约一起删除；这些地址是**位置信息**，
///         不属于某个客户端，因此单独留在这里，配对码接入、OOBE 与设置页都还能引用。
///     </para>
///     <para>
///         旧的独立域名 <c>secrandom-control.sectl.cn</c> 降级成**遗产地址**：读到或输入到它
///         一律按"出厂默认"处理并迁移到 <see cref="ProductionBaseUrl" />。
///     </para>
/// </remarks>
public static class ControlPlaneDefaults
{
    public const string ProductionBaseUrl = "https://sectl.cn";

    public static string DefaultBaseUrl => ProductionBaseUrl;

    /// <summary>网页控制台在集控站点里的路由（v4.1 起控制台是主站 SPA 的一个页面）。</summary>
    public const string ConsolePath = "/dashboard/control";

    /// <summary>旧版集控平台的域名（遗产地址：读到即迁移到 <see cref="ProductionBaseUrl" />）。</summary>
    public const string LegacyProductionHost = "secrandom-control.sectl.cn";

    /// <summary>把基址拼成网页控制台地址；基址为空时用线上默认。</summary>
    /// <remarks>
    ///     之所以不在调用处直接打开基址：<c>https://sectl.cn</c> 是官网首页，不是控制台。
    ///     老版本把控制台部署在独立域名的根路径，所以当年"打开基址"是对的；v4.1 并入主站后必须带路由。
    /// </remarks>
    public static string ConsoleUrl(string? baseUrl)
    {
        var normalized = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
        return $"{normalized.TrimEnd('/')}{ConsolePath}";
    }

    /// <summary>
    ///     这个地址是不是旧默认域名（任意协议、任意路径都算）。
    /// </summary>
    /// <remarks>只判断主机名，不判断路径：旧平台上用户可能存过带子路径的地址，迁移时要一并收回来。</remarks>
    public static bool IsLegacyDefaultBaseUrl(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return false;

        return Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var parsed)
               && string.Equals(parsed.Host, LegacyProductionHost, StringComparison.OrdinalIgnoreCase);
    }
}