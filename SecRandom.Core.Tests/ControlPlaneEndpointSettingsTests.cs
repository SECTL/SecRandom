using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Services.ControlPlane;

namespace SecRandom.Core.Tests;

/// <summary>
///     控制面（集控 REST）基址：校验/规范化、落盘与回落。
/// </summary>
/// <remarks>
///     <para>
///         这一组守的是一条**安全边界**：这个地址曾经是账号控制台的基址，而账号控制台的每个请求
///         都会带上本账号的 SECTL access token；现在账号控制台固定打官方域名，这里只剩配对码
///         节点接入与设置页展示使用的地址，仍然不允许明文出网（只允许回环）。它不在
///         <c>settings.json</c> 里，而在 <c>data/config/control-plane/endpoint.json</c>。
///     </para>
///     <para>
///         全部落在临时目录里，一个字节都不碰真实 <c>data</c>。
///     </para>
/// </remarks>
public sealed class ControlPlaneEndpointSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "SecRandom-control-plane-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (Exception)
        {
            // 临时目录清理失败不该让测试变红。
        }
    }

    private FileControlPlaneEndpointStore CreateStore() =>
        new(NullLogger<FileControlPlaneEndpointStore>.Instance, Path.Combine(_directory, "endpoint.json"));

    // ---------------------------------------------------------------- 校验与规范化

    [Theory]
    [InlineData("https://sectl.cn", true, "https://sectl.cn")]
    [InlineData("https://sectl.cn/", true, "https://sectl.cn")]
    // 自建部署常挂在反向代理的子路径下：路径要保留，结尾多余的斜杠要去掉（否则会打出 //v1/groups）。
    [InlineData("  https://cp.example.com/panel/  ", true, "https://cp.example.com/panel")]
    [InlineData("https://cp.example.com:8443", true, "https://cp.example.com:8443")]
    // 明文只对回环放行：本机联调可以，出网不行。
    [InlineData("http://127.0.0.1:8791", true, "http://127.0.0.1:8791")]
    [InlineData("http://localhost:8791/", true, "http://localhost:8791")]
    [InlineData("http://cp.example.com", false, null)]
    [InlineData("ws://cp.example.com", false, null)]
    [InlineData("cp.example.com", false, null)]
    [InlineData("https://user:secret@cp.example.com", false, null)]
    [InlineData("https://cp.example.com/?token=abc", false, null)]
    [InlineData("https://cp.example.com/#frag", false, null)]
    [InlineData("", false, null)]
    [InlineData("   ", false, null)]
    public void 控制面地址校验与规范化(string input, bool expected, string? normalized)
    {
        var actual = ControlPlaneEndpointPolicy.TryValidate(input, out var result, out var error);

        Assert.Equal(expected, actual);
        Assert.Equal(normalized, result);
        if (expected)
            Assert.Null(error);
        else
            Assert.False(string.IsNullOrWhiteSpace(error));
    }

    // ---------------------------------------------------------------- 落盘与回落

    [Fact]
    public void 没配置过时用线上默认地址()
    {
        var store = CreateStore();

        Assert.Equal(ControlPlaneDefaults.DefaultBaseUrl, store.Current);
        Assert.False(store.IsCustom);
    }

    [Fact]
    public void 自定义地址会被落盘并被新实例读回()
    {
        var store = CreateStore();
        var raised = new List<string>();
        store.Changed += (_, url) => raised.Add(url);

        Assert.True(store.TryUpdate("https://cp.example.com/panel/", out var error), error);

        Assert.Null(error);
        Assert.Equal("https://cp.example.com/panel", store.Current);
        Assert.True(store.IsCustom);
        Assert.Equal(["https://cp.example.com/panel"], raised);

        // 新实例（= 重启后）读回来必须还是这个地址，否则"改了地址"就是假的。
        Assert.Equal("https://cp.example.com/panel", CreateStore().Current);
    }

    [Fact]
    public void 无效地址不落盘也不改内存值()
    {
        var store = CreateStore();
        Assert.True(store.TryUpdate("https://cp.example.com", out _));

        Assert.False(store.TryUpdate("http://cp.example.com", out var error));

        Assert.Equal("plaintext_endpoint_requires_loopback", error);
        Assert.Equal("https://cp.example.com", store.Current);
        Assert.Equal("https://cp.example.com", CreateStore().Current);
    }

    [Fact]
    public void 恢复默认会删掉自定义地址()
    {
        var store = CreateStore();
        Assert.True(store.TryUpdate("https://cp.example.com", out _));

        store.ResetToDefault();

        Assert.Equal(ControlPlaneDefaults.DefaultBaseUrl, store.Current);
        Assert.False(store.IsCustom);
        Assert.Equal(ControlPlaneDefaults.DefaultBaseUrl, CreateStore().Current);
    }

    [Fact]
    public void 手输默认地址等于恢复默认()
    {
        var store = CreateStore();
        Assert.True(store.TryUpdate("https://cp.example.com", out _));

        Assert.True(store.TryUpdate(ControlPlaneDefaults.DefaultBaseUrl + "/", out var error));

        Assert.Null(error);
        Assert.False(store.IsCustom);
        // 文件不会留下一条"与默认逐字相同"的自定义地址：否则"恢复默认"按钮会一直亮着。
        Assert.Equal(ControlPlaneDefaults.DefaultBaseUrl, CreateStore().Current);
    }

    [Fact]
    public void 文件里的旧默认地址会自动迁移到新默认地址()
    {
        // 老机器里留下的 endpoint.json 还写着旧域名：读到就迁移，且必须落盘，
        // 否则下次启动又读回旧地址，"迁移"只对本次运行生效。
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "endpoint.json");
        File.WriteAllText(path, """{ "format_version": 1, "base_url": "https://secrandom-control.sectl.cn" }""");

        var store = CreateStore();

        Assert.Equal(ControlPlaneDefaults.DefaultBaseUrl, store.Current);
        Assert.False(store.IsCustom);
        if (File.Exists(path))
            Assert.DoesNotContain("secrandom-control", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 手输旧默认域名等于恢复默认()
    {
        var store = CreateStore();
        Assert.True(store.TryUpdate("https://cp.example.com", out _));

        Assert.True(store.TryUpdate("https://secrandom-control.sectl.cn/", out var error));

        Assert.Null(error);
        Assert.False(store.IsCustom);
        Assert.Equal(ControlPlaneDefaults.DefaultBaseUrl, CreateStore().Current);
    }

    [Fact]
    public void 地址没变时不重复触发变化事件()
    {
        var store = CreateStore();
        Assert.True(store.TryUpdate("https://cp.example.com", out _));

        var raised = 0;
        store.Changed += (_, _) => raised++;
        Assert.True(store.TryUpdate("https://cp.example.com/", out _));

        Assert.Equal(0, raised);
    }

    [Fact]
    public void 文件损坏时回落到默认地址()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "endpoint.json"), "{ this is not json");

        var store = CreateStore();

        Assert.Equal(ControlPlaneDefaults.DefaultBaseUrl, store.Current);
        Assert.False(store.IsCustom);
    }

    [Fact]
    public void 文件里被手改成明文地址时按没配置处理()
    {
        // 文件可以被手改：读回来必须重新过一遍校验，而不是照单全收把令牌明文送出去。
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "endpoint.json"),
            """{ "format_version": 1, "base_url": "http://cp.example.com" }""");

        var store = CreateStore();

        Assert.Equal(ControlPlaneDefaults.DefaultBaseUrl, store.Current);
    }

    // 账号控制台（ControlCenterClient）固定打官方 https://sectl.cn/control/api，
    // 不再读取这个基址：JWT 不允许被自定义地址带走，所以原来的「客户端真的打这个地址」
    // 三个断言随 v1 控制面一起退出；这个存储只剩配对码节点接入与设置页展示的地址。
}
