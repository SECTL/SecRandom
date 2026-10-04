using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.Auth;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     把 SECTL OAuth 会话接成节点通道凭据。
/// </summary>
/// <remarks>
///     节点通道使用服务端的 <c>SectlBearer</c> 方案（直接出示 SECTL access token），
///     与控制台浏览器的会话 cookie 无关。未登录返回 <c>null</c>，让客户端等待登录，
///     而不是拿一个空 token 去撞 <c>unauthorized</c>。
/// </remarks>
public sealed class ControlNodeCredentialProvider(SectlAuthService authService) : IControlNodeCredentialProvider
{
    public Task<string?> TryGetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken) =>
        authService.TryGetAccessTokenAsync(forceRefresh, cancellationToken);
}
