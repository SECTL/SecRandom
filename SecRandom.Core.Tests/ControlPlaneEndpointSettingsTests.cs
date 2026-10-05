using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Services.ControlPlane;

namespace SecRandom.Core.Tests;

/// <summary>
///     控制面（集控 REST）基址：校验/规范化、落盘与回落、客户端实际打出去的地址，以及调试页那个总开关。
/// </summary>
/// <remarks>
///     <para>
///         这一组守的是一条**安全边界**：控制面地址每次请求都会带上本账号的 SECTL access token，
///         因此它既不能明文出网（只允许回环），也不能因为一次设置导入而被改掉——它不在
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
    [InlineData("https://secrandom-control.sectl.cn", true, "https://secrandom-control.sectl.cn")]
    [InlineData("https://secrandom-control.sectl.cn/", true, "https://secrandom-control.sectl.cn")]
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

        Assert.Equal(ControlPlaneClient.DefaultBaseUrl, store.Current);
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

        Assert.Equal(ControlPlaneClient.DefaultBaseUrl, store.Current);
        Assert.False(store.IsCustom);
        Assert.Equal(ControlPlaneClient.DefaultBaseUrl, CreateStore().Current);
    }

    [Fact]
    public void 手输默认地址等于恢复默认()
    {
        var store = CreateStore();
        Assert.True(store.TryUpdate("https://cp.example.com", out _));

        Assert.True(store.TryUpdate(ControlPlaneClient.DefaultBaseUrl + "/", out var error));

        Assert.Null(error);
        Assert.False(store.IsCustom);
        // 文件不会留下一条"与默认逐字相同"的自定义地址：否则"恢复默认"按钮会一直亮着。
        Assert.Equal(ControlPlaneClient.DefaultBaseUrl, CreateStore().Current);
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

        Assert.Equal(ControlPlaneClient.DefaultBaseUrl, store.Current);
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

        Assert.Equal(ControlPlaneClient.DefaultBaseUrl, store.Current);
    }

    // ---------------------------------------------------------------- 客户端真的打这个地址

    [Fact]
    public async Task 配置过的地址会用在请求上()
    {
        var store = CreateStore();
        Assert.True(store.TryUpdate("https://cp.example.com/panel", out _));

        var sender = new RecordingSender();
        var client = new ControlPlaneClient(
            sender,
            NullLogger<ControlPlaneClient>.Instance,
            endpointStore: store);

        await client.GetGroupsAsync();

        Assert.Equal("https://cp.example.com/panel/v1/groups", Assert.Single(sender.Requests).AbsoluteUri);
    }

    [Fact]
    public async Task 没配置过时仍然打生产控制面域名()
    {
        var sender = new RecordingSender();
        var client = new ControlPlaneClient(
            sender,
            NullLogger<ControlPlaneClient>.Instance,
            endpointStore: CreateStore());

        await client.GetGroupsAsync();

        Assert.Equal("https://secrandom-control.sectl.cn/v1/groups", Assert.Single(sender.Requests).AbsoluteUri);
    }

    [Fact]
    public async Task 地址改完之后不需要重建客户端()
    {
        // 地址是可以在设置里改的；改完还要重启才生效，等于这个设置是假的。
        var store = CreateStore();
        var sender = new RecordingSender();
        var client = new ControlPlaneClient(
            sender,
            NullLogger<ControlPlaneClient>.Instance,
            endpointStore: store);

        Assert.True(store.TryUpdate("https://cp.example.com", out _));
        await client.GetGroupsAsync();

        Assert.Equal("https://cp.example.com/v1/groups", Assert.Single(sender.Requests).AbsoluteUri);
    }

    // ---------------------------------------------------------------- 调试页那个总开关

    [Fact]
    public void 总开关默认关闭且只在真正变化时通知()
    {
        var gate = new ControlPlaneEndpointSettingsGate();
        Assert.False(gate.IsRevealed);

        var raised = 0;
        gate.Changed += (_, _) => raised++;

        gate.SetRevealed(true);
        Assert.True(gate.IsRevealed);
        Assert.Equal(1, raised);

        // 重复设置同一个值不该再通知（订阅方每次都会刷新界面）。
        gate.SetRevealed(true);
        Assert.Equal(1, raised);

        gate.SetRevealed(false);
        Assert.False(gate.IsRevealed);
        Assert.Equal(2, raised);
    }

    private sealed class RecordingSender : IAuthorizedApiSender
    {
        public List<Uri> Requests { get; } = [];

        public Task<HttpResponseMessage> SendAuthorizedAsync(
            Func<HttpRequestMessage> createRequest,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
            CancellationToken cancellationToken = default)
        {
            using var request = createRequest();
            Requests.Add(request.RequestUri!);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        }
    }
}
