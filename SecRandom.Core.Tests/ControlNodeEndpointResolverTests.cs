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
            "ws://127.0.0.1:8792/control/ws",
            ControlNodeEndpointResolver.TryDeriveFromControlPlane("http://127.0.0.1:8792"));
    }

    [Fact]
    public void HttpsControlPlane_DerivesSecureNodeChannel_KeepingHostAndPort()
    {
        Assert.Equal(
            "wss://control.example.com/control/ws",
            ControlNodeEndpointResolver.TryDeriveFromControlPlane("https://control.example.com/"));

        Assert.Equal(
            "wss://control.example.com:8443/control/ws",
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
    /// <remarks>
    /// v4 起集控并入主站，出厂默认是 <c>wss://sectl.cn/control/ws</c>；
    /// 旧的 <c>secrandom-control.sectl.cn</c> 基址推导出来的是另一个地址，会被「修正」——
    /// 那是预期的（老用户填旧基址 ⇒ 自动跟到新通道）。
    /// </remarks>
    [Fact]
    public void OfficialCloud_LeavesTheDefaultEndpointUntouched()
    {
        var resolved = ControlNodeEndpointResolver.Resolve(
            "https://sectl.cn",
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
        Assert.Equal("ws://127.0.0.1:8792/control/ws", resolved);
    }

    /// <summary>用户那台机器的现场：http 基址存成了 wss 通道，同主机同端口 ⇒ 按基址修正。</summary>
    [Fact]
    public void SelfHosted_SameAuthorityWithWrongScheme_IsCorrected()
    {
        var resolved = ControlNodeEndpointResolver.Resolve(
            "http://127.0.0.1:8792",
            "wss://127.0.0.1:8792/control/ws",
            out var corrected);

        Assert.True(corrected);
        Assert.Equal("ws://127.0.0.1:8792/control/ws", resolved);
    }

    [Fact]
    public void SelfHosted_AlreadyCorrectChannel_IsNotRewritten()
    {
        var resolved = ControlNodeEndpointResolver.Resolve(
            "https://control.example.com",
            "wss://control.example.com/control/ws",
            out var corrected);

        Assert.False(corrected);
        Assert.Equal("wss://control.example.com/control/ws", resolved);
    }

    /// <summary>指到别的主机：用户是有意为之（比如集控在云端、节点通道自建），不覆盖。</summary>
    [Fact]
    public void SelfHosted_EndpointPointingAtAnotherHost_IsLeftAlone()
    {
        var resolved = ControlNodeEndpointResolver.Resolve(
            "http://127.0.0.1:8792",
            "wss://node.example.com/control/ws",
            out var corrected);

        Assert.False(corrected);
        Assert.Equal("wss://node.example.com/control/ws", resolved);
    }

    [Fact]
    public void BlankConfiguredEndpoint_FallsBackToTheDerivedChannel()
    {
        var resolved = ControlNodeEndpointResolver.Resolve("http://127.0.0.1:8792", "  ", out var corrected);

        Assert.True(corrected);
        Assert.Equal("ws://127.0.0.1:8792/control/ws", resolved);
    }

    /// <summary>推导结果必须过得了节点地址校验：回环明文正是策略允许的联调地址。</summary>
    [Fact]
    public void DerivedLoopbackChannel_PassesTheEndpointPolicy()
    {
        var derived = ControlNodeEndpointResolver.TryDeriveFromControlPlane("http://127.0.0.1:8792");

        Assert.True(ControlEndpointPolicy.TryValidate(derived, out _, out var error));
        Assert.Null(error);
    }

    /// <summary>旧版节点通道（v1 的 /v1/node/connect）在新集控上不存在 ⇒ 迁到新出厂默认。</summary>
    [Fact]
    public void LegacyNodeChannel_IsMigratedToTheNewDefault()
    {
        var resolved = ControlNodeEndpointResolver.Resolve(
            "https://sectl.cn",
            "wss://secrandom-control.sectl.cn/v1/node/connect",
            out var corrected);

        Assert.True(corrected);
        Assert.Equal(ControlNodeClientOptions.DefaultEndpoint, resolved);
    }

    /// <summary>旧地址但控制面基址也丢了（手工清过 base）⇒ 仍然迁到新默认，不是留在死地址上。</summary>
    [Fact]
    public void LegacyNodeChannel_WithoutControlPlaneBase_StillMigratesToTheNewDefault()
    {
        var resolved = ControlNodeEndpointResolver.Resolve(
            null,
            "wss://secrandom-control.sectl.cn/v1/node/connect",
            out var corrected);

        Assert.True(corrected);
        Assert.Equal(ControlNodeClientOptions.DefaultEndpoint, resolved);
    }

    /// <summary>旧版域名也要能被识别：只认主机名，协议与路径不该影响判断。</summary>
    [Theory]
    [InlineData("wss://secrandom-control.sectl.cn/v1/node/connect", true)]
    [InlineData("wss://SECRANDOM-control.sectl.cn/v1/node/connect", true)]
    [InlineData("https://secrandom-control.sectl.cn/", true)]
    [InlineData("wss://sectl.cn/control/ws", false)]
    [InlineData("wss://node.example.com/control/ws", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void IsLegacyNodeChannel_OnlyLooksAtTheHost(string? endpoint, bool expected)
    {
        Assert.Equal(expected, ControlNodeEndpointResolver.IsLegacyNodeChannel(endpoint));
    }
}
