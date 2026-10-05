using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        Assert.DoesNotContain(code, message, StringComparison.Ordinal);
    }

    [Fact]
    public void 失败文案_抽取被拒时区分上课时间与需要本机验证()
    {
        var classTime = ControlPlaneMessages.DescribeCommand(
            FailedCommand("draw_denied", new { reason = "blocked_by_class_time" }));
        var verification = ControlPlaneMessages.DescribeCommand(
            FailedCommand("draw_denied", new { reason = "local_verification_required" }));

        Assert.Equal(LR.RD_ClassTime, classTime);
        Assert.Equal(LR.RD_Denied, verification);
    }

    [Theory]
    // 设备侧形状：整条原因码就是 invalid_value:<字段>:<为什么>
    [InlineData("invalid_value:list_name:not_found", null, null, "RD_ListNotFound", null)]
    [InlineData("invalid_value:gender:not_in_list", null, null, "RD_NotInList", null)]
    [InlineData("invalid_value:count:out_of_range", null, null, "RD_OutOfRange", null)]
    [InlineData("invalid_value:list_name:no_candidate", null, null, "RD_NoCandidate", null)]
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
        var expected = Expected(expectedKey);

        Assert.Equal(formatField is null ? expected : string.Format(expected, formatField), message);
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

    private static string GetRepositoryPath(string relativePath) => Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")),
        relativePath);
}
