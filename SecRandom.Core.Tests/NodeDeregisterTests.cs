using System.Text.Json;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Core.Tests;

/// <summary>
///     <c>node.deregister</c>（自我注销）：帧与载荷形状，以及**"什么时候绝对不许发"**这条硬约束。
/// </summary>
/// <remarks>
///     <para>
///         服务端是"**登记即列出**"：离线只显示 <c>online:false</c>，只有自我注销或管理员移除才会消失。
///         因此注销只在**退出登录**与**换组**时发；退出程序、关窗口、后台驻留结束、崩溃恢复、更新重启
///         都**不许**发——在关闭路径上发注销等于"老师一关教室机，它就从控制台上消失了"，
///         而那正是用户抱怨过的现象。一句话记法：**退出登录 = 注销；退出程序 = 不注销，保持 offline 可见**。
///     </para>
///     <para>
///         这个文件里的"守卫"用例是**前瞻性**的：它现在通过，是因为代码里还没有任何注销调用；
///         哪天有人"顺手在 Stop 里补一句注销"，它会立刻变红。
///     </para>
/// </remarks>
public sealed class NodeDeregisterTests
{
    [Fact]
    public void 注销帧与确认帧的名字是协议里钉死的()
    {
        Assert.Equal("node.deregister", ControlFrameTypes.Deregister);
        Assert.Equal("node.deregister.ack", ControlFrameTypes.DeregisterAck);
    }

    [Fact]
    public void 注销载荷只有group_id且按snake_case序列化()
    {
        var payload = new ControlDeregisterRequest("group-1").ToPayload();

        Assert.Equal("group-1", payload.GetProperty("group_id").GetString());
        // 不多带字段：注销只说"我在哪个组里的登记"，别的一律不上行。
        Assert.Single(payload.EnumerateObject());
    }

    [Theory]
    [InlineData("""{ "group_id": "group-1" }""", true, "group-1")]
    [InlineData("""{ "group_id": "  group-1  " }""", true, "group-1")]
    [InlineData("""{ "group_id": "" }""", false, null)]
    [InlineData("""{ "group_id": "   " }""", false, null)]
    [InlineData("""{ "group_id": 7 }""", false, null)]
    [InlineData("""{ }""", false, null)]
    [InlineData("""[]""", false, null)]
    public void 注销载荷解析_空组或不合法都拒(string json, bool expected, string? expectedGroupId)
    {
        var element = JsonDocument.Parse(json).RootElement.Clone();

        Assert.Equal(expected, ControlDeregisterRequest.TryParse(element, out var request, out var reason));

        if (expected)
        {
            Assert.Equal(expectedGroupId, request!.GroupId);
            return;
        }

        // 理由码要能告诉服务端是哪个字段错了（与命令载荷同一套口径）。
        Assert.StartsWith("invalid_value:group_id:", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void 守卫_退出程序与宿主停止路径都不许发注销()
    {
        // 关闭路径：App.StopAsync（进程退出/重启）与节点宿主服务的 StopAsync。
        var appStop = Window(
            File.ReadAllText(GetRepositoryPath("SecRandom/App.axaml.cs")),
            "public async Task StopAsync()",
            2000);

        Assert.DoesNotContain("Deregister", appStop, StringComparison.Ordinal);
        Assert.DoesNotContain("deregister", appStop, StringComparison.Ordinal);

        var hostedStop = Window(
            File.ReadAllText(GetRepositoryPath("SecRandom/Services/ControlNode/ControlNodeHostedService.cs")),
            "public async Task StopAsync(CancellationToken",
            800);

        Assert.DoesNotContain("Deregister", hostedStop, StringComparison.Ordinal);
        Assert.DoesNotContain("deregister", hostedStop, StringComparison.Ordinal);
    }

    /// <summary>从起点标记往后再截一段源码：方法边界用锚点太脆，窗口足够覆盖方法体。</summary>
    private static string Window(string source, string start, int length)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"源码里找不到起点：{start}");

        return source[startIndex..Math.Min(source.Length, startIndex + length)];
    }

    private static string GetRepositoryPath(string relativePath) => Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")),
        relativePath);
}
