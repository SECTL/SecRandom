using SecRandom.Core.Services.ControlNode;

namespace SecRandom.Core.Tests;

/// <summary>
///     节点通道地址必须跟着集控基址的协议走：<c>http</c> 的实例配 <c>wss</c> 的通道时，
///     WebSocket 会拿明文端口去做 TLS 握手，永远连不上（现场表现：一直"集控节点连接失败"、
///     服务端永远看不到这台机器在线、远控权限也就永远报不上去）。
/// </summary>
public sealed class ControlNodeEndpointResolverTests
{
    [Fact]
    public void HttpControlPlane_DerivesPlaintextNodeChannel()
    {
        Assert.Equal(
            "ws://127.0.0.1:8792/v1/node/connect",
            ControlNodeEndpointResolver.TryDeriveFromControlPlane("http://127.0.0.1:8792"));
    }

    [Fact]
    public void HttpsControlPlane_DerivesSecureNodeChannel_KeepingHostAndPort()
    {
        Assert.Equal(
            "wss://control.example.com/v1/node/connect",
            ControlNodeEndpointResolver.TryDeriveFromControlPlane("https://control.example.com/"));

        Assert.Equal(
            "wss://control.example.com:8443/v1/node/connect",
            ControlNodeEndpointResolver.TryDeriveFromControlPlane("https://control.example.com:8443"));
    }

    [Fact]
    public void NonHttpControlPlane_CannotBeDerived()
    {
        Assert.Null(ControlNodeEndpointResolver.TryDeriveFromControlPlane("ftp://control.example.com"));
        Assert.Null(ControlNodeEndpointResolver.TryDeriveFromControlPlane("not a url"));
        Assert.Null(ControlNodeEndpointResolver.TryDeriveFromControlPlane(null));
        Assert.Null(ControlNodeEndpointResolver.TryDeriveFromControlPlane("   "));
    }

    /// <summary>线上默认：基址推导出来的正好就是出厂默认值，不该产生任何改写。</summary>
    [Fact]
    public void OfficialCloud_LeavesTheDefaultEndpointUntouched()
    {
        var resolved = ControlNodeEndpointResolver.Resolve(
            "https://secrandom-control.sectl.cn",
            ControlNodeClientOptions.DefaultEndpoint,
            out var corrected);

        Assert.False(corrected);
        Assert.Equal(ControlNodeClientOptions.DefaultEndpoint, resolved);
    }

    /// <summary>自建实例 + 从没填过节点通道（还是出厂默认）⇒ 跟着这台集控走。</summary>
    [Fact]
    public void SelfHosted_DefaultEndpointIsReplacedByTheDerivedChannel()
    {
        var resolved = ControlNodeEndpointResolver.Resolve(
            "http://127.0.0.1:8792",
            ControlNodeClientOptions.DefaultEndpoint,
            out var corrected);

        Assert.True(corrected);
        Assert.Equal("ws://127.0.0.1:8792/v1/node/connect", resolved);
    }

    /// <summary>用户那台机器的现场：http 基址存成了 wss 通道，同主机同端口 ⇒ 按基址修正。</summary>
    [Fact]
    public void SelfHosted_SameAuthorityWithWrongScheme_IsCorrected()
    {
        var resolved = ControlNodeEndpointResolver.Resolve(
            "http://127.0.0.1:8792",
            "wss://127.0.0.1:8792/v1/node/connect",
            out var corrected);

        Assert.True(corrected);
        Assert.Equal("ws://127.0.0.1:8792/v1/node/connect", resolved);
    }

    [Fact]
    public void SelfHosted_AlreadyCorrectChannel_IsNotRewritten()
    {
        var resolved = ControlNodeEndpointResolver.Resolve(
            "https://control.example.com",
            "wss://control.example.com/v1/node/connect",
            out var corrected);

        Assert.False(corrected);
        Assert.Equal("wss://control.example.com/v1/node/connect", resolved);
    }

    /// <summary>指到别的主机：用户是有意为之（比如集控在云端、节点通道自建），不覆盖。</summary>
    [Fact]
    public void SelfHosted_EndpointPointingAtAnotherHost_IsLeftAlone()
    {
        var resolved = ControlNodeEndpointResolver.Resolve(
            "http://127.0.0.1:8792",
            "wss://node.example.com/v1/node/connect",
            out var corrected);

        Assert.False(corrected);
        Assert.Equal("wss://node.example.com/v1/node/connect", resolved);
    }

    [Fact]
    public void BlankConfiguredEndpoint_FallsBackToTheDerivedChannel()
    {
        var resolved = ControlNodeEndpointResolver.Resolve("http://127.0.0.1:8792", "  ", out var corrected);

        Assert.True(corrected);
        Assert.Equal("ws://127.0.0.1:8792/v1/node/connect", resolved);
    }

    /// <summary>推导结果必须过得了节点地址校验：回环明文正是策略允许的联调地址。</summary>
    [Fact]
    public void DerivedLoopbackChannel_PassesTheEndpointPolicy()
    {
        var derived = ControlNodeEndpointResolver.TryDeriveFromControlPlane("http://127.0.0.1:8792");

        Assert.True(ControlEndpointPolicy.TryValidate(derived, out _, out var error));
        Assert.Null(error);
    }
}
