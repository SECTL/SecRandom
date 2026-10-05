using SecRandom.Services.ControlPlane;
using SecRandom.Shared.Models.ControlPlane;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.Services.ControlPlane;

/// <summary>
///     把控制面的失败翻译成手机上讲得通的一句话。
/// </summary>
/// <remarks>
///     <para>
///         用户看到的不该是 <c>invalid_value:gender:not_in_list</c>，也不该是一句"未知错误"：
///         前者没人看得懂，后者等于让老师去猜。每一种已知原因都对应一句可行动的话
///         （"这台机器关闭了本机远控"、"该名单里没有符合条件的人"、"设备还没回执"）。
///     </para>
///     <para>
///         <b>未知错误码照样原样显示。</b>把它藏成"未知错误"，运维拿着截图也没法去日志里搜；
///         显示出来，至少能对上服务端的一条记录。
///     </para>
///     <para>
///         这个类不碰界面、不碰网络，只做映射，因此可以被单测逐条钉住——
///         文案错了（比如把"超时"说成"抽取失败"）只会在用户那里暴露，代码评审很难发现。
///     </para>
/// </remarks>
public static class ControlPlaneMessages
{
    /// <summary>请求本身失败（网络、鉴权、超时…）时的一句话。</summary>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            ControlPlaneException { Kind: ControlPlaneErrorKind.Unauthorized } => LR.RD_Unauthorized,
            ControlPlaneException { Kind: ControlPlaneErrorKind.Forbidden } => LR.RD_Forbidden,
            ControlPlaneException { Kind: ControlPlaneErrorKind.NotFound } => LR.RD_NotFound,
            ControlPlaneException { Kind: ControlPlaneErrorKind.Timeout } => LR.RD_CommandTimeout,
            ControlPlaneException { Kind: ControlPlaneErrorKind.Network } => LR.RD_Network,
            ControlPlaneException { Kind: ControlPlaneErrorKind.RateLimited } => LR.RD_RateLimited,
            ControlPlaneException controlPlane => string.Format(LR.RD_Error, controlPlane.Code),
            _ => string.Format(LR.RD_Error, exception.Message)
        };
    }

    /// <summary>
    ///     同一句话，但对非鉴权类失败**带上服务端错误码**。
    /// </summary>
    /// <remarks>
    ///     线上"某个请求 404"的排查里，"目标不存在"四个字把原因说没了——是域名错了、路径少了、
    ///     还是这个组真的没了？带上 <c>http_404</c> 这种原样错误码，用户截图给运维时才算一条线索。
    ///     未登录、超时与网络不可达除外：那几种情况下错误码不增加任何信息。
    /// </remarks>
    public static string DescribeWithCode(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var friendly = Describe(exception);
        if (exception is not ControlPlaneException controlPlane)
            return friendly;

        if (controlPlane.Kind is ControlPlaneErrorKind.Unauthorized
            or ControlPlaneErrorKind.Timeout
            or ControlPlaneErrorKind.Network)
            return friendly;

        return string.Format(LR.RD_FailureWithCode, friendly, controlPlane.Code);
    }

    /// <summary>命令跑完了但没成功时的一句话。</summary>
    public static string DescribeCommand(NodeCommandDto command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = command.FailureCode;
        if (string.IsNullOrWhiteSpace(code))
            return command.IsTerminal ? LR.RD_Failed : LR.RD_CommandTimeout;

        // 取值不合法的两种线上形状都要认：服务端可能把字段与细因拆在 result_detail 里，
        // 也可能整条原因码就是 invalid_value:<字段>:<为什么>（设备侧就是这么回的）。
        var field = command.FailureField;
        var why = command.FailureWhy;
        if (why is null && code.StartsWith("invalid_value:", StringComparison.Ordinal))
        {
            var parts = code["invalid_value:".Length..].Split(':', 2);
            field ??= parts.ElementAtOrDefault(0);
            why = parts.ElementAtOrDefault(1);
        }

        if (why is not null || field is not null)
            return DescribeInvalidValue(field, why);

        return code switch
        {
            "local_remote_disabled" => LR.RD_LocalRemoteDisabled,
            "draw_locked" => LR.RD_DrawLocked,
            "busy" => LR.RD_Busy,
            "capability_unsupported" => LR.RD_NoCapability,
            "expired" => LR.RD_Expired,
            "rate_limited" => LR.RD_RateLimited,
            "execution_failed" => LR.RD_Failed,
            "draw_denied" => string.Equals(command.DetailReason, "blocked_by_class_time", StringComparison.Ordinal)
                ? LR.RD_ClassTime
                : LR.RD_Denied,
            _ => string.Format(LR.RD_Error, code)
        };
    }

    private static string DescribeInvalidValue(string? field, string? why) => why switch
    {
        "not_found" => LR.RD_ListNotFound,
        "not_in_list" => LR.RD_NotInList,
        "out_of_range" => LR.RD_OutOfRange,
        "no_candidate" or "no_matching_member" => LR.RD_NoCandidate,
        _ => field is { Length: > 0 }
            ? string.Format(LR.RD_InvalidValueField, field)
            : LR.RD_InvalidValue
    };
}
