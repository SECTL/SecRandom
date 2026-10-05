using System.Net;
using System.Net.Http;
using System.Text.Json;
using SecRandom.Services.Auth;

namespace SecRandom.Services.ControlPlane;

/// <summary>
///     带 Bearer 的请求发送边界。
/// </summary>
/// <remarks>
///     <para>
///         存在的理由只有一个：<see cref="ControlPlaneClient" /> 必须走**与云备份同一条**授权通道
///         （<see cref="SectlAuthService.SendAuthorizedAsync" />：附加 Bearer、401 时刷新一次、
///         令牌被吊销时结束会话），而单测里不能真的发 HTTP。把这一层抽成一个接口，
///         生产实现是薄转发，测试实现是排好的应答队列。
///     </para>
///     <para>
///         <b>调用方不得自己缓存或刷新令牌。</b>刷新令牌是一次性的（轮换后旧的立即失效），
///         任何一处自己实现一套刷新，都会与认证服务的单飞逻辑打架，把会话打成"随机掉线"。
///     </para>
/// </remarks>
public interface IAuthorizedApiSender
{
    Task<HttpResponseMessage> SendAuthorizedAsync(
        Func<HttpRequestMessage> createRequest,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        CancellationToken cancellationToken = default);
}

/// <summary>把 <see cref="SectlAuthService" /> 适配成 <see cref="IAuthorizedApiSender" />。</summary>
internal sealed class SectlAuthorizedApiSender(SectlAuthService authService) : IAuthorizedApiSender
{
    public Task<HttpResponseMessage> SendAuthorizedAsync(
        Func<HttpRequestMessage> createRequest,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        CancellationToken cancellationToken = default) =>
        authService.SendAuthorizedAsync(createRequest, completionOption, cancellationToken);
}

/// <summary>控制面请求失败的分类。</summary>
/// <remarks>
///     分类决定手机上说哪句话（"没登录"/"你不是这个组的管理员"/"设备没回执"），
///     而 <see cref="ControlPlaneException.Code" /> 保留服务端原样的错误码：
///     **没见过的码也必须留着**，否则一个新错误码在手机上只会显示成"未知错误"，
///     运维拿着截图也没法去日志里搜。
/// </remarks>
public enum ControlPlaneErrorKind
{
    Unknown,
    Unauthorized,
    Forbidden,
    NotFound,
    InvalidRequest,
    Conflict,
    RateLimited,
    ServerError,
    Network,
    Timeout,
    InvalidResponse
}

public sealed class ControlPlaneException : Exception
{
    public ControlPlaneException(
        string code,
        ControlPlaneErrorKind kind,
        int? statusCode = null,
        JsonElement? detail = null,
        string? message = null)
        : base(message ?? code)
    {
        Code = code;
        Kind = kind;
        StatusCode = statusCode;
        Detail = detail;
    }

    public string Code { get; }

    public ControlPlaneErrorKind Kind { get; }

    public int? StatusCode { get; }

    public JsonElement? Detail { get; }

    /// <summary>把 HTTP 状态码映射成分类；未知状态码归到服务端错误而不是"未知"。</summary>
    public static ControlPlaneErrorKind ClassifyStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => ControlPlaneErrorKind.Unauthorized,
        HttpStatusCode.Forbidden => ControlPlaneErrorKind.Forbidden,
        HttpStatusCode.NotFound => ControlPlaneErrorKind.NotFound,
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => ControlPlaneErrorKind.InvalidRequest,
        HttpStatusCode.Conflict => ControlPlaneErrorKind.Conflict,
        HttpStatusCode.TooManyRequests => ControlPlaneErrorKind.RateLimited,
        _ when (int)statusCode >= 500 => ControlPlaneErrorKind.ServerError,
        _ => ControlPlaneErrorKind.Unknown
    };

    public static string FallbackCode(HttpStatusCode statusCode) => $"http_{(int)statusCode}";
}
