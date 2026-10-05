using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Services.ControlPlane;
using SecRandom.Shared.Models.ControlPlane;

namespace SecRandom.Core.Tests;

/// <summary>
///     手机端控制面客户端：解析、错误码映射、轮询到终态、超时与取消。
/// </summary>
/// <remarks>
///     <para>
///         <b>全部走假发送器，一个字节都不出网。</b>控制面的服务端鉴权还在开发中，
///         单测如果打真实地址，跑一次 CI 就是一次误伤生产（而且必然失败）。
///     </para>
///     <para>
///         「401 刷新一次」在本客户端里的含义是：**刷新这件事不属于客户端**——
///         它把请求交给 <see cref="IAuthorizedApiSender" />（生产实现是
///         <c>SectlAuthService.SendAuthorizedAsync</c>，刷新令牌在那里单飞轮换），
///         因此客户端对一次调用只调用发送器一次，401 穿过来时按未授权上报而不是自己重试。
///         自己再写一套重试，会与一次性刷新令牌的轮换打架。
///     </para>
/// </remarks>
public sealed class ControlPlaneClientTests
{
    private const string GroupId = "group 1";
    private const string NodeId = "node/2";

    /// <summary>线上实测的响应体（同一账号在控制台里能看到的那个组）。</summary>
    private const string ProductionGroupsBody = """
        [{"group_id":"grp_01ab30b332e6","name":"高三教学楼","owner_user_id":"69c78f81003173a586ca","owner_display_name":"黎泽懿_Aionflux","created_at":"2026-10-04T11:45:05.014+00:00","role":"owner"}]
        """;

    // ---------------------------------------------------------------- 线上故障的回归点

    [Fact]
    public async Task 生产响应体必须解析出组id名字与角色()
    {
        // 线上"手机拿不到组"的排查，必须能在真实响应体上复现或排除。
        var sender = new FakeSender().Respond(HttpStatusCode.OK, ProductionGroupsBody);
        var client = CreateClient(sender, out _);

        var group = Assert.Single(await client.GetGroupsAsync());

        Assert.Equal("grp_01ab30b332e6", group.GroupId);
        Assert.Equal("高三教学楼", group.Name);
        Assert.Equal("owner", group.Role);
        Assert.Equal("黎泽懿_Aionflux", group.OwnerDisplayName);
    }

    [Fact]
    public async Task 生产响应里的owner必须被当成有权限的角色()
    {
        var sender = new FakeSender().Respond(HttpStatusCode.OK, ProductionGroupsBody);
        var client = CreateClient(sender, out _);

        var group = Assert.Single(await client.GetGroupsAsync());

        // 服务端返回的角色就是 owner。只认 admin/operator 的话，组所有者本人会被判成"权限不足"。
        Assert.Equal(2, group.RoleRank);
        Assert.True(group.IsAdmin);
        Assert.True(group.IsOperator);
        Assert.True(group.CanReadRoster);
        Assert.True(group.CanOperateNodes);
        Assert.False(group.IsKnownInsufficientRole);
    }

    [Theory]
    [InlineData("owner", true, true)]
    [InlineData("admin", true, true)]
    [InlineData("operator", false, true)]
    [InlineData("viewer", false, false)]
    [InlineData("readonly", false, false)]
    // 不认识的角色不拦：拦错了合法账号就用不了，越权时服务端照样会回 403。
    [InlineData("supervisor", true, true)]
    [InlineData(null, true, true)]
    public void 角色等级决定能不能读名单与下发命令(string? role, bool canReadRoster, bool canOperate)
    {
        var group = new GroupDto { GroupId = "g1", Role = role };

        Assert.Equal(canReadRoster, group.CanReadRoster);
        Assert.Equal(canOperate, group.CanOperateNodes);
    }

    [Fact]
    public async Task 请求必须打到生产控制面域名而不是账号接口()
    {
        // 线上故障就是这一条：集控控制面是**独立域名**，打到账号/云存储那套接口会拿 404，
        // 页面再把它显示成"没有组/没有设备"，于是看起来像账号不在任何组里。
        Assert.Equal("https://secrandom-control.sectl.cn", ControlPlaneClient.DefaultBaseUrl);
        Assert.DoesNotContain("appwrite", ControlPlaneClient.DefaultBaseUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost", ControlPlaneClient.DefaultBaseUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", ControlPlaneClient.DefaultBaseUrl, StringComparison.Ordinal);

        var sender = new FakeSender().Respond(HttpStatusCode.OK, "[]");
        var client = new ControlPlaneClient(sender, NullLogger<ControlPlaneClient>.Instance);
        await client.GetGroupsAsync();

        var request = Assert.Single(sender.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://secrandom-control.sectl.cn/v1/groups", request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task 按组拉节点的地址是协议约定的那一条()
    {
        var sender = new FakeSender().Respond(HttpStatusCode.OK, "[]");
        var client = new ControlPlaneClient(sender, NullLogger<ControlPlaneClient>.Instance);

        await client.GetNodesAsync("grp_01ab30b332e6");

        var request = Assert.Single(sender.Requests);
        Assert.Equal(
            "https://secrandom-control.sectl.cn/v1/groups/grp_01ab30b332e6/nodes",
            request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task 失败时异常必须带上真正打出去的地址与服务端错误码()
    {
        // 排查"某个请求 404"时，第一件要知道的就是打到了哪个 URL。
        var sender = new FakeSender().Respond(HttpStatusCode.NotFound, """{ "error": "not_found" }""");
        var client = CreateClient(sender, out _);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() => client.GetGroupsAsync());

        Assert.Equal("not_found", exception.Code);
        Assert.Equal(ControlPlaneErrorKind.NotFound, exception.Kind);
        Assert.Equal("https://control.example/v1/groups", exception.RequestUri);
    }

    [Fact]
    public async Task 未登录时归类成需要登录而不是未知错误()
    {
        // 授权边界在没有登录时抛 InvalidOperationException；不归类的话界面只会显示
        // "出错：SECTL 账号未登录。"，既不像"去登录"也不像"凭据失效"。
        var sender = new FakeSender { ThrowOnSend = new InvalidOperationException("SECTL 账号未登录。") };
        var client = CreateClient(sender, out _);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() => client.GetGroupsAsync());

        Assert.Equal("not_signed_in", exception.Code);
        Assert.Equal(ControlPlaneErrorKind.Unauthorized, exception.Kind);
    }

    // ---------------------------------------------------------------- 抽取回执（线上真实形状）

    /// <summary>设备侧 <c>command.result.detail</c> 的真实形状（点名 1 人，名单「测试 1」）。</summary>
    private const string ProductionDrawResultDetail =
        """{ "target": "roll_call", "list_name": "测试 1", "count": 1, "drawn": [ { "id": "12", "name": "学生12" } ] }""";

    [Fact]
    public async Task 真实抽取回执能解析出抽到的人()
    {
        // 线上"点名明明抽成功了、手机却说没有符合条件的人"，第一个要排除的就是回执解析。
        var sender = new FakeSender().Respond(HttpStatusCode.OK, $$"""
            { "command_id": "cmd_1", "capability": "draw.trigger", "status": "completed",
              "result_detail": {{ProductionDrawResultDetail}} }
            """);
        var client = CreateClient(sender, out _);

        var command = await client.GetCommandAsync("g1", "cmd_1");

        Assert.True(command.IsSucceeded);
        var member = Assert.Single(command.DrawnMembers());
        Assert.Equal("12", member.Id);
        Assert.Equal("学生12", member.Name);
        Assert.Equal("学生12", member.DisplayLabel);
        Assert.Equal("测试 1", command.ResultListName);
        Assert.Equal(1, command.ResultCount);
    }

    [Fact]
    public async Task 回执里缺id或姓名时该键不写也能解析()
    {
        var sender = new FakeSender().Respond(HttpStatusCode.OK, """
            { "command_id": "cmd_1", "status": "completed",
              "result_detail": { "target": "roll_call", "count": 2, "drawn": [ { "name": "学生12" }, { "id": "13" } ] } }
            """);
        var client = CreateClient(sender, out _);

        var drawn = (await client.GetCommandAsync("g1", "cmd_1")).DrawnMembers();

        Assert.Equal(2, drawn.Count);
        Assert.Null(drawn[0].Id);
        Assert.Equal("学生12", drawn[0].DisplayLabel);
        Assert.Equal("13", drawn[1].DisplayLabel);
    }

    [Fact]
    public async Task 回执被当字符串存或在别的字段里时仍然能解析()
    {
        // 服务端把 detail 落成 JSON 文本、或落在 result_payload 里，都不该让"抽到了谁"消失。
        var escaped = ProductionDrawResultDetail.Replace("\"", "\\\"");
        var asString = new FakeSender().Respond(HttpStatusCode.OK, $$"""
            { "command_id": "cmd_1", "status": "completed", "result_detail": "{{escaped}}" }
            """);
        var payloadInstead = new FakeSender().Respond(HttpStatusCode.OK, $$"""
            { "command_id": "cmd_1", "status": "completed", "result_payload": {{ProductionDrawResultDetail}} }
            """);

        var fromString = (await CreateClient(asString, out _).GetCommandAsync("g1", "cmd_1")).DrawnMembers();
        var fromPayload = (await CreateClient(payloadInstead, out _).GetCommandAsync("g1", "cmd_1")).DrawnMembers();

        Assert.Equal("学生12", Assert.Single(fromString).Name);
        Assert.Equal("学生12", Assert.Single(fromPayload).Name);
    }

    [Fact]
    public async Task 成功但没有成员的回执不会假装成没有符合条件的理由()
    {
        var sender = new FakeSender().Respond(HttpStatusCode.OK, """
            { "command_id": "cmd_1", "status": "completed", "result_detail": { "target": "roll_call", "count": 0 } }
            """);
        var client = CreateClient(sender, out _);

        var command = await client.GetCommandAsync("g1", "cmd_1");

        Assert.True(command.IsSucceeded);
        Assert.Empty(command.DrawnMembers());
        // 这是"客户端没读懂回执"，不是"没人可抽"——两种文案由 DescribeDrawResult 分开。
        Assert.Equal(
            SecRandom.Langs.Mobile.Resources.RD_ResultUnreadable,
            ControlPlaneMessages.DescribeDrawResult(command));
    }

    // ---------------------------------------------------------------- 解析

    [Fact]
    public async Task 组与节点按snake_case解析且忽略未知字段()
    {
        var sender = new FakeSender()
            .Respond(HttpStatusCode.OK, """
                                       [ { "group_id": "g1", "name": "高一", "role": "admin",
                                           "owner_user_id": "u1", "member_count": 3, "future_field": true },
                                         { "group_id": "g2", "role": "operator" } ]
                                       """);

        var client = CreateClient(sender, out _);
        var groups = await client.GetGroupsAsync();

        Assert.Equal(2, groups.Count);
        Assert.Equal("g1", groups[0].GroupId);
        Assert.Equal("高一", groups[0].DisplayLabel);
        Assert.True(groups[0].IsAdmin);
        Assert.True(groups[0].CanReadRoster);
        Assert.Equal(3, groups[0].MemberCount);

        // 缺 name 时退回 group_id：界面上不该出现空白标签。
        Assert.Equal("g2", groups[1].DisplayLabel);
        Assert.True(groups[1].IsOperator);
        Assert.False(groups[1].CanReadRoster);
    }

    [Fact]
    public async Task 节点的能力离线与锁定状态被投影成判断()
    {
        var sender = new FakeSender().Respond(HttpStatusCode.OK, """
            [ { "node_id": "n1", "platform": "windows", "version": "v3.0.0",
                "capabilities": [ "draw.trigger", "roster.read" ], "local_remote_allowed": true,
                "display_name": "高一（1）班讲台", "online": false, "draw_locked": true } ]
            """);

        var client = CreateClient(sender, out _);
        var nodes = await client.GetNodesAsync("g1");

        var node = Assert.Single(nodes);
        Assert.Equal("高一（1）班讲台", node.DisplayLabel);
        Assert.True(node.Supports("draw.trigger"));
        Assert.False(node.Supports("Draw.Trigger"));
        Assert.False(node.IsOnline);
        Assert.True(node.IsDrawLocked);
        Assert.True(node.IsLocalRemoteAllowed);
    }

    [Fact]
    public async Task 命令回执解析出抽到了谁()
    {
        var sender = new FakeSender().Respond(HttpStatusCode.OK, """
            { "command_id": "cmd_1", "capability": "draw.trigger", "status": "succeeded",
              "result_detail": { "target": "roll_call", "list_name": "高一（1）班", "count": 2,
                                 "drawn": [ { "id": "01", "name": "张三" }, { "name": "李四" } ] } }
            """);

        var client = CreateClient(sender, out _);
        var command = await client.GetCommandAsync("g1", "cmd_1");

        Assert.True(command.IsTerminal);
        Assert.True(command.IsSucceeded);
        Assert.Equal("高一（1）班", command.ResultListName);
        Assert.Equal(2, command.ResultCount);

        var drawn = command.DrawnMembers();
        Assert.Equal(2, drawn.Count);
        Assert.Equal("张三", drawn[0].DisplayLabel);
        Assert.Equal("01", drawn[0].SecondaryLabel);

        // 只有姓名时界面显示姓名，没有"次要标签"可显示。
        Assert.Equal("李四", drawn[1].DisplayLabel);
        Assert.Null(drawn[1].SecondaryLabel);
    }

    [Fact]
    public async Task 中止状态与未完成状态分别判定()
    {
        var sender = new FakeSender()
            .Respond(HttpStatusCode.OK, """{ "command_id": "c1", "status": "pending" }""")
            .Respond(HttpStatusCode.OK, """{ "command_id": "c2", "status": "expired" }""")
            .Respond(HttpStatusCode.OK, """{ "command_id": "c3", "status": "weird_new_state" }""");

        var client = CreateClient(sender, out _);

        var pending = await client.GetCommandAsync("g1", "c1");
        var expired = await client.GetCommandAsync("g1", "c2");
        var unknown = await client.GetCommandAsync("g1", "c3");

        Assert.False(pending.IsTerminal);
        Assert.True(expired.IsTerminal);
        Assert.False(expired.IsSucceeded);
        // 没见过的状态当作"还要等"：轮到终态就停，没轮到就继续等。
        Assert.False(unknown.IsTerminal);
    }

    [Fact]
    public async Task 失败命令的原因码与字段被拆出来()
    {
        var sender = new FakeSender().Respond(HttpStatusCode.OK, """
            { "command_id": "cmd_2", "status": "failed",
              "result_detail": { "reason": "invalid_value:gender:not_in_list", "field": "gender", "why": "not_in_list" } }
            """);

        var client = CreateClient(sender, out _);
        var command = await client.GetCommandAsync("g1", "cmd_2");

        Assert.True(command.IsTerminal);
        Assert.False(command.IsSucceeded);
        Assert.Equal("invalid_value:gender:not_in_list", command.FailureCode);
        Assert.Equal("gender", command.FailureField);
        Assert.Equal("not_in_list", command.FailureWhy);
    }

    // ---------------------------------------------------------------- 错误映射

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized", ControlPlaneErrorKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, "insufficient_role", ControlPlaneErrorKind.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, "node_not_found", ControlPlaneErrorKind.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited", ControlPlaneErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, "internal_error", ControlPlaneErrorKind.ServerError)]
    public async Task 服务端错误码映射成强类型错误(
        HttpStatusCode status,
        string code,
        ControlPlaneErrorKind expectedKind)
    {
        var sender = new FakeSender().Respond(status, $$"""{ "error": "{{code}}", "error_description": "说明" }""");
        var client = CreateClient(sender, out _);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() => client.GetGroupsAsync());

        Assert.Equal(code, exception.Code);
        Assert.Equal(expectedKind, exception.Kind);
        Assert.Equal((int)status, exception.StatusCode);
    }

    [Fact]
    public async Task 未知错误码同样保留而不是变成未知错误()
    {
        var sender = new FakeSender().Respond((HttpStatusCode)418, """{ "error": "teapot_mode" }""");
        var client = CreateClient(sender, out _);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() => client.GetGroupsAsync());

        Assert.Equal("teapot_mode", exception.Code);
        Assert.Equal(ControlPlaneErrorKind.Unknown, exception.Kind);
    }

    [Fact]
    public async Task 没有错误体时退回状态码本身()
    {
        var sender = new FakeSender().Respond(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "text/html");
        var client = CreateClient(sender, out _);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() => client.GetGroupsAsync());

        Assert.Equal("http_502", exception.Code);
        Assert.Equal(ControlPlaneErrorKind.ServerError, exception.Kind);
    }

    [Fact]
    public async Task 未授权只经过一次授权边界而不是客户端自己重试()
    {
        // 刷新令牌是一次性的：客户端再发一次就是拿同一个旧令牌去撞，会把会话打成随机掉线。
        var sender = new FakeSender().Respond(HttpStatusCode.Unauthorized, """{ "error": "unauthorized" }""");
        var client = CreateClient(sender, out _);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() => client.GetGroupsAsync());

        Assert.Equal(ControlPlaneErrorKind.Unauthorized, exception.Kind);
        Assert.Equal(1, sender.Calls);
    }

    [Fact]
    public async Task 成功响应读不懂时报响应异常而不是空列表()
    {
        var sender = new FakeSender().Respond(HttpStatusCode.OK, "{ not json");
        var client = CreateClient(sender, out _);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() => client.GetGroupsAsync());

        Assert.Equal(ControlPlaneErrorKind.InvalidResponse, exception.Kind);
    }

    // ---------------------------------------------------------------- 轮询

    [Fact]
    public async Task 轮询到终态立刻停止()
    {
        var sender = new FakeSender()
            .Respond(HttpStatusCode.OK, """{ "command_id": "c", "status": "pending" }""")
            .Respond(HttpStatusCode.OK, """{ "command_id": "c", "status": "running" }""")
            .Respond(HttpStatusCode.OK, """{ "command_id": "c", "status": "succeeded" }""");

        var client = CreateClient(sender, out var delays);
        var command = await client.PollCommandAsync("g1", "c");

        Assert.True(command.IsSucceeded);
        Assert.Equal(3, sender.Calls);
        // 第三次已经是终态：不该再等一轮。
        Assert.Equal(2, delays.Count);

        // 节奏必须是 1s/2s/4s（第一轮与第二轮之间不等待就发起）。
        Assert.Equal(TimeSpan.FromSeconds(1), delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(2), delays[1]);
    }

    [Fact]
    public async Task 轮询超过总预算时报超时而不是抽取失败()
    {
        var sender = new FakeSender().RespondAlways(HttpStatusCode.OK, """{ "command_id": "c", "status": "pending" }""");
        // 这里用**真实**的等待：超时判定看的是真实经过的时间，用一个不等的假等待只会空转到天荒地老。
        var client = new ControlPlaneClient(sender, NullLogger<ControlPlaneClient>.Instance, "https://control.example");

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() => client.PollCommandAsync(
            "g1",
            "c",
            new ControlPlanePollOptions([TimeSpan.FromMilliseconds(1)], TimeSpan.FromMilliseconds(20))));

        // 超时是"设备还没回执"，与"抽取失败"是两码事：手机端据此说"稍后再看"。
        Assert.Equal(ControlPlaneErrorKind.Timeout, exception.Kind);
        Assert.True(sender.Calls > 1);
    }

    [Fact]
    public async Task 轮询可以被打断()
    {
        var sender = new FakeSender().RespondAlways(HttpStatusCode.OK, """{ "command_id": "c", "status": "pending" }""");
        var client = new ControlPlaneClient(sender, NullLogger<ControlPlaneClient>.Instance, "https://control.example");

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PollCommandAsync(
            "g1",
            "c",
            new ControlPlanePollOptions([TimeSpan.FromMilliseconds(1)], TimeSpan.FromMinutes(1)),
            cancellation.Token));
    }

    // ---------------------------------------------------------------- 请求形状

    [Fact]
    public async Task 提交命令的方法地址与请求体都是协议约定的形状()
    {
        var sender = new FakeSender().Respond(HttpStatusCode.Accepted, """{ "command_id": "cmd_9", "status": "pending" }""");
        var client = CreateClient(sender, out _);

        var command = await client.SubmitCommandAsync(
            GroupId,
            NodeId,
            new ControlPlaneCommandRequest
            {
                Capability = "draw.trigger",
                Payload = JsonDocument.Parse("""{ "target": "roll_call", "count": 2 }""").RootElement.Clone()
            });

        Assert.Equal("cmd_9", command.CommandId);

        var request = Assert.Single(sender.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("control.example", request.Uri.Host);
        // id 里的空格与斜杠必须转义，否则会拼出另一条路由（大小写不敏感：Uri 自己会规范化十六进制）。
        Assert.Contains("group%201", request.Uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("node%2F2", request.Uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("/commands", request.Uri.AbsoluteUri, StringComparison.Ordinal);

        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("draw.trigger", body.RootElement.GetProperty("capability").GetString());
        Assert.Equal("action", body.RootElement.GetProperty("kind").GetString());
        Assert.Equal("roll_call", body.RootElement.GetProperty("payload").GetProperty("target").GetString());
    }

    [Fact]
    public async Task 下命令拿到非命令载荷时说清楚而不是给个空id()
    {
        // 期望状态那类响应（没有 command_id）如果被当成命令，调用方会拿着空 id 去轮询。
        var sender = new FakeSender().Respond(HttpStatusCode.OK, """{ "applied_revision": 12 }""");
        var client = CreateClient(sender, out _);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() => client.SubmitCommandAsync(
            "g1",
            "n1",
            new ControlPlaneCommandRequest { Capability = "draw.trigger" }));

        Assert.Equal(ControlPlaneErrorKind.InvalidResponse, exception.Kind);
    }

    private static ControlPlaneClient CreateClient(FakeSender sender, out List<TimeSpan> delays)
    {
        var recorded = new List<TimeSpan>();
        delays = recorded;

        return new ControlPlaneClient(
            sender,
            NullLogger<ControlPlaneClient>.Instance,
            "https://control.example/",
            (wait, _) =>
            {
                recorded.Add(wait);
                return Task.CompletedTask;
            });
    }

    private sealed record SentRequest(HttpMethod Method, Uri Uri, string Body);

    private sealed class FakeSender : IAuthorizedApiSender
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();
        private Func<HttpResponseMessage>? _always;

        public List<SentRequest> Requests { get; } = [];

        public int Calls { get; private set; }

        /// <summary>让发送器直接抛异常（模拟"未登录"这类授权边界失败）。</summary>
        public Exception? ThrowOnSend { get; init; }

        public FakeSender Respond(HttpStatusCode status, string body, string contentType = "application/json")
        {
            _responses.Enqueue(() => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType)
            });
            return this;
        }

        public FakeSender RespondAlways(HttpStatusCode status, string body)
        {
            _always = () => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return this;
        }

        public async Task<HttpResponseMessage> SendAuthorizedAsync(
            Func<HttpRequestMessage> createRequest,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
            CancellationToken cancellationToken = default)
        {
            Calls++;

            if (ThrowOnSend is { } failure)
                throw failure;

            using var request = createRequest();
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new SentRequest(request.Method, request.RequestUri!, body));

            var factory = _responses.Count > 0
                ? _responses.Dequeue()
                : _always ?? throw new InvalidOperationException("测试没有为这次调用准备响应。");

            var response = factory();
            // 生产里 HttpClient 会把请求挂到响应上，异常里的 URL 就是这么来的；假发送器要照做，
            // 否则"异常带 URL"这条只有线上才成立。
            response.RequestMessage ??= request;
            return response;
        }
    }
}
