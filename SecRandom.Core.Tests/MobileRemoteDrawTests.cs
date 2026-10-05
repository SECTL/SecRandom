using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.Auth;
using SecRandom.Services.Config;
using SecRandom.Services.ControlNode;
using SecRandom.Services.ControlPlane;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.ControlPlane;
using SecRandom.ViewModels.Mobile;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.Core.Tests;

/// <summary>
///     手机端远程抽取页：设备行的可用性判定、上次选中设备的记忆、失败文案与三语覆盖。
/// </summary>
/// <remarks>
///     页面本身要在真机/模拟器上跑，但"哪台设备能用""失败时说什么""三语有没有漏"
///     都是纯逻辑，放在这里逐条钉住——这几件事错了只会被用户看到（按钮点不动、文案空白）。
/// </remarks>
public sealed class MobileRemoteDrawTests : IDisposable
{
    private readonly string _preferencePath = Path.Combine(
        Path.GetTempPath(), "SecRandom", "control-plane-preference-tests", Guid.NewGuid().ToString("N"), "last-device.json");

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_preferencePath);
        if (directory is not null && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    // ---------------------------------------------------------------- 设备行

    [Fact]
    public void 设备行_名字缺失时回落节点id且状态可读()
    {
        var row = Row(
            new GroupDto { GroupId = "g1", Name = "高一（1）班", Role = "admin" },
            new NodeDto
            {
                NodeId = "node-7",
                DisplayName = "  ",
                Online = true,
                LocalRemoteAllowed = true,
                Capabilities = [ControlCapabilities.DrawTrigger, ControlCapabilities.RosterRead]
            });

        Assert.Equal("node-7", row.DeviceLabel);
        Assert.Equal("高一（1）班", row.GroupLabel);
        Assert.Equal(LR.RD_Online, row.StatusText);
        Assert.True(row.IsUsable);
        Assert.Null(row.UnavailableReason);
        Assert.Contains("node-7", row.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void 设备行_本机关闭远控的设备不可用且写明原因()
    {
        var row = Row(
            new GroupDto { GroupId = "g1", Name = "高一（1）班", Role = "operator" },
            new NodeDto
            {
                NodeId = "n1",
                DisplayName = "讲台机",
                Online = true,
                LocalRemoteAllowed = false,
                Capabilities = [ControlCapabilities.DrawTrigger, ControlCapabilities.RosterRead]
            });

        Assert.False(row.IsUsable);
        Assert.True(row.IsRemoteDisabled);
        Assert.Equal(LR.RD_RemoteDisabled, row.StatusText);
        Assert.Equal(LR.RD_RemoteDisabled, row.UnavailableReason);
    }

    [Theory]
    // 没有名单读取 → 选不了名单；没有抽取 → 点不了抽取。两种原因必须能区分。
    [InlineData(new[] { "draw.trigger" }, "RD_MissingRosterCapability")]
    [InlineData(new[] { "roster.read" }, "RD_MissingDrawCapability")]
    [InlineData(new string[0], "RD_MissingRosterCapability")]
    public void 设备行_能力缺失时置灰并写出缺的是哪一项(string[] capabilities, string expectedKey)
    {
        var row = Row(
            new GroupDto { GroupId = "g1", Name = "组", Role = "admin" },
            new NodeDto
            {
                NodeId = "n1",
                Online = true,
                LocalRemoteAllowed = true,
                Capabilities = capabilities
            });

        Assert.False(row.IsUsable);
        Assert.NotNull(row.UnavailableReason);
        Assert.Equal(Expected(expectedKey), row.UnavailableReason);
    }

    [Fact]
    public void 设备行_离线仍然可选但要写明上线后执行()
    {
        var row = Row(
            new GroupDto { GroupId = "g1", Name = "组", Role = "admin" },
            new NodeDto
            {
                NodeId = "n1",
                Online = false,
                LocalRemoteAllowed = true,
                Capabilities = [ControlCapabilities.DrawTrigger, ControlCapabilities.RosterRead]
            });

        // 离线不是硬拒绝：命令会在它上线后补投，因此仍然可选、只是标注清楚。
        Assert.True(row.IsUsable);
        Assert.Equal(LR.RD_OfflineQueued, row.StatusText);
        Assert.Null(row.UnavailableReason);
    }

    [Fact]
    public void 设备行_按组与节点共同定位()
    {
        var row = Row(
            new GroupDto { GroupId = "g1", Name = "组一" },
            new NodeDto { NodeId = "n1", DisplayName = "同一台机器" });

        Assert.True(row.Matches("g1", "n1"));
        // 同一台机器挂在另一个组下是另一行：匹配必须同时看组。
        Assert.False(row.Matches("g2", "n1"));
        Assert.False(row.Matches("g1", "n2"));
        Assert.False(row.Matches(null, "n1"));
    }

    // ---------------------------------------------------------------- 上次选中的设备

    [Fact]
    public void 记住设备_写入后能读回并且覆盖写不叠加()
    {
        var store = new FileControlPlaneDevicePreferenceStore(_preferencePath);

        Assert.Null(store.Load());

        store.Save(new ControlPlaneDevicePreference("g1", "n1"));
        var first = store.Load();
        Assert.NotNull(first);
        Assert.Equal("g1", first!.GroupId);
        Assert.Equal("n1", first.NodeId);

        store.Save(new ControlPlaneDevicePreference("g2", "n2"));
        var second = store.Load();
        Assert.Equal("g2", second!.GroupId);
        Assert.Equal("n2", second.NodeId);

        // 原子替换：临时文件不该留在那里。
        Assert.False(File.Exists(_preferencePath + ".tmp"));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "group_id": "", "node_id": "n1" }""")]
    [InlineData("""{ "group_id": "g1" }""")]
    [InlineData("null")]
    public void 记住设备_坏文件一律回落而不是抛异常(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_preferencePath)!);
        File.WriteAllText(_preferencePath, content);

        // 一个记不住上次选择的手机不该打不开抽取页。
        Assert.Null(new FileControlPlaneDevicePreferenceStore(_preferencePath).Load());
    }

    // ---------------------------------------------------------------- 失败文案

    [Theory]
    [InlineData(ControlPlaneErrorKind.Unauthorized)]
    [InlineData(ControlPlaneErrorKind.Forbidden)]
    [InlineData(ControlPlaneErrorKind.NotFound)]
    [InlineData(ControlPlaneErrorKind.Timeout)]
    [InlineData(ControlPlaneErrorKind.Network)]
    [InlineData(ControlPlaneErrorKind.RateLimited)]
    public void 失败文案_请求类失败都是人话且非空(ControlPlaneErrorKind kind)
    {
        var message = ControlPlaneMessages.Describe(new ControlPlaneException("code", kind));

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain("code", message, StringComparison.Ordinal);
    }

    [Fact]
    public void 失败文案_请求类失败保留未知错误码()
    {
        // 藏成"未知错误"的话，运维拿着截图没法去日志里搜。
        var message = ControlPlaneMessages.Describe(
            new ControlPlaneException("teapot_mode", ControlPlaneErrorKind.Unknown));

        Assert.Contains("teapot_mode", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("local_remote_disabled")]
    [InlineData("draw_locked")]
    [InlineData("busy")]
    [InlineData("capability_unsupported")]
    [InlineData("expired")]
    public void 失败文案_设备侧原因码逐条有对应说法(string code)
    {
        var message = ControlPlaneMessages.DescribeCommand(FailedCommand(code));

        Assert.False(string.IsNullOrWhiteSpace(message));
        // 人话 + **原始错误码**：排查时只知道"设备拒绝了"是没法定位的。
        Assert.Contains(code, message, StringComparison.Ordinal);
    }

    [Fact]
    public void 失败文案_抽取被拒时区分上课时间与需要本机验证()
    {
        var classTime = ControlPlaneMessages.DescribeCommand(
            FailedCommand("draw_denied", new { reason = "blocked_by_class_time" }));
        var verification = ControlPlaneMessages.DescribeCommand(
            FailedCommand("draw_denied", new { reason = "local_verification_required" }));

        Assert.StartsWith(LR.RD_ClassTime, classTime, StringComparison.Ordinal);
        Assert.StartsWith(LR.RD_Denied, verification, StringComparison.Ordinal);
        Assert.NotEqual(classTime, verification);
    }

    [Theory]
    // 设备侧形状：整条原因码就是 invalid_value:<字段>:<为什么>
    [InlineData("invalid_value:list_name:not_found", null, null, "RD_ListNotFound", null)]
    [InlineData("invalid_value:gender:not_in_list", null, null, "RD_NotInList", null)]
    [InlineData("invalid_value:count:out_of_range", null, null, "RD_OutOfRange", null)]
    // 名单整体没有可抽的人 ≠ 条件筛完没有人：两句文案必须不同。
    [InlineData("invalid_value:list_name:no_candidate", null, null, "RD_NoRosterCandidate", null)]
    [InlineData("invalid_value:gender:no_matching_member", null, null, "RD_NoCandidate", null)]
    // 服务端形状：码与字段分开给
    [InlineData("invalid_value", "count", "type_mismatch", "RD_InvalidValueField", "count")]
    [InlineData("invalid_value", "gender", "not_in_list", "RD_NotInList", null)]
    public void 失败文案_非法取值按字段与细因给出可行动的话(
        string code,
        string? field,
        string? why,
        string expectedKey,
        string? formatField)
    {
        var message = ControlPlaneMessages.DescribeCommand(FailedCommand(code, field, why));
        var expected = formatField is null ? Expected(expectedKey) : string.Format(Expected(expectedKey), formatField);

        Assert.StartsWith(expected, message, StringComparison.Ordinal);
        // 原始错误码一并显示。
        Assert.Contains(code, message, StringComparison.Ordinal);
    }

    [Fact]
    public void 失败文案_没有原因的失败说抽取失败而不是空白()
    {
        Assert.Equal(LR.RD_Failed, ControlPlaneMessages.DescribeCommand(FailedCommand(null)));
        Assert.Equal(
            LR.RD_CommandTimeout,
            ControlPlaneMessages.DescribeCommand(new NodeCommandDto { CommandId = "c", Status = "pending" }));
    }

    // ---------------------------------------------------------------- 接线

    [Fact]
    public void 接线_底部导航有第五个入口且页面与视图模型都注册了()
    {
        var rootView = File.ReadAllText(GetRepositoryPath("SecRandom/Views/Mobile/MobileRootView.axaml"));
        Assert.Contains("langs:Resources.N_RemoteDraw", rootView, StringComparison.Ordinal);
        // 五档底部栏：新增入口必须同时把它算进列数，否则会挤掉一个。
        Assert.Contains("Columns=\"5\"", rootView, StringComparison.Ordinal);

        var navigation = File.ReadAllText(GetRepositoryPath("SecRandom/Views/Mobile/MobileNavigation.cs"));
        Assert.Contains("main.remoteDraw", navigation, StringComparison.Ordinal);
        Assert.Contains("RemoteDraw", navigation, StringComparison.Ordinal);
        Assert.Contains("RemoteDraw =", navigation, StringComparison.Ordinal);

        var app = File.ReadAllText(GetRepositoryPath("SecRandom/App.axaml.cs"));
        Assert.Contains("MobilePageIds.RemoteDraw", app, StringComparison.Ordinal);
        Assert.Contains("MobileRemoteDrawViewModel", app, StringComparison.Ordinal);
        // 控制面客户端与 Bearer 边界必须注册在共享分支（桌面与手机同一条授权通道）。
        Assert.Contains("IControlPlaneClient", app, StringComparison.Ordinal);
        Assert.Contains("IAuthorizedApiSender", app, StringComparison.Ordinal);
    }

    [Fact]
    public void 接线_页面把设备列表选中态与空态都绑上了()
    {
        var page = File.ReadAllText(GetRepositoryPath("SecRandom/Views/Mobile/MobileRemoteDrawPage.axaml"));

        Assert.Contains("ViewModel.Devices", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.SelectedDevice", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.DeviceSummary", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.EmptyStateText", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.UnavailableGroups", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.DrawCommand", page, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- 四种状态的区分

    /// <summary>
    ///     "失败"绝不能被渲染成"空数据"。
    /// </summary>
    /// <remarks>
    ///     线上就是这一条出的问题：按组拉节点的请求拿了 404，页面却显示
    ///     「这些组里还没有设备。教室机需要用同一个账号登录，并加入本组」——
    ///     用户看到的是"没有设备"，而事实是"请求失败了"。
    /// </remarks>
    [Fact]
    public void 状态区分_请求失败时说的是失败原因而不是没有设备()
    {
        var failure = string.Format(LR.RD_FailureWithCode, LR.RD_NotFound, "http_404");

        var text = MobileRemoteDrawViewModel.ResolveEmptyState(
            isSignedIn: true,
            isLoading: false,
            hasDevices: false,
            loadFailure: failure,
            failedGroupCount: 0,
            unavailableReason: null,
            roleHint: null,
            hasRoster: false);

        Assert.Equal(failure, text);
        Assert.DoesNotContain(LR.RD_NoDevicesHint, text, StringComparison.Ordinal);
        Assert.Contains("http_404", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 状态区分_未登录时说的是未登录而不是没有设备()
    {
        var text = MobileRemoteDrawViewModel.ResolveEmptyState(
            isSignedIn: false,
            isLoading: false,
            hasDevices: false,
            loadFailure: null,
            failedGroupCount: 0,
            unavailableReason: null,
            roleHint: null,
            hasRoster: false);

        Assert.Equal(LR.RD_SignedOut, text);
    }

    [Fact]
    public void 状态区分_组读失败与组里没有设备是两件事()
    {
        // 有组读失败、且一台设备都没读到：必须说"有 N 个组没读到"。
        var failed = MobileRemoteDrawViewModel.ResolveEmptyState(
            isSignedIn: true,
            isLoading: false,
            hasDevices: false,
            loadFailure: null,
            failedGroupCount: 2,
            unavailableReason: null,
            roleHint: null,
            hasRoster: false);

        Assert.Equal(string.Format(LR.RD_GroupsUnavailable, 2), failed);
        Assert.DoesNotContain(LR.RD_NoDevicesHint, failed, StringComparison.Ordinal);

        // 真的读到 0 台设备（200 + 空数组）才是"还没有设备"。
        var empty = MobileRemoteDrawViewModel.ResolveEmptyState(
            isSignedIn: true,
            isLoading: false,
            hasDevices: false,
            loadFailure: null,
            failedGroupCount: 0,
            unavailableReason: null,
            roleHint: null,
            hasRoster: false);

        Assert.Equal(LR.RD_NoDevicesHint, empty);
    }

    [Fact]
    public void 状态区分_网络不可达有自己的说法()
    {
        var text = ControlPlaneMessages.DescribeWithCode(
            new ControlPlaneException("network_error", ControlPlaneErrorKind.Network));

        Assert.Equal(LR.RD_Network, text);
    }

    [Fact]
    public void 状态区分_失败文案带上服务端错误码但未登录不带()
    {
        var notFound = ControlPlaneMessages.DescribeWithCode(
            new ControlPlaneException("http_404", ControlPlaneErrorKind.NotFound));
        var unauthorized = ControlPlaneMessages.DescribeWithCode(
            new ControlPlaneException("not_signed_in", ControlPlaneErrorKind.Unauthorized));

        Assert.Contains("http_404", notFound, StringComparison.Ordinal);
        Assert.Equal(LR.RD_Unauthorized, unauthorized);
    }

    // ---------------------------------------------------------------- 抽取结果文案（线上回归）

    [Fact]
    public void 抽取成功时必须显示抽到谁而不是沿用上一次的失败文案()
    {
        var command = new NodeCommandDto
        {
            CommandId = "cmd_1",
            Status = "completed",
            ResultDetail = JsonDocument.Parse(
                """{ "target": "roll_call", "list_name": "测试 1", "count": 1, "drawn": [ { "id": "12", "name": "学生12" } ] }""")
                .RootElement.Clone()
        };

        var text = ControlPlaneMessages.DescribeDrawResult(command);

        Assert.Equal(string.Format(LR.RD_DrawnCount, 1), text);
        Assert.DoesNotContain(LR.RD_NoCandidate, text, StringComparison.Ordinal);
        Assert.DoesNotContain(LR.RD_Failed, text, StringComparison.Ordinal);
    }

    [Fact]
    public void 抽取成功但回执没有成员时说的是回执问题而不是没人可抽()
    {
        var command = new NodeCommandDto
        {
            CommandId = "cmd_1",
            Status = "completed",
            ResultDetail = JsonDocument.Parse("""{ "target": "roll_call", "count": 0 }""").RootElement.Clone()
        };

        var text = ControlPlaneMessages.DescribeDrawResult(command);

        // 只有设备**明确**说没人时才可以显示"没有符合条件的人"。
        Assert.Equal(LR.RD_ResultUnreadable, text);
        Assert.DoesNotContain(LR.RD_NoCandidate, text, StringComparison.Ordinal);
        Assert.DoesNotContain(LR.RD_NoRosterCandidate, text, StringComparison.Ordinal);
    }

    [Fact]
    public void 设备说的没人可抽要带上原始错误码并且两种没人分开()
    {
        var noMatching = ControlPlaneMessages.DescribeDrawResult(
            FailedCommand("invalid_value:gender:no_matching_member"));
        var noCandidate = ControlPlaneMessages.DescribeDrawResult(
            FailedCommand("invalid_value:list_name:no_candidate"));

        Assert.Contains(LR.RD_NoCandidate, noMatching, StringComparison.Ordinal);
        Assert.Contains("invalid_value:gender:no_matching_member", noMatching, StringComparison.Ordinal);

        // "名单里没有可抽的人"与"条件筛完没有人"是两件事，不能共用一句文案。
        Assert.Contains(LR.RD_NoRosterCandidate, noCandidate, StringComparison.Ordinal);
        Assert.NotEqual(LR.RD_NoCandidate, LR.RD_NoRosterCandidate);
    }

    // ---------------------------------------------------------------- 置前决策

    [Theory]
    // 隐藏 → 显示；最小化 → 还原；已可见 → 只激活。
    [InlineData(false, false, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    public void 远程抽取提示窗口要还原显示并激活(bool isVisible, bool isMinimized, bool restore, bool show)
    {
        var plan = RemoteDrawWindowPlan.Resolve(isVisible, isMinimized);

        Assert.Equal(restore, plan.Restore);
        Assert.Equal(show, plan.Show);
        Assert.True(plan.Activate);
    }

    [Fact]
    public void 接线_抽取页不再重复显示标题()
    {
        var page = File.ReadAllText(GetRepositoryPath("SecRandom/Views/Mobile/MobileRemoteDrawPage.axaml"));

        // 页面内那个粗体大标题已被删掉：标题只由导航栏出（MobileRootView.Header）。
        Assert.DoesNotContain("langs:Resources.P_RemoteDraw", page, StringComparison.Ordinal);
        // 说明行保留，并且带一点上边距，免得删掉标题后顶着导航栏。
        Assert.Contains("langs:Resources.RD_Hint", page, StringComparison.Ordinal);
        Assert.Contains("Margin=\"0 4 0 0\"", page, StringComparison.Ordinal);

        // 导航栏仍然用 P_RemoteDraw，因此这个键不能删（N_RemoteDraw 是底部入口的标签）。
        var rootView = File.ReadAllText(GetRepositoryPath("SecRandom/Views/Mobile/MobileRootView.cs"));
        Assert.Contains("LR.P_RemoteDraw", rootView, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- 三语覆盖

    [Fact]
    public void 三语覆盖_页面与视图模型用到的每个键在三种语言里都有值()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var reference in new[]
                 {
                     @"SecRandom/Views/Mobile/MobileRemoteDrawPage.axaml",
                     @"SecRandom/Views/Mobile/Settings/MobileSettingsCatalogPage.axaml",
                     @"SecRandom/ViewModels/Mobile/MobileRemoteDrawViewModel.cs",
                     @"SecRandom/ViewModels/Mobile/MobileAccountSectionViewModel.cs",
                     @"SecRandom/ViewModels/Mobile/DeviceRow.cs",
                     @"SecRandom/ViewModels/Mobile/RosterOption.cs",
                     @"SecRandom/Services/ControlPlane/ControlPlaneMessages.cs"
                 })
        {
            var text = File.ReadAllText(GetRepositoryPath(reference));
            foreach (Match match in Regex.Matches(text, @"(?:Resources|LR)\.([A-Za-z0-9_]+)"))
                keys.Add(match.Groups[1].Value);
        }

        Assert.NotEmpty(keys);

        foreach (var culture in new[] { "zh-CN", "en-US", "ja-JP" })
        {
            var info = new CultureInfo(culture);
            foreach (var key in keys)
            {
                // 缺一个键，界面上就是一处空白（或退回中文），而这是只有用户会看到的问题。
                Assert.False(
                    string.IsNullOrWhiteSpace(LR.ResourceManager.GetString(key, info)),
                    $"移动端抽取页缺少 {culture} 的文案：{key}");
            }
        }
    }

    private static string Expected(string key) => key switch
    {
        "RD_MissingRosterCapability" => LR.RD_MissingRosterCapability,
        "RD_MissingDrawCapability" => LR.RD_MissingDrawCapability,
        "RD_ListNotFound" => LR.RD_ListNotFound,
        "RD_NotInList" => LR.RD_NotInList,
        "RD_OutOfRange" => LR.RD_OutOfRange,
        "RD_NoCandidate" => LR.RD_NoCandidate,
        "RD_NoRosterCandidate" => LR.RD_NoRosterCandidate,
        "RD_InvalidValueField" => LR.RD_InvalidValueField,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
    };

    private static DeviceRow Row(GroupDto group, NodeDto node) => new(group, node);

    /// <summary>一条失败的命令回执：<paramref name="error" /> 是控制面记录的失败原因码。</summary>
    /// <param name="error">失败原因码。</param>
    /// <param name="field">取值类失败时的字段名；给了就会带上对应细因。</param>
    /// <param name="why">取值类失败时的细因。</param>
    private static NodeCommandDto FailedCommand(string? error, string? field = null, string? why = null)
    {
        Dictionary<string, object?>? detail = null;
        if (field is not null || why is not null)
        {
            detail = [];
            if (field is not null)
                detail["field"] = field;
            if (why is not null)
                detail["why"] = why;
        }

        return new NodeCommandDto
        {
            CommandId = "cmd_1",
            Status = "failed",
            Error = error,
            ResultDetail = detail is null
                ? null
                : JsonSerializer.SerializeToElement(detail, ControlProtocolJson.Options)
        };
    }

    /// <summary>同上，但详情直接给一个对象（例如 <c>new { reason = "blocked_by_class_time" }</c>）。</summary>
    private static NodeCommandDto FailedCommand(string? error, object detail) => new()
    {
        CommandId = "cmd_1",
        Status = "failed",
        Error = error,
        ResultDetail = JsonSerializer.SerializeToElement(detail, ControlProtocolJson.Options)
    };

    // ---------------------------------------------------------------- 抽取类型切换（点名 / 抽奖）

    [Fact]
    public void 切换类型_抽奖读的是奖池并且没有性别分组()
    {
        var (viewModel, client) = CreateViewModel();

        Assert.False(viewModel.IsLotteryTarget);
        Assert.Equal(LR.RD_List, viewModel.ListFieldLabel);

        viewModel.SelectedDrawKind = LotteryKind;
        client.Responder = request => request.Capability == ControlCapabilities.RosterRead
            ? RosterCommand(PrizesResponse("元旦抽奖"))
            : Completed();

        viewModel.LoadRosterCommand.Execute(null);

        // 数据源换了：点名叫 roster.read 读 students，抽奖要读 prizes。
        Assert.Equal(ControlCapabilities.RosterRead, client.LastSubmission.Capability);
        Assert.Equal(
            ControlRosterReadRequest.Prizes,
            client.LastSubmission.Payload!.Value.GetProperty("roster_kind").GetString());

        Assert.True(viewModel.IsLotteryTarget);
        Assert.Equal(LR.RD_Pool, viewModel.ListFieldLabel);
        // 抽奖没有性别/分组：那两块整块隐藏，并且有一句解释。
        Assert.False(viewModel.IsScopeSelectorVisible);
        Assert.True(viewModel.IsLotteryScopeHintVisible);
    }

    [Fact]
    public void 切换类型_切回点名时恢复名单与条件()
    {
        var (viewModel, client) = CreateViewModel();
        viewModel.SelectedDrawKind = LotteryKind;
        client.Responder = _ => RosterCommand(PrizesResponse("元旦抽奖"));
        viewModel.LoadRosterCommand.Execute(null);
        Assert.True(viewModel.HasRoster);

        client.Responder = _ => RosterCommand(StudentsResponse("高一（1）班"));
        viewModel.SelectedDrawKind = RollCallKind;
        // 换类型＝换一批数据：上一类的名单不能留着（否则发出去的是另一批文件里的名字）。
        Assert.False(viewModel.IsLotteryTarget);
        Assert.Equal(LR.RD_List, viewModel.ListFieldLabel);
    }

    [Fact]
    public void 抽取_抽奖下发lottery且不带性别分组()
    {
        var (viewModel, client) = CreateViewModel();
        viewModel.SelectedDrawKind = LotteryKind;
        client.Responder = request => request.Capability switch
        {
            ControlCapabilities.RosterRead => RosterCommand(PrizesResponse("元旦抽奖")),
            _ => DrawnCommand("lottery", "元旦抽奖", 2)
        };

        viewModel.LoadRosterCommand.Execute(null);
        viewModel.SelectedCount = 2;
        viewModel.DrawCommand.Execute(null);

        Assert.Equal(ControlCapabilities.DrawTrigger, client.LastSubmission.Capability);
        var payload = client.LastSubmission.Payload!.Value;
        Assert.Equal("lottery", payload.GetProperty("target").GetString());
        Assert.Equal("元旦抽奖", payload.GetProperty("list_name").GetString());
        Assert.Equal(2, payload.GetProperty("count").GetInt32());

        // 设备侧对抽奖载荷里的 gender/group 是"存在即拒绝"，所以这里必须**根本不发**这两个键。
        Assert.False(payload.TryGetProperty("gender", out _));
        Assert.False(payload.TryGetProperty("group", out _));

        Assert.Equal(2, viewModel.DrawnMembers.Count);
        Assert.Equal(string.Format(LR.RD_DrawnPrizeCount, 2), viewModel.StatusText);
    }

    [Fact]
    public void 抽取_点名仍然带性别与分组且用roll_call目标()
    {
        var (viewModel, client) = CreateViewModel();
        client.Responder = request => request.Capability switch
        {
            ControlCapabilities.RosterRead => RosterCommand(StudentsResponse("高一（1）班")),
            _ => DrawnCommand("roll_call", "高一（1）班", 2)
        };

        viewModel.LoadRosterCommand.Execute(null);
        viewModel.SelectedGender = "男";
        viewModel.DrawCommand.Execute(null);

        var payload = client.LastSubmission.Payload!.Value;
        Assert.Equal("roll_call", payload.GetProperty("target").GetString());
        Assert.Equal("男", payload.GetProperty("gender").GetString());
        Assert.Equal(LR.RD_Any, viewModel.SelectedGroupScope);
        // "不限"不下发：协议里"没有这个字段"就是不限，发一个"不限"只会让设备去名单里找一个叫"不限"的分组。
        Assert.Equal(string.Format(LR.RD_DrawnCount, 2), viewModel.StatusText);
    }

    // ---------------------------------------------------------------- 重置本轮

    [Fact]
    public void 重置_要按两次才真正下发且第二次才带目标与名单()
    {
        var (viewModel, client) = CreateViewModel();
        client.Responder = request => request.Capability switch
        {
            ControlCapabilities.RosterRead => RosterCommand(StudentsResponse("高一（1）班")),
            ControlCapabilities.DrawReset => ResetCommand(3),
            _ => DrawnCommand("roll_call", "高一（1）班", 1)
        };

        viewModel.LoadRosterCommand.Execute(null);
        viewModel.DrawCommand.Execute(null);
        Assert.True(viewModel.HasResult);

        viewModel.ResetRoundCommand.Execute(null);
        // 第一次点击只是把它变成"再点一次确认"：破坏性动作不该一次误触就生效。
        Assert.True(viewModel.IsResetConfirmPending);
        Assert.Equal(LR.RD_ResetConfirm, viewModel.ResetButtonText);
        Assert.Equal(ControlCapabilities.DrawTrigger, client.LastSubmission.Capability);

        viewModel.ResetRoundCommand.Execute(null);

        Assert.False(viewModel.IsResetConfirmPending);
        Assert.Equal(LR.RD_Reset, viewModel.ResetButtonText);
        var reset = client.LastSubmissionOf(ControlCapabilities.DrawReset);
        var payload = reset.Payload!.Value;
        Assert.Equal("roll_call", payload.GetProperty("target").GetString());
        Assert.Equal("高一（1）班", payload.GetProperty("list_name").GetString());

        // 成功之后：清掉"抽到了谁"，并用回执里的 cleared 条数显示结果。
        Assert.False(viewModel.HasResult);
        Assert.Equal(string.Format(LR.RD_ResetDone, 3), viewModel.StatusText);
        // 重置之后要重读名单：设备已经把这一轮的进度清零了，页面上的计数得跟着刷新。
        Assert.Equal(ControlCapabilities.RosterRead, client.LastSubmission.Capability);
    }

    [Fact]
    public void 重置_抽奖时清的是奖品进度而不是点名进度()
    {
        var (viewModel, client) = CreateViewModel();
        viewModel.SelectedDrawKind = LotteryKind;
        client.Responder = request => request.Capability switch
        {
            ControlCapabilities.RosterRead => RosterCommand(PrizesResponse("元旦抽奖")),
            ControlCapabilities.DrawReset => ResetCommand(0),
            _ => DrawnCommand("lottery", "元旦抽奖", 1)
        };

        viewModel.LoadRosterCommand.Execute(null);
        viewModel.ResetRoundCommand.Execute(null);
        viewModel.ResetRoundCommand.Execute(null);

        var payload = client.LastSubmissionOf(ControlCapabilities.DrawReset).Payload!.Value;
        Assert.Equal("lottery", payload.GetProperty("target").GetString());
        Assert.Equal("元旦抽奖", payload.GetProperty("list_name").GetString());
        Assert.Equal(string.Format(LR.RD_ResetDone, 0), viewModel.StatusText);
    }

    [Fact]
    public void 重置_别的动作会取消待确认状态()
    {
        var (viewModel, client) = CreateViewModel();
        client.Responder = _ => RosterCommand(StudentsResponse("高一（1）班"));
        viewModel.LoadRosterCommand.Execute(null);

        viewModel.ResetRoundCommand.Execute(null);
        Assert.True(viewModel.IsResetConfirmPending);

        // 用户改主意去了别的名单：几秒钟前那个"确认"代表的意图已经不成立了。
        viewModel.SelectedDrawKind = LotteryKind;

        Assert.False(viewModel.IsResetConfirmPending);
        Assert.Equal(LR.RD_Reset, viewModel.ResetButtonText);
    }

    [Fact]
    public void 重置_设备侧失败时说失败原因而不是重置成功()
    {
        var (viewModel, client) = CreateViewModel();
        client.Responder = request => request.Capability switch
        {
            ControlCapabilities.RosterRead => RosterCommand(StudentsResponse("高一（1）班")),
            _ => FailedCommand("busy")
        };

        viewModel.LoadRosterCommand.Execute(null);
        viewModel.ResetRoundCommand.Execute(null);
        viewModel.ResetRoundCommand.Execute(null);

        Assert.Contains("busy", viewModel.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(LR.RD_ResetDone.Split('{')[0], viewModel.StatusText, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- 测试替身

    private static readonly RemoteDrawKindOption LotteryKind = RemoteDrawKindOption.CreateLottery();

    private static readonly RemoteDrawKindOption RollCallKind = RemoteDrawKindOption.CreateRollCall();

    private (MobileRemoteDrawViewModel ViewModel, RecordingControlPlaneClient Client) CreateViewModel()
    {
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(new MainConfigModel()));
        var deviceUuidStore = new DeviceUuidStore(configHandler, NullLogger<DeviceUuidStore>.Instance);
        var auth = new SectlAuthService(
            TestTokenStore.Create(),
            new StubHttpClientFactory(new HttpClient()),
            deviceUuidStore,
            NullLogger<SectlAuthService>.Instance,
            new LoopbackAuthRedirectBrokerFactory());

        var client = new RecordingControlPlaneClient();
        var viewModel = new MobileRemoteDrawViewModel(
            configHandler,
            client,
            new FileControlPlaneDevicePreferenceStore(_preferencePath),
            auth,
            NullLogger<MobileRemoteDrawViewModel>.Instance)
        {
            // 设备列表平时是网络拉回来的，这里直接选中一台可用设备：这些断言关心的是选中之后的行为。
            SelectedDevice = new DeviceRow(
                new GroupDto { GroupId = "g1", Name = "高一（1）班", Role = "admin" },
                new NodeDto
                {
                    NodeId = "n1",
                    DisplayName = "讲台机",
                    Online = true,
                    LocalRemoteAllowed = true,
                    Capabilities = [ControlCapabilities.DrawTrigger, ControlCapabilities.RosterRead]
                })
        };

        return (viewModel, client);
    }

    private static NodeCommandDto RosterCommand(ControlRosterReadResponse response) => new()
    {
        CommandId = "cmd_roster",
        Status = "completed",
        ResultPayload = JsonSerializer.SerializeToElement(response, ControlProtocolJson.Options)
    };

    private static NodeCommandDto DrawnCommand(string target, string listName, int count) => new()
    {
        CommandId = "cmd_draw",
        Status = "completed",
        ResultDetail = JsonDocument.Parse(
            $$"""
              { "target": "{{target}}", "list_name": "{{listName}}", "count": {{count}},
                "drawn": [ { "id": "01", "name": "张三" }, { "id": "02", "name": "李四" } ] }
              """).RootElement.Clone()
    };

    private static NodeCommandDto ResetCommand(int cleared) => new()
    {
        CommandId = "cmd_reset",
        Status = "completed",
        ResultDetail = JsonDocument.Parse(
            $$"""{ "target": "roll_call", "list_name": "高一（1）班", "cleared": {{cleared}} }""")
            .RootElement.Clone()
    };

    private static ControlRosterReadResponse StudentsResponse(string name) => new(
        ControlRosterReadRequest.Students,
        [
            new ControlRosterListPayload(
                name,
                true,
                2,
                2,
                false,
                [
                    new ControlRosterMemberPayload("01", "张三", "男", "A组", null, null, true),
                    new ControlRosterMemberPayload("02", "李四", "女", "B组", null, null, true)
                ])
        ]);

    private static ControlRosterReadResponse PrizesResponse(string name) => new(
        ControlRosterReadRequest.Prizes,
        [
            new ControlRosterListPayload(
                name,
                true,
                2,
                2,
                false,
                [
                    new ControlRosterMemberPayload("P01", "一等奖", null, null, 1, 1, true),
                    new ControlRosterMemberPayload("P02", "二等奖", null, null, 3, 1, true)
                ])
        ]);

    /// <summary>按能力回一条固定回执的控制面客户端；同时记下最后一次下发的命令本体。</summary>
    private sealed class RecordingControlPlaneClient : IControlPlaneClient
    {
        public List<ControlPlaneCommandRequest> Submissions { get; } = [];

        public Func<ControlPlaneCommandRequest, NodeCommandDto> Responder { get; set; } = _ => Completed();

        public ControlPlaneCommandRequest LastSubmission => Submissions[^1];

        /// <summary>某条能力最后一次下发的载荷。</summary>
        /// <remarks>
        ///     重置成功之后还会再读一次名单，因此"最后一次下发"未必是测试关心的那条命令。
        /// </remarks>
        public ControlPlaneCommandRequest LastSubmissionOf(string capability) =>
            Submissions.Last(request => string.Equals(request.Capability, capability, StringComparison.Ordinal));

        public Task<IReadOnlyList<GroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GroupDto>>([]);

        public Task<IReadOnlyList<NodeDto>> GetNodesAsync(string groupId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeDto>>([]);

        public Task<NodeCommandDto> SubmitCommandAsync(
            string groupId,
            string nodeId,
            ControlPlaneCommandRequest request,
            CancellationToken cancellationToken = default)
        {
            Submissions.Add(request);
            return Task.FromResult(new NodeCommandDto { CommandId = $"cmd_{Submissions.Count}", Status = "pending" });
        }

        public Task<NodeCommandDto> GetCommandAsync(
            string groupId,
            string commandId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Responder(LastSubmission));

        public Task<NodeCommandDto> PollCommandAsync(
            string groupId,
            string commandId,
            ControlPlanePollOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Responder(LastSubmission));
    }

    private static NodeCommandDto Completed() => new() { CommandId = "cmd", Status = "completed" };

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;

        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;

        public override void SaveConfig<T>(T value)
        {
        }

        public override void DeleteConfig<T>(T value)
        {
        }
    }

    private static string GetRepositoryPath(string relativePath) => Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")),
        relativePath);
}
