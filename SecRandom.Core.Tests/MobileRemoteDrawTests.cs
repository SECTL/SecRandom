using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core;
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

    // ---------------------------------------------------------------- 本机标识与结果空态

    [Theory]
    [InlineData("node-1", "node-1", true)]
    [InlineData("node-1", "NODE-1", true)]     // 大小写不同不是两台机器
    [InlineData(" node-1 ", "node-1", true)]   // 首尾空白同理
    [InlineData("node-1", "node-2", false)]
    [InlineData("", "node-1", false)]          // 本机身份没配置：一台都不标
    [InlineData("   ", "node-1", false)]
    [InlineData("node-1", "", false)]
    [InlineData("node-1", null, false)]
    [InlineData(null, null, false)]
    public void 本机标识_按nodeid比对且容忍大小写与空白(string? ownNodeId, string? candidateNodeId, bool expected) =>
        Assert.Equal(expected, DeviceRow.IsSameNode(ownNodeId, candidateNodeId));

    [Fact]
    public void 本机标识_只有本机那一行带徽章文案()
    {
        var self = Row("n1", isSelf: true);
        var other = Row("n2");

        // 本机**留在列表里**（只是标出来）：藏起来会让人以为设备没连上，也没法给它下发命令。
        Assert.True(self.IsSelf);
        Assert.Equal(LR.RD_SelfBadge, self.SelfBadgeText);

        Assert.False(other.IsSelf);
        Assert.Equal(string.Empty, other.SelfBadgeText);
    }

    [Fact]
    public void 本机提示_只在选中本机时出现()
    {
        var (viewModel, _) = CreateViewModel();

        viewModel.SelectedDevice = Row("n2");
        Assert.False(viewModel.IsSelfSelected);

        viewModel.SelectedDevice = Row("n1", isSelf: true);
        Assert.True(viewModel.IsSelfSelected);

        // 提示文案来自共享 VM，两个视图都绑它。
        Assert.False(string.IsNullOrWhiteSpace(MobileRemoteDrawViewModel.SelfSelectedHint));
    }

    [Fact]
    public void 结果空态_两行文案非空且出现回执后不再显示()
    {
        var (viewModel, _) = CreateViewModel();

        Assert.False(string.IsNullOrWhiteSpace(MobileRemoteDrawViewModel.ResultPlaceholderTitle));
        Assert.False(string.IsNullOrWhiteSpace(MobileRemoteDrawViewModel.ResultPlaceholderHint));
        Assert.False(viewModel.HasResult);

        viewModel.DrawnMembers.Add(new NodeCommandDrawnMember("01", "张三"));

        Assert.True(viewModel.HasResult);
        Assert.Contains("1", viewModel.DrawnCountText, StringComparison.Ordinal);
    }

    private static DeviceRow Row(string nodeId, bool isSelf = false, bool supportsConditions = false) => new(
        new GroupDto { GroupId = "g1", Name = "高一（1）班", Role = "admin" },
        new NodeDto
        {
            NodeId = nodeId,
            DisplayName = nodeId,
            Online = true,
            LocalRemoteAllowed = true,
            Capabilities = supportsConditions
                ? [ControlCapabilities.DrawTrigger, ControlCapabilities.RosterRead, ControlCapabilities.DrawTriggerConditions]
                : [ControlCapabilities.DrawTrigger, ControlCapabilities.RosterRead]
        },
        isSelf);

    // ---------------------------------------------------------------- 设备加载态（"已读到却仍显示正在读取设备"的回归）

    [Fact]
    public async Task 加载态_成功后清掉并且不再显示加载文案()
    {
        var (viewModel, client) = await CreateSignedInViewModelAsync();
        client.GroupsAsync = () => Task.FromResult<IReadOnlyList<GroupDto>>([Group("g1")]);
        client.NodesAsync = _ => Task.FromResult<IReadOnlyList<NodeDto>>([Node("n1")]);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        // 这三条一起才说明"读完了"：布尔清掉、文案不再挂着、列表真的有东西。
        Assert.False(viewModel.IsLoadingDevices);
        Assert.DoesNotContain(LR.RD_LoadingDevices, viewModel.StatusText, StringComparison.Ordinal);
        Assert.Single(viewModel.Devices);
    }

    [Fact]
    public async Task 加载态_失败后清掉并保留失败原因()
    {
        var (viewModel, client) = await CreateSignedInViewModelAsync();
        client.GroupsAsync = () => Task.FromException<IReadOnlyList<GroupDto>>(
            new ControlPlaneException("network_error", ControlPlaneErrorKind.Network));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoadingDevices);
        Assert.NotEmpty(viewModel.LoadFailure);
        Assert.DoesNotContain(LR.RD_LoadingDevices, viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 加载态_零台设备时清掉并落到空态()
    {
        var (viewModel, client) = await CreateSignedInViewModelAsync();
        client.GroupsAsync = () => Task.FromResult<IReadOnlyList<GroupDto>>([Group("g1")]);
        // 默认 nodes 为空：组读到了，但组里没有设备。

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoadingDevices);
        Assert.Empty(viewModel.Devices);
        Assert.Equal(LR.RD_NoDevicesHint, viewModel.EmptyStateText);
        Assert.DoesNotContain(LR.RD_LoadingDevices, viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 加载态_被更新的请求取代时旧请求不清新请求的状态()
    {
        var (viewModel, client) = await CreateSignedInViewModelAsync();
        var firstGate = new TaskCompletionSource<IReadOnlyList<GroupDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        client.GroupsAsync = () => Interlocked.Increment(ref calls) == 1
            ? firstGate.Task
            : Task.FromResult<IReadOnlyList<GroupDto>>([Group("g1")]);
        client.NodesAsync = _ => Task.FromResult<IReadOnlyList<NodeDto>>([Node("n1")]);

        // 旧请求卡住；新请求（换设备/切类型/再点刷新都会走这条路）立刻跑完。
        var stale = viewModel.RefreshCommand.ExecuteAsync(null);
        var fresh = viewModel.RefreshCommand.ExecuteAsync(null);
        await fresh;

        Assert.False(viewModel.IsLoadingDevices);
        Assert.Single(viewModel.Devices);

        // 旧请求这时才回来：它既不许改回设备列表，也不许把新请求的加载态/状态行动掉。
        firstGate.SetResult([Group("g9")]);
        await stale;

        Assert.False(viewModel.IsLoadingDevices);
        Assert.DoesNotContain(LR.RD_LoadingDevices, viewModel.StatusText, StringComparison.Ordinal);
        Assert.Single(viewModel.Devices);
        Assert.Equal("n1", viewModel.Devices[0].NodeId);
    }

    /// <summary>
    ///     建一个**已登录**的 VM。
    /// </summary>
    /// <remarks>
    ///     设备加载路径在未登录时会直接早退（那是对的：没登录就没有组可读），
    ///     因此要测加载态就必须先有一对令牌。
    /// </remarks>
    private async Task<(MobileRemoteDrawViewModel ViewModel, RecordingControlPlaneClient Client)> CreateSignedInViewModelAsync()
    {
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(new MainConfigModel()));
        var tokenStore = TestTokenStore.Create();
        await tokenStore.SaveAsync(new SectlToken("access", "refresh", "user-1", 3600), CancellationToken.None);

        var auth = new SectlAuthService(
            tokenStore,
            new StubHttpClientFactory(new HttpClient()),
            new DeviceUuidStore(configHandler, NullLogger<DeviceUuidStore>.Instance),
            NullLogger<SectlAuthService>.Instance,
            new LoopbackAuthRedirectBrokerFactory());
        await auth.InitializeAsync();

        var client = new RecordingControlPlaneClient();
        var viewModel = new MobileRemoteDrawViewModel(
            configHandler,
            client,
            new FileControlPlaneDevicePreferenceStore(_preferencePath),
            auth,
            NullLogger<MobileRemoteDrawViewModel>.Instance);

        return (viewModel, client);
    }

    /// <summary>
    ///     建一个"接入了自建集控"的 VM，默认**未登录 SECTL**——正是"手机不登录也能用"的那个场景。
    /// </summary>
    private (MobileRemoteDrawViewModel ViewModel, RecordingControlPlaneClient Client, FakeEnrollmentStore Store)
        CreateEnrolledViewModel(bool enrolled = true, bool expired = false, bool unreadable = false)
    {
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(new MainConfigModel()));
        var auth = new SectlAuthService(
            TestTokenStore.Create(),
            new StubHttpClientFactory(new HttpClient()),
            new DeviceUuidStore(configHandler, NullLogger<DeviceUuidStore>.Instance),
            NullLogger<SectlAuthService>.Instance,
            new LoopbackAuthRedirectBrokerFactory());

        var store = new FakeEnrollmentStore();
        if (enrolled || expired)
        {
            store.Save(new NodeEnrollmentRecord
            {
                NodeId = "node-1",
                GroupId = "g1",
                NodeToken = "srn_test_token",
                ExpiresAt = expired ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddDays(30)
            });
        }

        if (unreadable)
            store.MarkUnreadable();

        var client = new RecordingControlPlaneClient();
        var viewModel = new MobileRemoteDrawViewModel(
            configHandler,
            client,
            new FileControlPlaneDevicePreferenceStore(_preferencePath),
            auth,
            NullLogger<MobileRemoteDrawViewModel>.Instance,
            nodeStateStore: null,
            enrollmentStore: store);

        return (viewModel, client, store);
    }

    /// <summary>
    ///     建一个"手机端就地接入"的 VM：接入客户端、基址存储、真实平台名与设备名都接上，默认**未接入**。
    /// </summary>
    /// <remarks>
    ///     手机没有本地节点（<c>nodeStateStore: null</c>），接入时 node_id 整帧缺席、平台名报 android。
    /// </remarks>
    private (
        MobileRemoteDrawViewModel ViewModel,
        RecordingControlPlaneClient Client,
        FakeEnrollmentStore Store,
        FakeEndpointStore Endpoint) CreateEnrollingViewModel(
        RecordingEnrollHandler? handler = null,
        bool enrolled = false,
        bool customEndpoint = false)
    {
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(new MainConfigModel()));
        var auth = new SectlAuthService(
            TestTokenStore.Create(),
            new StubHttpClientFactory(new HttpClient()),
            new DeviceUuidStore(configHandler, NullLogger<DeviceUuidStore>.Instance),
            NullLogger<SectlAuthService>.Instance,
            new LoopbackAuthRedirectBrokerFactory());

        var endpoint = new FakeEndpointStore("https://control.example", customEndpoint);
        var store = new FakeEnrollmentStore();
        if (enrolled)
        {
            store.Save(new NodeEnrollmentRecord
            {
                NodeId = "node-1",
                GroupId = "g1",
                NodeToken = "srn_test_token",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
            });
        }

        var client = new RecordingControlPlaneClient();
        var viewModel = new MobileRemoteDrawViewModel(
            configHandler,
            client,
            new FileControlPlaneDevicePreferenceStore(_preferencePath),
            auth,
            NullLogger<MobileRemoteDrawViewModel>.Instance,
            nodeStateStore: null,
            enrollmentStore: store,
            enrollmentClient: new NodeEnrollmentClient(
                new FakeHttpClientFactory(
                    handler ?? new RecordingEnrollHandler(_ => Json(HttpStatusCode.OK, EnrollOk()))),
                endpoint),
            endpointStore: endpoint,
            platform: "android",
            deviceName: "Pixel 7");

        return (viewModel, client, store, endpoint);
    }

    // ---------------------------------------------------------------- 抽奖条件集（draw.trigger.conditions）

    [Fact]
    public void 条件集_设备没声明能力时绝不发conditions()
    {
        var (viewModel, client) = CreateViewModel();
        viewModel.SelectedDrawKind = LotteryKind;
        viewModel.SelectedDevice = Row("n1", supportsConditions: false);

        // 就算界面上残留了条件（换设备、旧状态），也**不许**把它们发出去：
        // 老设备会把 conditions 当"没写"从而静默按整池抽，那正是红线。
        viewModel.SelectedRecipientList = "高一（1）班";
        viewModel.SelectedPrizeTags.Add(new PrizeTagOption("文具"));

        Assert.False(viewModel.SupportsDrawConditions);
        Assert.False(viewModel.ShowsLotteryConditions);
        Assert.True(viewModel.ShowsLotteryConditionsUnsupported);

        client.Responder = request => request.Capability switch
        {
            ControlCapabilities.RosterRead => RosterCommand(PrizesResponse("元旦抽奖")),
            _ => DrawnCommand("lottery", "元旦抽奖", 1)
        };
        viewModel.LoadRosterCommand.Execute(null);
        viewModel.DrawCommand.Execute(null);

        var payload = client.LastSubmission.Payload!.Value;
        Assert.False(payload.TryGetProperty("conditions", out _));
    }

    [Fact]
    public void 条件集_声明了能力的抽奖设备会带上标签与发放名单()
    {
        var (viewModel, client) = CreateViewModel();
        viewModel.SelectedDrawKind = LotteryKind;

        client.Responder = request => request.Capability switch
        {
            ControlCapabilities.RosterRead => RosterCommand(PrizesResponse("元旦抽奖")),
            _ => DrawnCommand("lottery", "元旦抽奖", 1)
        };
        viewModel.SelectedDevice = Row("n1", supportsConditions: true);
        Assert.True(viewModel.ShowsLotteryConditions);

        viewModel.LoadRosterCommand.Execute(null);
        viewModel.SelectedPrizeTags.Add(new PrizeTagOption("一等奖"));
        viewModel.SelectedRecipientList = "高一（1）班";
        viewModel.DrawCommand.Execute(null);

        var payload = client.LastSubmission.Payload!.Value;
        var conditions = payload.GetProperty("conditions");
        Assert.Equal(1, conditions.GetProperty("version").GetInt32());
        Assert.Equal("一等奖", conditions.GetProperty("prize_tags")[0].GetString());
        Assert.Equal("高一（1）班", conditions.GetProperty("student_list").GetString());

        // 顶层 gender/group 对抽奖仍然**根本不发**（设备侧是"存在即拒绝"）。
        Assert.False(payload.TryGetProperty("gender", out _));
        Assert.False(payload.TryGetProperty("group", out _));
    }

    private static GroupDto Group(string groupId) => new() { GroupId = groupId, Name = groupId, Role = "admin" };

    // ---------------------------------------------------------------- 未登录 SECTL 也能用（已接入自建集控）

    /// <summary>
    ///     这一条就是整个功能的用户价值：**没登录 SECTL，只要接入了自建集控，设备照样读得到**。
    /// </summary>
    [Fact]
    public async Task 接入_未登录但已接入时设备照样读得到且不再提示登录()
    {
        var (viewModel, client, _) = CreateEnrolledViewModel();
        client.GroupsAsync = () => Task.FromResult<IReadOnlyList<GroupDto>>([Group("g1")]);
        client.NodesAsync = _ => Task.FromResult<IReadOnlyList<NodeDto>>([Node("n1")]);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsSignedIn);
        Assert.True(viewModel.IsEnrolled);
        Assert.True(viewModel.HasControlPlaneAccess);
        Assert.False(viewModel.NeedsSignIn);
        Assert.Single(viewModel.Devices);
        Assert.DoesNotContain(LR.RD_SignedOut, viewModel.EmptyStateText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 接入_未接入且未登录时还是原来那句提示并且不发注定失败的请求()
    {
        var groupsCalls = 0;
        var (viewModel, client, _) = CreateEnrolledViewModel(enrolled: false);
        client.GroupsAsync = () =>
        {
            Interlocked.Increment(ref groupsCalls);
            return Task.FromResult<IReadOnlyList<GroupDto>>([Group("g1")]);
        };

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsEnrolled);
        Assert.False(viewModel.HasControlPlaneAccess);
        Assert.True(viewModel.NeedsSignIn);
        Assert.False(viewModel.NeedsEnrollment);
        Assert.Equal(LR.RD_SignedOut, viewModel.EmptyStateText);

        // 没凭据就不该去请求：那只会拿一个 401 回来，还会把"请先登录"挤掉。
        Assert.Equal(0, groupsCalls);
    }

    [Fact]
    public async Task 接入_记录读不出来时要求去设置填接入码而不是反复重试()
    {
        var (viewModel, client, _) = CreateEnrolledViewModel(unreadable: true);
        client.GroupsAsync = () => Task.FromResult<IReadOnlyList<GroupDto>>([Group("g1")]);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsEnrolled);
        Assert.True(viewModel.NeedsEnrollment);
        Assert.True(viewModel.NeedsSignIn);
        Assert.Equal(LR.RD_EnrollmentRequired, viewModel.EmptyStateText);
    }

    [Fact]
    public async Task 接入_服务端拒了令牌时说要重新接入而不是去登录()
    {
        var (viewModel, client, _) = CreateEnrolledViewModel();
        client.GroupsAsync = () => Task.FromException<IReadOnlyList<GroupDto>>(
            new ControlPlaneException("unauthorized", ControlPlaneErrorKind.Unauthorized));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEnrolled); // 令牌还在，只是服务端不认了
        Assert.True(viewModel.EnrollmentRejected);
        Assert.True(viewModel.NeedsEnrollment);
        Assert.False(viewModel.NeedsSignIn); // 这时让用户去登录 SECTL 是死路
        Assert.NotEmpty(viewModel.LoadFailure); // 失败原因优先展示
        Assert.DoesNotContain(LR.RD_SignedOut, viewModel.EmptyStateText, StringComparison.Ordinal);
    }

    /// <param name="expected">0=未登录文案，1=去设置填接入码，2=重新接入，3=还没有设备。</param>
    [Theory]
    [InlineData(false, false, false, 0)] // 未登录未接入：未登录文案
    [InlineData(false, true, false, 1)] // 未接入、需要接入：去设置填接入码
    [InlineData(true, true, true, 2)] // 已接入、被服务端拒了：重新接入
    [InlineData(true, true, false, 3)] // 已接入、本地过期但服务端没说：不误报"失效"
    public void 接入_空态文案按接入优先于登录的顺序判定(
        bool isEnrolled,
        bool needsEnrollment,
        bool enrollmentExpired,
        int expected)
    {
        var text = MobileRemoteDrawViewModel.ResolveEmptyState(
            isSignedIn: false,
            isLoading: false,
            hasDevices: false,
            loadFailure: null,
            failedGroupCount: 0,
            unavailableReason: null,
            roleHint: null,
            hasRoster: false,
            isEnrolled: isEnrolled,
            needsEnrollment: needsEnrollment,
            enrollmentExpired: enrollmentExpired);

        var expectedText = expected switch
        {
            0 => LR.RD_SignedOut,
            1 => LR.RD_EnrollmentRequired,
            2 => LR.RD_EnrollmentExpired,
            _ => LR.RD_NoDevicesHint
        };

        Assert.Equal(expectedText, text);

        if (expected != 0)
            Assert.DoesNotContain(LR.RD_SignedOut, text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- 手机端内联接入卡片

    /// <summary>
    ///     未接入（也没登录）时，手机页必须**就地**给出接入入口，而不是只留一句"去设置"。
    /// </summary>
    /// <remarks>
    ///     手机上根本没有「设置 → 集控」那一页（<see cref="MobileSettingsNavigator" /> 找不到页面时既不报错也不返回失败），
    ///     只给导航按钮就等于给了一条走不通的路；这条用例同时钉住页面绑的是"卡片"而不是"导航"。
    /// </remarks>
    [Fact]
    public void 接入_未接入时手机页就地给出接入入口而不是死路()
    {
        var (viewModel, _, _, _) = CreateEnrollingViewModel();

        Assert.False(viewModel.IsEnrolled);
        Assert.False(viewModel.HasControlPlaneAccess);
        Assert.True(viewModel.ShowEnrollmentEntry); // 就地给卡片
        Assert.True(viewModel.ShowEndpointField);
        Assert.False(viewModel.ShowReenrollNavigation); // 那个导航按钮收起来，不留"点了没反应"
        Assert.False(viewModel.CanEnroll); // 没填接入码时按钮禁用
        Assert.False(viewModel.CanClearEnrollment);
        Assert.False(viewModel.HasEnrollmentMessage);

        // 卡片本身要真的在页面里：删掉这段 XAML 等于又把入口收了回去。
        var page = File.ReadAllText(GetRepositoryPath("SecRandom/Views/Mobile/MobileRemoteDrawPage.axaml"));
        Assert.Contains("ViewModel.ShowEnrollmentEntry", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.ControlPlaneEndpoint", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.EnrollmentCode", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.EnrollCommand", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.ClearEnrollmentCommand", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.ShowReenrollNavigation", page, StringComparison.Ordinal);
        Assert.DoesNotContain("IsVisible=\"{Binding ViewModel.NeedsEnrollment}\"", page, StringComparison.Ordinal);
    }

    /// <summary>没有接入存储的宿主（降级接线）不该看到一张填了也没用的卡。</summary>
    [Fact]
    public void 接入_没有接入存储的宿主不显示这张卡片()
    {
        var (viewModel, _) = CreateViewModel();

        Assert.False(viewModel.ShowEnrollmentEntry);
        Assert.False(viewModel.ShowEndpointField);
    }

    /// <summary>填了接入码点"接入"：就地换到节点令牌、清空输入框、并把设备列表拉起来。</summary>
    [Fact]
    public async Task 接入_填入接入码后换到节点令牌并自己刷新设备列表()
    {
        var handler = new RecordingEnrollHandler(_ => Json(HttpStatusCode.OK, EnrollOk()));
        var (viewModel, client, store, _) = CreateEnrollingViewModel(handler);
        // 页面加载（Loaded）之后卡片才可能被点到；订阅也是在这一步接上的。
        await viewModel.InitializeAsync();

        // 接入成功后列表要跟着起来：存储的 Changed 会触发一次刷新，这里等它真的发生。
        var refreshed = new TaskCompletionSource();
        client.GroupsAsync = () =>
        {
            refreshed.TrySetResult();
            return Task.FromResult<IReadOnlyList<GroupDto>>([Group("g1")]);
        };

        viewModel.EnrollmentCode = " 7K3M-9QZX ";
        Assert.True(viewModel.CanEnroll);

        await viewModel.EnrollCommand.ExecuteAsync(null);
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsEnrolled);
        Assert.Equal("高三（2）班", store.Status.GroupName);
        Assert.True(store.TryGetAccessToken(out var token));
        Assert.Equal("srn_node_9_secret", token);

        // 接入码用完即清，失败提示为空，卡片自己收起来。
        Assert.Equal(string.Empty, viewModel.EnrollmentCode);
        Assert.Equal(string.Empty, viewModel.EnrollmentMessage);
        Assert.False(viewModel.ShowEnrollmentEntry);

        // 请求形状：匿名、POST 到契约里的那条路径，带上本机真实平台与设备名（手机没有本地节点）。
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v1/node/enroll", request.Uri.AbsolutePath);
        Assert.Null(request.Authorization);

        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("7K3M-9QZX", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("android", body.RootElement.GetProperty("platform").GetString());
        Assert.Equal("Pixel 7", body.RootElement.GetProperty("display_name").GetString());
        Assert.Equal(GlobalConstants.Version, body.RootElement.GetProperty("version").GetString());
        Assert.False(body.RootElement.TryGetProperty("node_id", out _));
    }

    /// <summary>直接粘贴 <c>srn_…</c> 令牌：不走接入码那条一次性通道，一个请求都不该发。</summary>
    [Fact]
    public async Task 接入_直接粘贴令牌时不发接入请求()
    {
        var handler = new RecordingEnrollHandler(_ => Json(HttpStatusCode.OK, EnrollOk()));
        var (viewModel, _, store, _) = CreateEnrollingViewModel(handler);

        viewModel.EnrollmentCode = "srn_pasted_token";

        await viewModel.EnrollCommand.ExecuteAsync(null);

        Assert.Equal(0, handler.Calls);
        Assert.True(store.TryGetAccessToken(out var token));
        Assert.Equal("srn_pasted_token", token);
        Assert.Equal(string.Empty, viewModel.EnrollmentCode);
    }

    /// <summary>服务端拒绝时只显示错误码，接入码留着让用户改一个字符重试，且**绝不回显**接入码真值。</summary>
    [Fact]
    public async Task 接入_服务端拒绝时只显示错误码并且接入码留着让人改()
    {
        var handler = new RecordingEnrollHandler(_ =>
            Json(HttpStatusCode.Unauthorized, """{"code":"enrollment_code_invalid"}"""));
        var (viewModel, _, store, _) = CreateEnrollingViewModel(handler);

        viewModel.EnrollmentCode = "ABC-123";

        await viewModel.EnrollCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsEnrolled);
        Assert.False(store.Status.HasToken);
        Assert.Equal(string.Format(LR.RD_EnrollFailed, "enrollment_code_invalid"), viewModel.EnrollmentMessage);
        Assert.True(viewModel.HasEnrollmentMessage);
        Assert.True(viewModel.ShowEnrollmentEntry); // 卡片留着，用户能直接重填
        Assert.Equal("ABC-123", viewModel.EnrollmentCode);
        Assert.DoesNotContain("ABC-123", viewModel.EnrollmentMessage, StringComparison.Ordinal);
    }

    /// <summary>基址只在点"接入"那一刻落盘（绑定的默认触发器是逐字符更新，边打边写盘是另一回事）。</summary>
    [Fact]
    public async Task 接入_地址只在点接入时才落盘()
    {
        var handler = new RecordingEnrollHandler(_ => Json(HttpStatusCode.OK, EnrollOk()));
        var (viewModel, _, _, endpoint) = CreateEnrollingViewModel(handler);

        viewModel.ControlPlaneEndpoint = "https://console.example";
        Assert.Equal(0, endpoint.UpdateCalls);

        viewModel.EnrollmentCode = "7K3M-9QZX";
        await viewModel.EnrollCommand.ExecuteAsync(null);

        Assert.Equal(1, endpoint.UpdateCalls);
        Assert.Equal("https://console.example", endpoint.Current);
        // 请求确实打到了刚填的那个地址上（改完地址就能用，不必重启）。
        Assert.Equal("https://console.example", Assert.Single(handler.Requests).Uri.GetLeftPart(UriPartial.Authority));
    }

    /// <summary>地址不合法时只提示、不写盘、也不拿它去发请求。</summary>
    [Fact]
    public async Task 接入_地址不合法时只提示不拿它去发请求()
    {
        var handler = new RecordingEnrollHandler(_ => Json(HttpStatusCode.OK, EnrollOk()));
        var (viewModel, _, _, endpoint) = CreateEnrollingViewModel(handler);
        endpoint.AcceptUpdate = false;

        viewModel.ControlPlaneEndpoint = "这不是一个地址";
        viewModel.EnrollmentCode = "7K3M-9QZX";

        await viewModel.EnrollCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasEndpointError);
        Assert.False(viewModel.IsEnrolled);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(string.Empty, viewModel.EnrollmentMessage);
    }

    /// <summary>清除接入后回到"未接入"的入口状态：卡片重新出现，令牌立刻读不出来。</summary>
    [Fact]
    public async Task 接入_清除接入后回到未接入的入口状态()
    {
        var (viewModel, _, store, _) = CreateEnrollingViewModel(enrolled: true);
        await viewModel.InitializeAsync(); // 页面加载之后才会有"清除接入"这个动作

        Assert.True(viewModel.IsEnrolled);
        Assert.False(viewModel.ShowEnrollmentEntry); // 已经能用，不再拿接入卡片占地方
        Assert.True(viewModel.CanClearEnrollment);

        viewModel.ClearEnrollmentCommand.Execute(null);

        Assert.False(viewModel.IsEnrolled);
        Assert.False(store.TryGetAccessToken(out _));
        Assert.True(viewModel.ShowEnrollmentEntry);
        Assert.False(viewModel.ShowReenrollNavigation);
        Assert.Equal(string.Empty, viewModel.EnrollmentMessage);
    }

    /// <summary>基址被改成过自建的（说明在用自建集控）时，接入卡片也要给：节点令牌可能得补回来。</summary>
    [Fact]
    public void 接入_自建基址下即使已接入也给入口()
    {
        var (viewModel, _, _, _) = CreateEnrollingViewModel(enrolled: true, customEndpoint: true);

        Assert.True(viewModel.IsEnrolled);
        Assert.True(viewModel.ShowEnrollmentEntry);
        Assert.False(viewModel.ShowReenrollNavigation);
    }

    private static NodeDto Node(string nodeId) => new()
    {
        NodeId = nodeId,
        DisplayName = nodeId,
        Online = true,
        LocalRemoteAllowed = true,
        Capabilities = [ControlCapabilities.DrawTrigger, ControlCapabilities.RosterRead]
    };

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

        /// <summary>读组的行为（默认空：多数用例只关心选中设备之后的行为）。</summary>
        public Func<Task<IReadOnlyList<GroupDto>>> GroupsAsync { get; set; } =
            () => Task.FromResult<IReadOnlyList<GroupDto>>([]);

        /// <summary>读某组设备的行为（默认空）。</summary>
        public Func<string, Task<IReadOnlyList<NodeDto>>> NodesAsync { get; set; } =
            _ => Task.FromResult<IReadOnlyList<NodeDto>>([]);

        public ControlPlaneCommandRequest LastSubmission => Submissions[^1];

        /// <summary>某条能力最后一次下发的载荷。</summary>
        /// <remarks>
        ///     重置成功之后还会再读一次名单，因此"最后一次下发"未必是测试关心的那条命令。
        /// </remarks>
        public ControlPlaneCommandRequest LastSubmissionOf(string capability) =>
            Submissions.Last(request => string.Equals(request.Capability, capability, StringComparison.Ordinal));

        public Task<IReadOnlyList<GroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
            GroupsAsync();

        public Task<IReadOnlyList<NodeDto>> GetNodesAsync(string groupId, CancellationToken cancellationToken = default) =>
            NodesAsync(groupId);

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

    /// <summary>内存版接入存储：手机侧只关心"有没有接入"，所以这里只要状态对得上就行。</summary>
    private sealed class FakeEnrollmentStore : INodeEnrollmentStore
    {
        private readonly object _gate = new();
        private NodeEnrollmentRecord? _record;
        private bool _unreadable;

        public NodeEnrollmentStatus Status
        {
            get
            {
                lock (_gate)
                {
                    if (_unreadable)
                        return new NodeEnrollmentStatus { IsUnreadable = true };

                    if (_record is null)
                        return NodeEnrollmentStatus.NotEnrolled;

                    return new NodeEnrollmentStatus
                    {
                        HasToken = true,
                        IsExpired = _record.ExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow,
                        NodeId = _record.NodeId,
                        GroupId = _record.GroupId,
                        GroupName = _record.GroupName,
                        ExpiresAt = _record.ExpiresAt
                    };
                }
            }
        }

        public int Generation { get; private set; }

        public event EventHandler<NodeEnrollmentStatus>? Changed;

        /// <summary>模拟"记录读不出来"：文件还在，但已经按未接入处理。</summary>
        public void MarkUnreadable()
        {
            lock (_gate)
            {
                _record = null;
                _unreadable = true;
                Generation++;
            }

            Changed?.Invoke(this, Status);
        }

        public bool TryGetAccessToken(out string? accessToken)
        {
            lock (_gate)
            {
                accessToken = _record?.NodeToken;
                return _record is not null;
            }
        }

        public bool Save(NodeEnrollmentRecord record)
        {
            lock (_gate)
            {
                _record = record;
                _unreadable = false;
                Generation++;
            }

            Changed?.Invoke(this, Status);
            return true;
        }

        public bool Clear()
        {
            bool had;
            lock (_gate)
            {
                had = _record is not null || _unreadable;
                _record = null;
                _unreadable = false;
                Generation++;
            }

            Changed?.Invoke(this, Status);
            return had;
        }
    }

    private static string GetRepositoryPath(string relativePath) => Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")),
        relativePath);

    // ---------------------------------------------------------------- 接入请求的假件

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    /// <summary>契约里的成功回应；接入码换来的节点令牌放在 <c>node_token</c> 里。</summary>
    private static string EnrollOk(string nodeToken = "srn_node_9_secret") =>
        $$"""
          { "node_id": "node-9", "group_id": "grp_9", "group_name": "高三（2）班",
            "node_token": "{{nodeToken}}", "expires_at": "2030-01-02T03:04:05Z" }
          """;

    private sealed record SentEnrollRequest(HttpMethod Method, Uri Uri, string Body, string? Authorization);

    /// <summary>接住手机端发出去的那个接入请求：方法、地址、请求体、以及"到底有没有带凭据"。</summary>
    private sealed class RecordingEnrollHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<SentEnrollRequest> _requests = [];

        public IReadOnlyList<SentEnrollRequest> Requests
        {
            get
            {
                lock (_gate)
                    return [.. _requests];
            }
        }

        public int Calls
        {
            get
            {
                lock (_gate)
                    return _requests.Count;
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            lock (_gate)
            {
                _requests.Add(new SentEnrollRequest(
                    request.Method,
                    request.RequestUri!,
                    body,
                    request.Headers.Authorization?.ToString()));
            }

            return responder(request);
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>
    ///     内存版基址存储：手机端"改地址再接入"这条路径要能单测。
    /// </summary>
    private sealed class FakeEndpointStore : IControlPlaneEndpointStore
    {
        private readonly string _defaultBaseUrl;

        public FakeEndpointStore(string defaultBaseUrl, bool custom = false)
        {
            _defaultBaseUrl = defaultBaseUrl;
            Current = defaultBaseUrl;
            IsCustom = custom;
        }

        public string Current { get; private set; }

        public bool IsCustom { get; private set; }

        /// <summary>false = 模拟"这个地址不合法"，此时什么都不写、只回错误。</summary>
        public bool AcceptUpdate { get; set; } = true;

        public int UpdateCalls { get; private set; }

        public event EventHandler<string>? Changed;

        public bool TryUpdate(string? endpoint, out string? error)
        {
            UpdateCalls++;
            if (!AcceptUpdate || string.IsNullOrWhiteSpace(endpoint))
            {
                error = "invalid_endpoint";
                return false;
            }

            Current = endpoint.Trim();
            IsCustom = true;
            error = null;
            Changed?.Invoke(this, Current);
            return true;
        }

        public void ResetToDefault()
        {
            Current = _defaultBaseUrl;
            IsCustom = false;
            Changed?.Invoke(this, Current);
        }
    }
}
