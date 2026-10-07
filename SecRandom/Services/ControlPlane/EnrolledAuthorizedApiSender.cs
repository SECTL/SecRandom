using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.ControlNode;

namespace SecRandom.Services.ControlPlane;

/// <summary>
///     <b>已接入</b>自建集控时，控制面请求改带**节点令牌**。
/// </summary>
/// <remarks>
///     <para>
///         这是"客户端不需要登录 SECTL 也能用自建集控"的落地点：一旦本机有接入令牌，
///         所有控制面请求的 <c>Authorization</c> 头就只放那个 <c>srn_…</c> 令牌，
///         SECTL 账号的 access token **一个字节都不再发出去**。
///         这不只是"能不能用"的问题，也是安全边界：自建服务器是别人（或学校自己）的机器，
///         把 SECTL 账号令牌发给它等于把账号交出去。
///     </para>
///     <para>
///         <b>运行期切换，不是启动期二选一。</b>用户在设置页接入或清除之后，下一个请求就走新通道，
///         不需要重启：每次请求都会重新读一次接入状态。
///     </para>
///     <para>
///         <b>节点令牌没有"刷新"这回事</b>（服务端签发的不可透明长期令牌，撤销在服务端做），
///         因此这条路径自己附加 <c>Bearer</c>，不经过 <see cref="Auth.SectlAuthService" />
///         的单飞刷新逻辑——那条逻辑会拿它去换 SECTL 的令牌，两边会互相打脸。
///     </para>
///     <para>
///         未接入时**原样转发**给 <paramref name="fallback" />：没接入的机器（也就是绝大多数
///         线上用户）一个字节的行为都不许变。
///     </para>
/// </remarks>
internal sealed class EnrolledAuthorizedApiSender(
    IAuthorizedApiSender fallback,
    INodeEnrollmentStore enrollmentStore,
    IHttpClientFactory httpClientFactory) : IAuthorizedApiSender
{
    public async Task<HttpResponseMessage> SendAuthorizedAsync(
        Func<HttpRequestMessage> createRequest,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createRequest);

        // 每次请求都重新取：接入/清除是运行期动作，缓存下来会让"清除接入"在重启前不生效。
        if (!enrollmentStore.TryGetAccessToken(out var nodeToken) || string.IsNullOrWhiteSpace(nodeToken))
        {
            // 未接入 = 今天的行为，逐字转发给 SECTL 那条通道。
            return await fallback.SendAuthorizedAsync(createRequest, completionOption, cancellationToken)
                .ConfigureAwait(false);
        }

        var request = createRequest()
            ?? throw new InvalidOperationException("The authorized request factory returned no request.");

        // 自己加头：这里**不能**再走认证服务。它的契约是"没登录就抛 InvalidOperationException"，
        // 而这条路根本不需要登录；让它插一脚就会把"已接入但没登录"变成失败。
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", nodeToken.Trim());

        // 客户端交给 IHttpClientFactory：它复用底层连接池，而"每次新建 HttpClient"会把 TCP 端口耗干。
        using var http = httpClientFactory.CreateClient();
        return await http.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
    }
}
