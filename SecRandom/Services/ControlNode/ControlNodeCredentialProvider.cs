using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.Auth;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     把"自建集控接入"与"SECTL 账号登录"两条路并成一条节点通道凭据。
/// </summary>
/// <remarks>
///     <para>
///         <b>接入优先</b>：本机已经用接入码接入过自建集控时，节点通道出示的是那份接入令牌，
///         与 SECTL 账号在不在、是否过期**完全无关**——这正是"不登录 SECTL 也能用自建集控"的前提。
///         它同样是节点通道自己的 Bearer 认证，与控制台浏览器的会话 cookie 无关。
///     </para>
///     <para>
///         <b>未接入时逐字回退</b>：没有接入记录（或记录读不出来）时原样走 <see cref="SectlAuthService" />，
///         官方云端的表现与本次改动之前完全一致；未登录返回 <c>null</c>，让客户端等待登录，
///         而不是拿一个空 token 去撞 <c>unauthorized</c>。
///     </para>
/// </remarks>
public sealed class ControlNodeCredentialProvider(SectlAuthService authService, INodeEnrollmentStore? enrollmentStore = null)
    : IControlNodeCredentialProvider
{
    public Task<string?> TryGetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        // 每次取凭据都重新读一遍接入状态：接入/清除接入是运行期动作，
        // 缓存下来会让"清除接入"在重启之前一直不生效。
        if (enrollmentStore?.TryGetAccessToken(out var nodeToken) == true && !string.IsNullOrWhiteSpace(nodeToken))
            return Task.FromResult<string?>(nodeToken);

        return authService.TryGetAccessTokenAsync(forceRefresh, cancellationToken);
    }
}
