using System.Text;
using System.Text.Json;
using SecRandom.Core.Models;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Interfaces;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     集控三类新能力（<c>media.play</c> / <c>settings.write</c> / <c>roster.write</c>）的载荷校验。
/// </summary>
/// <remarks>
///     这些断言都钉在"**远程能对教室机做什么**"的边界上：能力清单一旦声明，服务端就会真的下发命令，
///     因此越界、越权、会让课堂出事的输入必须在解析阶段就被挡住，而不是等执行到一半。
/// </remarks>
public sealed class ControlNodePayloadTests
{
    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ---------------------------------------------------------------- media.play

    [Fact]
    public void 播报载荷_缺省动作是播报且文本被裁剪()
    {
        var parsed = ControlMediaPlayRequest.TryParse(
            Payload("""{ "text": "  请第一组上台  " } """), out var request, out var reason);

        Assert.True(parsed, reason);
        Assert.NotNull(request);
        Assert.Equal(ControlMediaPlayRequest.AnnounceAction, request.Action);
        Assert.Equal("请第一组上台", request.Text);
    }

    [Fact]
    public void 播报载荷_不支持的动作被明确拒绝而不是忽略()
    {
        // show（屏幕上显示任意文本）本机还没有承载界面。**假装成功**会让控制台
        // 以为教室里出现了内容，实际什么都没发生。
        var parsed = ControlMediaPlayRequest.TryParse(
            Payload("""{ "action": "show", "text": "hi" }"""), out _, out var reason);

        Assert.False(parsed);
        Assert.Equal("unsupported_action:show", reason);
    }

    [Fact]
    public void 播报载荷_超长文本被拒绝()
    {
        var text = new string('啊', ControlMediaPlayRequest.MaxTextLength + 1);

        var parsed = ControlMediaPlayRequest.TryParse(
            Payload($$"""{ "action": "announce", "text": "{{text}}" }"""), out _, out var reason);

        Assert.False(parsed);
        Assert.Equal("text_too_long", reason);
    }

    [Theory]
    [InlineData("""{ "action": "announce" }""")]
    [InlineData("""{ "action": "announce", "text": "   " }""")]
    [InlineData("""{ "action": "announce", "text": 5 }""")]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    public void 播报载荷_无效输入一律算无效命令(string json)
    {
        var parsed = ControlMediaPlayRequest.TryParse(Payload(json), out var request, out var reason);

        Assert.False(parsed);
        Assert.Null(request);
        Assert.Equal("invalid_command", reason);
    }

    // -------------------------------------------- media.play 的三个选项（§4.5.7）

    [Fact]
    public void 播报载荷_老载荷不带选项时三个字段都是没给()
    {
        var parsed = ControlMediaPlayRequest.TryParse(
            Payload("""{ "action": "announce", "text": "请第一组上台" }"""), out var request, out var reason);

        Assert.True(parsed, reason);
        Assert.NotNull(request);
        Assert.Null(request.ShowQuickDrawWindow);
        Assert.Null(request.SystemVolumePercent);
        Assert.Null(request.VoiceVolumePercent);
        Assert.False(request.HasTemporaryVolume);

        // 值相等即"逐字不变"：老载荷解析出来的记录必须与从前那个两参数构造完全一致。
        Assert.Equal(new ControlMediaPlayRequest("announce", "请第一组上台"), request);
    }

    [Fact]
    public void 播报载荷_显式null与缺键一样是没给()
    {
        var parsed = ControlMediaPlayRequest.TryParse(
            Payload("""
                    { "text": "hi", "show_quick_draw_window": null,
                      "system_volume_percent": null, "voice_volume_percent": null }
                    """),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        Assert.NotNull(request);
        Assert.Null(request.ShowQuickDrawWindow);
        Assert.Null(request.SystemVolumePercent);
        Assert.Null(request.VoiceVolumePercent);
        Assert.False(request.HasTemporaryVolume);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(100)]
    public void 播报载荷_音量选项接受0到100的整数(int percent)
    {
        var parsed = ControlMediaPlayRequest.TryParse(
            Payload($$"""
                     { "text": "hi", "system_volume_percent": {{percent}}, "voice_volume_percent": {{percent}} }
                     """),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        Assert.NotNull(request);
        Assert.Equal(percent, request.SystemVolumePercent);
        Assert.Equal(percent, request.VoiceVolumePercent);
        Assert.True(request.HasTemporaryVolume);
    }

    [Fact]
    public void 播报载荷_音量为0是明确设成静音而不是没给()
    {
        // 本节最要紧的一条：把"没碰过"落成 0，一次普通播报就会把教室机的音量清零；
        // 反过来把 0 当成"没给"，管理员发的"静音播报"就永远不生效。
        var muted = ControlMediaPlayRequest.TryParse(
            Payload("""{ "text": "hi", "system_volume_percent": 0, "voice_volume_percent": 0 }"""),
            out var silentRequest,
            out var silentReason);
        var untouched = ControlMediaPlayRequest.TryParse(
            Payload("""{ "text": "hi" }"""), out var plainRequest, out var plainReason);

        Assert.True(muted, silentReason);
        Assert.True(untouched, plainReason);
        Assert.Equal(0, silentRequest!.VoiceVolumePercent);
        Assert.Equal(0, silentRequest.SystemVolumePercent);
        Assert.Null(plainRequest!.VoiceVolumePercent);
        Assert.Null(plainRequest.SystemVolumePercent);
    }

    [Fact]
    public void 播报载荷_带小数点的整数值按整数接受()
    {
        // JSON 里 40.0 与 40 是同一个数：手写载荷带小数点不该被判成坏数据
        // （与 roster 的 count / weight 同一条约定）；40.5 才是没有意义的分数百分比。
        var parsed = ControlMediaPlayRequest.TryParse(
            Payload("""{ "text": "hi", "voice_volume_percent": 40.0 }"""), out var request, out var reason);

        Assert.True(parsed, reason);
        Assert.Equal(40, request!.VoiceVolumePercent);
    }

    [Theory]
    [InlineData("""{ "text": "hi", "system_volume_percent": 101 }""", "system_volume_percent")]
    [InlineData("""{ "text": "hi", "system_volume_percent": -1 }""", "system_volume_percent")]
    [InlineData("""{ "text": "hi", "system_volume_percent": 40.5 }""", "system_volume_percent")]
    [InlineData("""{ "text": "hi", "system_volume_percent": "40" }""", "system_volume_percent")]
    [InlineData("""{ "text": "hi", "system_volume_percent": true }""", "system_volume_percent")]
    [InlineData("""{ "text": "hi", "voice_volume_percent": 101 }""", "voice_volume_percent")]
    [InlineData("""{ "text": "hi", "voice_volume_percent": -1 }""", "voice_volume_percent")]
    [InlineData("""{ "text": "hi", "voice_volume_percent": 40.5 }""", "voice_volume_percent")]
    [InlineData("""{ "text": "hi", "voice_volume_percent": "80" }""", "voice_volume_percent")]
    [InlineData("""{ "text": "hi", "voice_volume_percent": [] }""", "voice_volume_percent")]
    public void 播报载荷_音量选项非法时整条拒绝并指出字段(string json, string field)
    {
        // 不许悄悄降级成"没给"：降级会让控制台以为音量调过了，而教室里根本没变。
        var parsed = ControlMediaPlayRequest.TryParse(Payload(json), out var request, out var reason, out var hint);

        Assert.False(parsed);
        Assert.Null(request);
        Assert.Equal("invalid_command", reason);
        Assert.NotNull(hint);
        Assert.Contains(field, hint);
        Assert.Contains("0-100", hint);
    }

    [Theory]
    [InlineData("""{ "text": "hi", "show_quick_draw_window": true }""", true)]
    [InlineData("""{ "text": "hi", "show_quick_draw_window": false }""", false)]
    [InlineData("""{ "text": "hi", "show_quick_draw_window": "true" }""", false)]
    [InlineData("""{ "text": "hi", "show_quick_draw_window": 1 }""", false)]
    [InlineData("""{ "text": "hi", "show_quick_draw_window": null }""", null)]
    [InlineData("""{ "text": "hi" }""", null)]
    public void 播报载荷_闪抽开关只认字面量true(string json, bool? expected)
    {
        // 协议明文：只认 JSON 的 true；字符串 "true" 按 false 处理（没有"半显示"这种语义），
        // 而缺失 / null 是"没给"。
        var parsed = ControlMediaPlayRequest.TryParse(Payload(json), out var request, out var reason);

        Assert.True(parsed, reason);
        Assert.Equal(expected, request!.ShowQuickDrawWindow);
    }

    [Fact]
    public void 播报载荷_三参数重载仍然不给hint()
    {
        // 老签名（三个 out 参数）是既有调用点与单测用的那个：它必须继续可编译、行为不变。
        var parsed = ControlMediaPlayRequest.TryParse(
            Payload("""{ "text": "hi", "voice_volume_percent": 200 }"""), out _, out var reason);

        Assert.False(parsed);
        Assert.Equal("invalid_command", reason);
    }

    // ---------------------------------------------------------------- settings.write

    [Fact]
    public void 设置白名单_包含的路径都是真正可写的()
    {
        var planned = ControlSettingsWhitelist.TryPlan(
            Payload("""
                    { "patch": { "voice.enable": true, "voice.volume": 30, "voice.speech_rate": 120,
                                 "roll_call.half_repeat": 3, "quick_draw.disable_after_click": 2,
                                 "lottery.half_repeat": 4, "notification.roll_call.enabled": false,
                                 "notification.quick_draw.enabled": true, "notification.lottery.enabled": true } }
                    """),
            out var changes,
            out var reason);

        Assert.True(planned, reason);
        Assert.Equal(9, changes.Count);

        var model = new MainConfigModel();
        ControlSettingsWhitelist.Apply(model, changes);

        Assert.True(model.VoiceSettings.VoiceEnable);
        Assert.Equal(30, model.VoiceSettings.VolumeSize);
        Assert.Equal(120, model.VoiceSettings.SpeechRate);
        Assert.Equal(3, model.RollCallSettings.HalfRepeat);
        Assert.Equal(2, model.QuickDrawSettings.DisableAfterClick);
        Assert.Equal(4, model.LotterySettings.HalfRepeat);
        Assert.False(model.NotificationSettings.RollCall.Enabled);
        Assert.True(model.NotificationSettings.QuickDraw.Enabled);
        Assert.True(model.NotificationSettings.Lottery.Enabled);
    }

    /// <summary>
    ///     安全设置、更新、备份、桌面集成（自启/协议注册）不允许远程修改。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         判定用**路径分段**而不是子串：子串规则会把无辜字段一起误伤——
    ///         <c>voice.system_volume_control</c>、<c>more.roll_call_control_panel_position</c>、
    ///         <c>more.*_quantity_control</c> 只是恰好以 <c>control</c> 结尾，和"集控设置"无关。
    ///         段规则既挡住真正的敏感子树（<c>general.backup.*</c> 的 <c>backup</c> 段），
    ///         又不会连带禁掉普通开关。
    ///     </para>
    ///     <para>
    ///         另有一批**逐条列出**的只读项（置顶模式、后台驻留、启动即显示、同意项、
    ///         证明留存策略、URL 协议注册）：它们不是靠命名规律识别的，必须显式出现，
    ///         否则将来有人复制一个类似命名就会悄悄放开。
    ///     </para>
    /// </remarks>
    [Fact]
    public void 设置白名单_不包含安全更新备份与桌面集成()
    {
        string[] forbiddenSegments = ["security", "update", "backup", "autostart", "protocol"];

        foreach (var path in ControlSettingsWhitelist.WritablePaths)
        {
            var segments = path.Split('.');

            foreach (var segment in segments)
            {
                Assert.DoesNotContain(
                    segment, forbiddenSegments, StringComparer.OrdinalIgnoreCase);
            }
        }

        // 显式只读项：改了它们等于改这台机器的启动方式、提权方式、同意的条款或证据留存。
        string[] alwaysReadOnly =
        [
            "general.basic.main_window_topmost_mode",
            "floating_window.floating_window_topmost_mode",
            "general.basic.background_resident",
            "general.basic.show_startup_window",
            "general.basic.url_protocol",
            "general.basic.guide_completed",
            "general.basic.accepted_eula_version",
            "general.basic.accepted_privacy_policy_version",
            "general.basic.accepted_gpl_version",
            "general.basic.accepted_verification_notice_version"
        ];

        foreach (var path in alwaysReadOnly)
        {
            Assert.DoesNotContain(
                path, ControlSettingsWhitelist.WritablePaths, StringComparer.Ordinal);
        }

        Assert.DoesNotContain(
            ControlSettingsWhitelist.WritablePaths,
            path => path.StartsWith("general.proof_retention.", StringComparison.Ordinal));
    }

    /// <summary>
    ///     被旧的子串规则误伤的普通开关必须**可写**。
    /// </summary>
    /// <remarks>
    ///     钉住这条是因为它们看起来"像是集控设置"（名字里带 control），
    ///     很容易在下一次收紧规则时被顺手禁掉——而它们只是界面布局与音量控制的开关。
    /// </remarks>
    [Fact]
    public void 设置白名单_包含被名字误伤的普通开关()
    {
        string[] innocent =
        [
            "voice.system_volume_control",
            "more.roll_call_control_panel_position",
            "more.lottery_control_panel_position",
            "more.roll_call_quantity_control",
            "more.lottery_quantity_control"
        ];

        foreach (var path in innocent)
        {
            Assert.Contains(path, ControlSettingsWhitelist.WritablePaths);
        }
    }

    [Fact]
    public void 设置变更_未知路径整体拒绝并带回具体路径()
    {
        var planned = ControlSettingsWhitelist.TryPlan(
            Payload("""{ "patch": { "voice.volume": 50, "security.require_password": false } }"""),
            out var changes,
            out var reason);

        Assert.False(planned);
        Assert.Empty(changes);
        Assert.Equal("not_writable:security.require_password", reason);
    }

    [Theory]
    [InlineData("""{ "patch": { "voice.volume": "大" } }""", "invalid_value:voice.volume:type_mismatch")]
    [InlineData("""{ "patch": { "voice.volume": 101 } }""", "invalid_value:voice.volume:out_of_range:0..100")]
    [InlineData("""{ "patch": { "voice.enable": "true" } }""", "invalid_value:voice.enable:type_mismatch")]
    [InlineData("""{ "patch": { "roll_call.half_repeat": 0 } }""", "invalid_value:roll_call.half_repeat:out_of_range:1..20")]
    [InlineData("""{ "patch": {} }""", "invalid_command")]
    [InlineData("""{ "patch": [] }""", "invalid_command")]
    public void 设置变更_非法值被拒绝且不改动任何设置(string json, string expectedReason)
    {
        var planned = ControlSettingsWhitelist.TryPlan(Payload(json), out var changes, out var reason);

        Assert.False(planned);
        Assert.Empty(changes);
        Assert.Equal(expectedReason, reason);
    }

    // ---------------------------------------------------------------- roster.write

    [Fact]
    public void 名单下发_替换模式会丢掉未提及的学生()
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload("""
                    { "list_name": "高一（1）班", "mode": "replace", "activate": false,
                      "students": [ { "id": "01", "name": "张三" }, { "id": "02", "name": "李四", "enabled": false } ] }
                    """),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        Assert.NotNull(request);
        Assert.Equal("高一（1）班", request.ListName);
        Assert.Equal(RosterWriteModes.Replace, request.Mode);
        Assert.False(request.Activate);
        Assert.Equal(2, request.Students.Count);
        Assert.True(request.Students[0].Exists);
        Assert.False(request.Students[1].Exists);
    }

    [Fact]
    public void 名单下发_学号与姓名都空的行不算学生()
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload("""
                    { "list_name": "高一（1）班",
                      "students": [ { "id": "", "name": "  " }, { "id": "01", "name": "张三" } ] }
                    """),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        Assert.NotNull(request);
        Assert.Single(request.Students);
        Assert.Equal("张三", request.Students[0].Name);
    }

    [Fact]
    public void 名单下发_合并模式就地更新以保住历史身份()
    {
        var recordId = Guid.NewGuid();
        var existing = new StudentList("高一（1）班")
        {
            Students =
            [
                new Student { RecordId = recordId, Id = "01", Name = "张三", Group = "A" },
                new Student { RecordId = Guid.NewGuid(), Id = "02", Name = "李四" }
            ]
        };

        var merged = ControlRosterMerge.Merge(
            existing,
            [
                new ControlRosterStudentInput("01", "张三丰", string.Empty, "B", true),
                new ControlRosterStudentInput("03", "王五", string.Empty, string.Empty, true)
            ]);

        Assert.Equal(3, merged.Count);

        // 命中者**就地更新**：RecordId 必须还是原来那个，否则历史与公平性统计会和这个人断掉。
        Assert.Equal(recordId, merged[0].RecordId);
        Assert.Equal("张三丰", merged[0].Name);
        Assert.Equal("B", merged[0].Group);

        // 未提及的本地学生保留，新学生追加。
        Assert.Equal("李四", merged[1].Name);
        Assert.Equal("王五", merged[2].Name);
    }

    [Fact]
    public void 名单下发_合并模式在本地名单不存在时等于替换()
    {
        var merged = ControlRosterMerge.Merge(
            null, [new ControlRosterStudentInput("01", "张三", string.Empty, string.Empty, true)]);

        Assert.Single(merged);
    }

    [Fact]
    public void 名单下发_合并模式未下发标签时保留设备上的标签()
    {
        var existing = new StudentList("高一（1）班")
        {
            Students =
            [
                new Student { Id = "01", Name = "张三", Tags = "尖子 组长" },
                new Student { Id = "02", Name = "李四", Tags = "住宿" }
            ]
        };

        // 控制台只改了一个人的名字，tags 整条都没提。
        var merged = ControlRosterMerge.Merge(
            existing, [new ControlRosterStudentInput("01", "张三丰", string.Empty, string.Empty, true)]);

        // 这一行的标签必须原样留着，别人更不该被动到——"没提"不是"清空"。
        Assert.Equal("尖子 组长", merged[0].Tags);
        Assert.Equal("住宿", merged[1].Tags);
    }

    [Fact]
    public void 名单下发_合并模式下发标签时覆盖且规范化()
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload("""
                    { "list_name": "高一（1）班", "mode": "merge",
                      "students": [ { "id": "01", "name": "张三",
                                      "tags": [ "  尖子 ", "", "组长", "尖子", "三好;住宿" ] } ] }
                    """),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        Assert.NotNull(request);

        // 规范化与读通道同一个 helper：trim、丢空项、去重，元素里的分号同样按导入的分隔符拆开。
        Assert.Equal(new[] { "尖子", "组长", "三好", "住宿" }, request.Students[0].Tags);

        var existing = new StudentList("高一（1）班")
        {
            Students = [new Student { Id = "01", Name = "张三", Tags = "旧标签" }]
        };

        var merged = ControlRosterMerge.Merge(existing, request.Students);

        Assert.Equal("尖子 组长 三好 住宿", merged[0].Tags);
    }

    [Fact]
    public void 名单下发_合并模式空数组明确清空标签()
    {
        var existing = new StudentList("高一（1）班")
        {
            Students = [new Student { Id = "01", Name = "张三", Tags = "尖子 组长" }]
        };

        var merged = ControlRosterMerge.Merge(
            existing,
            [new ControlRosterStudentInput("01", "张三", string.Empty, string.Empty, true, [])]);

        Assert.Equal(string.Empty, merged[0].Tags);
    }

    [Fact]
    public void 名单下发_替换模式未下发标签等于没有标签()
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload("""
                    { "list_name": "高一（1）班", "mode": "replace",
                      "students": [ { "id": "01", "name": "张三" }, { "id": "02", "name": "李四", "tags": [ "住宿" ] } ] }
                    """),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        Assert.NotNull(request);

        // 缺失的 tags 是"本次没下发"，不是"清空"——两者在载荷这一层必须还能分辨。
        Assert.Null(request.Students[0].Tags);
        Assert.Equal(new[] { "住宿" }, request.Students[1].Tags);

        // replace 按载荷重建整份名单：没下发标签的行显式落成空标签，而不是靠模型默认值恰好为空。
        Assert.Equal(string.Empty, request.Students[0].ToStudent().Tags);
        Assert.Equal("住宿", request.Students[1].ToStudent().Tags);
    }

    [Theory]
    [InlineData("""{ "id": "01" }""")]
    [InlineData("""{ "id": "01", "tags": null }""")]
    public void 名单下发_缺省与显式null都是本次未下发标签(string entry)
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload($$"""{ "list_name": "高一", "students": [ {{entry}} ] }"""), out var request, out var reason);

        Assert.True(parsed, reason);
        Assert.Null(request!.Students[0].Tags);
    }

    [Fact]
    public void 名单下发_空数组是明确清空而不是没下发()
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload("""{ "list_name": "高一", "students": [ { "id": "01", "tags": [] } ] }"""),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        var emptyTags = request!.Students[0].Tags;
        Assert.NotNull(emptyTags);
        Assert.Empty(emptyTags);

        // 给了但规范化后一个都不剩，同样是"明确清空"。
        var blank = ControlRosterPushRequest.TryParse(
            Payload("""{ "list_name": "高一", "students": [ { "id": "01", "tags": [ "  ", ";" ] } ] }"""),
            out var blankRequest,
            out var blankReason);

        Assert.True(blank, blankReason);
        var blankTags = blankRequest!.Students[0].Tags;
        Assert.NotNull(blankTags);
        Assert.Empty(blankTags);
    }

    [Theory]
    [InlineData("""{ "list_name": "  ", "students": [ { "id": "01" } ] }""", "invalid_list_name")]
    [InlineData("""{ "list_name": "高一", "students": [] }""", "empty_roster")]
    [InlineData("""{ "list_name": "高一", "students": [ { "id": "" } ] }""", "empty_roster")]
    [InlineData("""{ "list_name": "高一", "students": "01,02" }""", "invalid_command")]
    [InlineData("""{ "students": [ { "id": "01" } ] }""", "invalid_command")]
    [InlineData("""{ "list_name": "高一", "mode": "upsert", "students": [ { "id": "01" } ] }""", "unsupported_mode:upsert")]
    [InlineData("""{ "list_name": "高一", "students": [ { "id": "01", "tags": "尖子 组长" } ] }""", "invalid_command")]
    [InlineData("""{ "list_name": "高一", "students": [ { "id": "01", "tags": { "a": 1 } } ] }""", "invalid_command")]
    [InlineData("""{ "list_name": "高一", "students": [ { "id": "01", "tags": [ "尖子", 7 ] } ] }""", "invalid_command")]
    [InlineData("""{ "list_name": "高一", "students": [ { "id": "01", "tags": [ "尖子", null ] } ] }""", "invalid_command")]
    public void 名单下发_非法载荷被拒绝(string json, string expectedReason)
    {
        var parsed = ControlRosterPushRequest.TryParse(Payload(json), out var request, out var reason);

        Assert.False(parsed);
        Assert.Null(request);
        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void 名单下发_超过人数上限被拒绝()
    {
        var students = string.Join(
            ",",
            Enumerable.Range(1, ControlRosterPushRequest.MaxStudents + 1)
                .Select(index => $$"""{ "id": "{{index}}" }"""));

        var parsed = ControlRosterPushRequest.TryParse(
            Payload($$"""{ "list_name": "高一", "students": [{{students}}] }"""), out _, out var reason);

        Assert.False(parsed);
        Assert.Equal($"roster_too_large:{ControlRosterPushRequest.MaxStudents}", reason);
    }

    // ---------------------------------------------------------------- roster.write（奖池）

    /// <summary>
    ///     同一条 <c>roster.write</c> 能力靠 <c>roster_kind</c> + 数组名分流到奖池。
    /// </summary>
    /// <remarks>
    ///     这条分支从协议落地那天起就是缺的：控制台一直在发 <c>roster_kind: "prizes"</c>，
    ///     客户端却只会找 <c>students</c>，于是每一次奖池下发都被判成 <c>invalid_command</c>。
    /// </remarks>
    [Fact]
    public void 奖池下发_奖品载荷被解析成奖品输入()
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload("""
                    { "list_name": "元旦抽奖", "mode": "merge", "activate": true, "roster_kind": "prizes",
                      "prizes": [ { "id": "p1", "name": "一等奖", "count": 2, "weight": 1.5, "enabled": true,
                                    "tags": [ "甲", "乙" ] },
                                  { "name": "二等奖", "enabled": false } ] }
                    """),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        Assert.NotNull(request);
        Assert.True(request.IsPrizeRoster);
        Assert.Equal(ControlRosterPushRequest.PrizesKind, request.RosterKind);
        Assert.Equal("元旦抽奖", request.ListName);
        Assert.Equal(RosterWriteModes.Merge, request.Mode);
        Assert.True(request.Activate);

        // 奖池载荷里没有学生：两种数组不会同时出现。
        Assert.Empty(request.Students);

        var prizes = request.Prizes!;
        Assert.Equal(2, prizes.Count);
        Assert.Equal("p1", prizes[0].Id);
        Assert.Equal("一等奖", prizes[0].Name);
        Assert.Equal(2, prizes[0].Count);
        Assert.Equal(1.5, prizes[0].Weight);
        Assert.True(prizes[0].Exists);
        Assert.Equal(new[] { "甲", "乙" }, prizes[0].Tags);

        // 缺 count / weight 用手写载荷的默认值（与 Prize 自己的默认值一致），缺 enabled 算启用。
        Assert.Equal(1, prizes[1].Count);
        Assert.Equal(1, prizes[1].Weight);
        Assert.False(prizes[1].Exists);
    }

    [Theory]
    [InlineData("""{ "list_name": "高一（1）班", "students": [ { "id": "01", "name": "张三" } ] }""")]
    [InlineData("""{ "list_name": "高一（1）班", "roster_kind": "students", "students": [ { "id": "01", "name": "张三" } ] }""")]
    // 非字符串的 roster_kind 与读通道一样按缺省处理：类型不对时"猜一个类型"比"拒收"更危险，
    // 所以两边都退回点名，而不是把 7 拼进原因码。
    [InlineData("""{ "list_name": "高一（1）班", "roster_kind": 7, "students": [ { "id": "01", "name": "张三" } ] }""")]
    public void 名单下发_缺省与显式students都走学生路径(string json)
    {
        var parsed = ControlRosterPushRequest.TryParse(Payload(json), out var request, out var reason);

        Assert.True(parsed, reason);
        Assert.NotNull(request);
        Assert.False(request.IsPrizeRoster);
        Assert.Equal(ControlRosterPushRequest.StudentsKind, request.RosterKind);
        Assert.Null(request.Prizes);
        Assert.Single(request.Students);
    }

    [Fact]
    public void 奖池下发_合并模式就地更新以保住历史身份()
    {
        var recordId = Guid.NewGuid();
        var existing = new PrizeList("元旦抽奖")
        {
            Prizes =
            [
                new Prize { RecordId = recordId, Id = "p1", Name = "一等奖", Count = 2, Weight = 1.5 },
                // 没有编号的奖品只能按名字认人——这一条同时钉住"编号为空时回落到 name"。
                new Prize { Id = string.Empty, Name = "二等奖", Tags = "文具" }
            ]
        };

        var nameMatchedRecordId = existing.Prizes[1].RecordId;

        var merged = ControlRosterMerge.MergePrizes(
            existing,
            [
                new ControlRosterPrizeInput("p1", "特等奖", 3, 2.0, true),
                new ControlRosterPrizeInput(string.Empty, "二等奖", 5, 1, true),
                new ControlRosterPrizeInput("p3", "三等奖", 1, 1, true)
            ]);

        Assert.Equal(3, merged.Count);

        // 命中者**就地更新**：RecordId 必须还是原来那个，否则抽奖历史与公平性统计会和这个奖项断掉。
        Assert.Equal(recordId, merged[0].RecordId);
        Assert.Equal("特等奖", merged[0].Name);
        Assert.Equal(3, merged[0].Count);
        Assert.Equal(2.0, merged[0].Weight);

        // 按名字命中的那一条同样就地更新；这次没下发 tags，标签留着。
        Assert.Equal(nameMatchedRecordId, merged[1].RecordId);
        Assert.Equal(5, merged[1].Count);
        Assert.Equal("文具", merged[1].Tags);

        // 未提及的本地奖品保留，新奖品追加。
        Assert.Equal("三等奖", merged[2].Name);
    }

    [Fact]
    public void 奖池下发_合并模式在本地奖池不存在时等于替换()
    {
        var merged = ControlRosterMerge.MergePrizes(
            null, [new ControlRosterPrizeInput("p1", "一等奖", 1, 1, true)]);

        Assert.Single(merged);
    }

    [Fact]
    public void 奖池下发_合并模式未下发标签时保留设备上的标签()
    {
        var existing = new PrizeList("元旦抽奖")
        {
            Prizes =
            [
                new Prize { Id = "p1", Name = "一等奖", Tags = "甲 乙" },
                new Prize { Id = "p2", Name = "二等奖", Tags = "丙" }
            ]
        };

        // 控制台只改了数量，tags 整条都没提。
        var merged = ControlRosterMerge.MergePrizes(
            existing, [new ControlRosterPrizeInput("p1", "一等奖", 5, 1, true)]);

        // 这一行的标签必须原样留着，别人更不该被动到——"没提"不是"清空"。
        Assert.Equal("甲 乙", merged[0].Tags);
        Assert.Equal("丙", merged[1].Tags);
        Assert.Equal(5, merged[0].Count);
    }

    [Fact]
    public void 奖池下发_合并模式下发标签时覆盖且规范化()
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload("""
                    { "list_name": "元旦抽奖", "mode": "merge", "roster_kind": "prizes",
                      "prizes": [ { "id": "p1", "name": "一等奖",
                                    "tags": [ "  甲 ", "", "乙", "甲", "丙;丁" ] } ] }
                    """),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        Assert.NotNull(request);

        // 规范化与读通道同一个 helper：trim、丢空项、去重，元素里的分号同样按导入的分隔符拆开。
        Assert.Equal(new[] { "甲", "乙", "丙", "丁" }, request.Prizes![0].Tags);

        var existing = new PrizeList("元旦抽奖")
        {
            Prizes = [new Prize { Id = "p1", Name = "一等奖", Tags = "旧标签" }]
        };

        var merged = ControlRosterMerge.MergePrizes(existing, request.Prizes!);

        Assert.Equal("甲 乙 丙 丁", merged[0].Tags);
    }

    [Fact]
    public void 奖池下发_合并模式空数组明确清空标签()
    {
        var existing = new PrizeList("元旦抽奖")
        {
            Prizes = [new Prize { Id = "p1", Name = "一等奖", Tags = "甲 乙" }]
        };

        var merged = ControlRosterMerge.MergePrizes(
            existing, [new ControlRosterPrizeInput("p1", "一等奖", 1, 1, true, [])]);

        Assert.Equal(string.Empty, merged[0].Tags);
    }

    [Theory]
    [InlineData("""{ "id": "p1", "name": "一等奖" }""")]
    [InlineData("""{ "id": "p1", "name": "一等奖", "tags": null }""")]
    public void 奖池下发_缺省与显式null都是本次未下发标签(string entry)
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload($$"""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [ {{entry}} ] }"""),
            out var request, out var reason);

        Assert.True(parsed, reason);
        Assert.Null(request!.Prizes![0].Tags);
    }

    [Fact]
    public void 奖池下发_空数组是明确清空而不是没下发()
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload("""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [ { "id": "p1", "tags": [] } ] }"""),
            out var request, out var reason);

        Assert.True(parsed, reason);
        var emptyTags = request!.Prizes![0].Tags;
        Assert.NotNull(emptyTags);
        Assert.Empty(emptyTags);

        // 给了但规范化后一个都不剩，同样是"明确清空"。
        var blank = ControlRosterPushRequest.TryParse(
            Payload("""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [ { "id": "p1", "tags": [ "  ", ";" ] } ] }"""),
            out var blankRequest, out var blankReason);

        Assert.True(blank, blankReason);
        var blankTags = blankRequest!.Prizes![0].Tags;
        Assert.NotNull(blankTags);
        Assert.Empty(blankTags);
    }

    [Fact]
    public void 奖池下发_替换模式未下发标签等于没有标签()
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload("""
                    { "list_name": "元旦抽奖", "mode": "replace", "roster_kind": "prizes",
                      "prizes": [ { "id": "p1", "name": "一等奖" }, { "id": "p2", "name": "二等奖", "tags": [ "文具" ] } ] }
                    """),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        Assert.NotNull(request);

        // 缺失的 tags 是"本次没下发"，不是"清空"——两者在载荷这一层必须还能分辨。
        Assert.Null(request.Prizes![0].Tags);
        Assert.Equal(new[] { "文具" }, request.Prizes[1].Tags);

        // replace 按载荷重建整份奖池：没下发标签的奖品显式落成空标签，而不是靠模型默认值恰好为空。
        Assert.Equal(string.Empty, request.Prizes[0].ToPrize().Tags);
        Assert.Equal("文具", request.Prizes[1].ToPrize().Tags);
    }

    [Theory]
    [InlineData("""{ "name": "一等奖" }""", 1, 1d)]
    [InlineData("""{ "name": "一等奖", "count": 2, "weight": 3 }""", 2, 3d)]
    [InlineData("""{ "name": "一等奖", "count": 2.0, "weight": 1.5 }""", 2, 1.5d)]
    [InlineData("""{ "name": "一等奖", "count": null, "weight": null }""", 1, 1d)]
    public void 奖池下发_数量与权重缺省时用默认值(string entry, int expectedCount, double expectedWeight)
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload($$"""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [ {{entry}} ] }"""),
            out var request, out var reason);

        Assert.True(parsed, reason);
        Assert.Equal(expectedCount, request!.Prizes![0].Count);
        Assert.Equal(expectedWeight, request.Prizes[0].Weight);
    }

    [Theory]
    [InlineData("""{ "list_name": "元旦抽奖", "roster_kind": "prizes" }""", "invalid_command")]
    [InlineData("""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": {} }""", "invalid_command")]
    [InlineData("""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "students": [ { "id": "01" } ] }""", "invalid_command")]
    [InlineData("""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [] }""", "empty_roster")]
    [InlineData("""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [ { "id": "  " } ] }""", "empty_roster")]
    [InlineData("""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [ "p1" ] }""", "invalid_prize_entry")]
    [InlineData("""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [ { "name": "一等奖", "count": "2" } ] }""", "invalid_prize_entry")]
    [InlineData("""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [ { "name": "一等奖", "count": 2.5 } ] }""", "invalid_prize_entry")]
    [InlineData("""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [ { "name": "一等奖", "weight": true } ] }""", "invalid_prize_entry")]
    [InlineData("""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [ { "name": "一等奖", "tags": "文具" } ] }""", "invalid_command")]
    [InlineData("""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [ { "name": "一等奖", "tags": { "a": 1 } } ] }""", "invalid_command")]
    [InlineData("""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [ { "name": "一等奖", "tags": [ "文具", 7 ] } ] }""", "invalid_command")]
    [InlineData("""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [ { "name": "一等奖", "tags": [ "文具", null ] } ] }""", "invalid_command")]
    [InlineData("""{ "list_name": "  ", "roster_kind": "prizes", "prizes": [ { "name": "一等奖" } ] }""", "invalid_list_name")]
    [InlineData("""{ "list_name": "元旦抽奖", "mode": "upsert", "roster_kind": "prizes", "prizes": [ { "name": "一等奖" } ] }""", "unsupported_mode:upsert")]
    [InlineData("""{ "list_name": "元旦抽奖", "roster_kind": "classes", "prizes": [ { "name": "一等奖" } ] }""", "unsupported_roster_kind:classes")]
    [InlineData("""{ "list_name": "高一（1）班", "roster_kind": "teachers", "students": [ { "id": "01" } ] }""", "unsupported_roster_kind:teachers")]
    public void 奖池下发_非法载荷被拒绝(string json, string expectedReason)
    {
        var parsed = ControlRosterPushRequest.TryParse(Payload(json), out var request, out var reason);

        Assert.False(parsed);
        Assert.Null(request);
        Assert.Equal(expectedReason, reason);
    }

    /// <summary>
    ///     奖池的上限与名单**是同一个数**：控制台对两种名单共用一条行数上限。
    /// </summary>
    /// <remarks>
    ///     两个数字只要分叉，控制台就会发出"本地校验通过、设备判超限"的载荷，
    ///     而那是最难查的一类问题——两边都"没错"。
    /// </remarks>
    [Fact]
    public void 奖池下发_超过奖品条数上限被拒绝()
    {
        Assert.Equal(ControlRosterPushRequest.MaxStudents, ControlRosterPushRequest.MaxPrizes);

        var prizes = string.Join(
            ",",
            Enumerable.Range(1, ControlRosterPushRequest.MaxPrizes + 1)
                .Select(index => $$"""{ "id": "p{{index}}" }"""));

        var parsed = ControlRosterPushRequest.TryParse(
            Payload($$"""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [{{prizes}}] }"""),
            out _, out var reason);

        Assert.False(parsed);
        Assert.Equal($"roster_too_large:{ControlRosterPushRequest.MaxPrizes}", reason);
    }

    // ---------------------------------------------------------------- roster.write（特殊语音）

    /// <summary>
    ///     「特殊语音」三项走与 <c>tags</c> 同一条三态规则：缺失 / <c>null</c> = 本次不下发，
    ///     空串 = 明确清空，非空 = 覆盖。
    /// </summary>
    /// <remarks>
    ///     它比 <c>tags</c> 更容易写错：三项是**同一件事的三个部分**（前缀 + 别名 + 后缀拼成一句播报），
    ///     所以逐字段独立判三态——只给其中一项就只改那一项，而不是"有一个没给就整组作废"。
    /// </remarks>
    [Fact]
    public void 名单下发_特殊语音三个字段被解析()
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload("""
                    { "list_name": "高一（1）班", "mode": "merge",
                      "students": [ { "id": "01", "name": "张三",
                                      "specific_voice_alias": "  张老师  ",
                                      "specific_voice_prefix": "请",
                                      "specific_voice_suffix": "上台" } ] }
                    """),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        Assert.NotNull(request);

        var student = request!.Students[0];

        // 只 trim：协议里纯空白等于空串（清空），与读通道"空串等于没有"是同一条约定。
        Assert.Equal("张老师", student.SpecificVoiceAlias);
        Assert.Equal("请", student.SpecificVoicePrefix);
        Assert.Equal("上台", student.SpecificVoiceSuffix);
        Assert.True(student.SpecificVoice.HasAny);
    }

    [Theory]
    [InlineData("""{ "id": "01" }""")]
    [InlineData("""{ "id": "01", "specific_voice_alias": null, "specific_voice_prefix": null, "specific_voice_suffix": null }""")]
    public void 名单下发_缺省与显式null都是本次未下发特殊语音(string entry)
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload($$"""{ "list_name": "高一", "students": [ {{entry}} ] }"""), out var request, out var reason);

        Assert.True(parsed, reason);

        var student = request!.Students[0];

        Assert.Null(student.SpecificVoiceAlias);
        Assert.Null(student.SpecificVoicePrefix);
        Assert.Null(student.SpecificVoiceSuffix);
        Assert.False(student.SpecificVoice.HasAny);

        // replace 下"没下发"就等于这台设备上没有这行附加设置：不是三个空串，是根本没有这个键。
        Assert.Null(Voice(student.ToStudent()));
    }

    [Fact]
    public void 名单下发_合并模式未下发特殊语音时保留设备上的原值()
    {
        var existing = new StudentList("高一（1）班")
        {
            Students = [new Student { Id = "01", Name = "张三", Tags = "组长" }]
        };

        AttachVoice(existing.Students[0], "旧别名", "旧前缀", "旧后缀");

        // 控制台只改了名字：三个字段一个字都没提。
        var merged = ControlRosterMerge.Merge(
            existing,
            [new ControlRosterStudentInput("01", "张三丰", string.Empty, string.Empty, true)]);

        var settings = Voice(merged[0]);

        Assert.NotNull(settings);
        Assert.Equal("旧别名", settings!.TtsAlias);
        Assert.Equal("旧前缀", settings.Prefix);
        Assert.Equal("旧后缀", settings.Suffix);
        Assert.True(settings.IsAttachSettingsEnabled);
    }

    /// <summary>
    ///     只给一项空串 → 只清那一项；另外两项与开关保持原样（这就是"逐字段判三态"的意思）。
    /// </summary>
    [Fact]
    public void 名单下发_合并模式空串只清空给到的那一项()
    {
        var existing = new StudentList("高一（1）班")
        {
            Students = [new Student { Id = "01", Name = "张三" }]
        };

        AttachVoice(existing.Students[0], "旧别名", "旧前缀", "旧后缀");

        var merged = ControlRosterMerge.Merge(
            existing,
            [new ControlRosterStudentInput("01", "张三", string.Empty, string.Empty, true, null, string.Empty)]);

        var settings = Voice(merged[0]);

        Assert.NotNull(settings);
        Assert.Equal(string.Empty, settings!.TtsAlias);
        Assert.Equal("旧前缀", settings.Prefix);
        Assert.Equal("旧后缀", settings.Suffix);

        // 还剩有效内容（前缀），开关必须继续开着，否则"清了个别名"顺手把播报关了。
        Assert.True(settings.IsAttachSettingsEnabled);
    }

    /// <summary>
    ///     三项都清空 → 开关跟着关掉，读通道于是什么也不报（否则会回一组空值给控制台）。
    /// </summary>
    [Fact]
    public void 名单下发_三项都清空后开关关掉且读通道什么都不报()
    {
        var existing = new StudentList("高一（1）班")
        {
            Students = [new Student { Id = "01", Name = "张三" }]
        };

        AttachVoice(existing.Students[0], "旧别名", "旧前缀", "旧后缀");

        var merged = ControlRosterMerge.Merge(
            existing,
            [
                new ControlRosterStudentInput(
                    "01", "张三", string.Empty, string.Empty, true, null, string.Empty, string.Empty, string.Empty)
            ]);

        var settings = Voice(merged[0]);

        Assert.NotNull(settings);
        Assert.False(settings!.IsAttachSettingsEnabled);

        var member = ControlRosterMemberPayload.FromStudent(merged[0]);

        Assert.Null(member.SpecificVoiceAlias);
        Assert.Null(member.SpecificVoicePrefix);
        Assert.Null(member.SpecificVoiceSuffix);
    }

    /// <summary>写进去的三项要能原样读回来——同一条 <c>SettingsId</c>，同一个附加设置对象。</summary>
    [Fact]
    public void 名单下发_写进去的特殊语音能被读通道原样读回()
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload("""
                    { "list_name": "高一（1）班", "mode": "replace",
                      "students": [ { "id": "01", "name": "张三",
                                      "specific_voice_alias": "张老师",
                                      "specific_voice_prefix": "请",
                                      "specific_voice_suffix": "上台" } ] }
                    """),
            out var request,
            out var reason);

        Assert.True(parsed, reason);

        var member = ControlRosterMemberPayload.FromStudent(request!.Students[0].ToStudent());

        Assert.Equal("张老师", member.SpecificVoiceAlias);
        Assert.Equal("请", member.SpecificVoicePrefix);
        Assert.Equal("上台", member.SpecificVoiceSuffix);
    }

    [Theory]
    [InlineData("""{ "id": "01", "specific_voice_alias": 7 }""")]
    [InlineData("""{ "id": "01", "specific_voice_prefix": [ "请" ] }""")]
    [InlineData("""{ "id": "01", "specific_voice_suffix": { "a": 1 } }""")]
    [InlineData("""{ "id": "01", "specific_voice_alias": true }""")]
    public void 名单下发_特殊语音给了非字符串整条拒绝(string entry)
    {
        // 与 tags 同一条理由：认得的字段给了坏值就整条拒绝，不能悄悄降级成"没下发"——
        // 那会让控制台以为写进去了，设备上却什么都没变。
        var parsed = ControlRosterPushRequest.TryParse(
            Payload($$"""{ "list_name": "高一", "students": [ {{entry}} ] }"""), out var request, out var reason);

        Assert.False(parsed);
        Assert.Null(request);
        Assert.Equal("invalid_command", reason);
    }

    /// <summary>
    ///     奖池那侧用的是**同一组字段名、同一条三态规则**（特殊语音是两种名单共用的附加设置）。
    /// </summary>
    [Fact]
    public void 奖池下发_特殊语音与学生那侧同一套语义()
    {
        var existing = new PrizeList("元旦抽奖")
        {
            Prizes = [new Prize { Id = "p1", Name = "一等奖", Count = 2, Weight = 1 }]
        };

        AttachVoice(existing.Prizes[0], "旧别名", "旧前缀", "旧后缀");

        var parsed = ControlRosterPushRequest.TryParse(
            Payload("""
                    { "list_name": "元旦抽奖", "roster_kind": "prizes", "mode": "merge",
                      "prizes": [ { "id": "p1", "name": "特等奖", "count": 3, "weight": 1,
                                    "specific_voice_alias": "张老师" },
                                  { "id": "p2", "name": "二等奖" } ] }
                    """),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        Assert.NotNull(request);

        var merged = ControlRosterMerge.MergePrizes(existing, request!.Prizes!);

        // 下发了一项：改名 + 换别名，前缀后缀没提就留着。
        var first = Voice(merged[0]);
        Assert.NotNull(first);
        Assert.Equal("特等奖", merged[0].Name);
        Assert.Equal("张老师", first!.TtsAlias);
        Assert.Equal("旧前缀", first.Prefix);
        Assert.Equal("旧后缀", first.Suffix);

        // 一个字都没提的：连附加设置都不该被建出来。
        Assert.Null(Voice(merged[1]));
    }

    [Theory]
    [InlineData("""{ "name": "一等奖", "specific_voice_alias": 7 }""", "invalid_command")]
    [InlineData("""{ "name": "一等奖", "specific_voice_prefix": { "a": 1 } }""", "invalid_command")]
    public void 奖池下发_特殊语音给了非字符串整条拒绝(string entry, string expectedReason)
    {
        var parsed = ControlRosterPushRequest.TryParse(
            Payload($$"""
                     { "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [ {{entry}} ] }
                     """),
            out _,
            out var reason);

        Assert.False(parsed);
        Assert.Equal(expectedReason, reason);
    }

    /// <summary>特殊语音在 <c>AttachedObjects</c> 里的键必须与控件注册的 Guid 逐字相同。</summary>
    /// <remarks>
    ///     键一旦对不上，读写两边都会"成功"，只是读出来永远是"这个人没设置过"——
    ///     一个既没有异常也没有日志的静默失效。
    /// </remarks>
    [Fact]
    public void 特殊语音的存储键就是控件声明的那个Guid()
    {
        Assert.Equal(
            "10F2C686-07D7-47E7-9A4F-B7A4724A6A10",
            ControlSpecificVoiceValues.SettingsId.ToString().ToUpperInvariant());
        Assert.Equal(
            Guid.Parse(GlobalConstants.SpecificAnnouncementAttachedSettings),
            ControlSpecificVoiceValues.SettingsId);
    }

    // ---------------------------------------------------------------- 载荷字节预算

    /// <summary>
    ///     构造一份名单/奖池载荷：<paramref name="students" /> 条，每条的姓名是
    ///     <paramref name="nameLength" /> 个中文字加两位序号。
    /// </summary>
    private static string RosterJson(int students, int nameLength, bool prizes = false)
    {
        var name = new string('同', nameLength);
        var entries = string.Join(
            ",",
            Enumerable.Range(1, students).Select(index =>
                $$"""{ "id": "{{index:0000}}", "name": "{{name}}{{index:00}}" }"""));

        return prizes
            ? $$"""{ "list_name": "元旦抽奖", "roster_kind": "prizes", "prizes": [{{entries}}] }"""
            : $$"""{ "list_name": "高一（1）班", "students": [{{entries}}] }""";
    }

    /// <summary>断言原因码就是 <c>payload_too_large:&lt;bytes&gt;:&lt;budget&gt;</c>，且两个数都对得上。</summary>
    private static void AssertPayloadTooLarge(string json, string reason)
    {
        Assert.StartsWith("payload_too_large:", reason);

        var parts = reason.Split(':');
        Assert.Equal(3, parts.Length);

        // 报出的必须是**真实序列化字节数**：控制台据此判断这份数据比一帧大多少。
        var bytes = int.Parse(parts[1]);
        Assert.Equal(Encoding.UTF8.GetByteCount(json), bytes);
        Assert.True(bytes > ControlProtocolJson.PayloadBudgetBytes);
        Assert.Equal(ControlProtocolJson.PayloadBudgetBytes, int.Parse(parts[2]));
    }

    /// <summary>
    ///     超预算的名单载荷必须在**解析成员之前**被拒。
    /// </summary>
    /// <remarks>
    ///     这不是"操作失败"而是更糟：超限帧在客户端传输层会让整条集控连接被断开
    ///     （连一个 <c>command.result</c> 都回不去），控制台看到的是"设备掉线"。
    ///     所以设备侧必须在解析前就量出"这份数据装不进一帧"。
    /// </remarks>
    [Fact]
    public void 名单下发_超字节预算被拒绝()
    {
        // 2000 人 × 60 字姓名：约 300 KB，是 60 KiB 预算的四倍以上——人数上限是拦不住它的。
        var json = RosterJson(students: 2000, nameLength: 60);

        var parsed = ControlRosterPushRequest.TryParse(Payload(json), out var request, out var reason);

        Assert.False(parsed);
        Assert.Null(request);
        AssertPayloadTooLarge(json, reason);
    }

    /// <summary>奖池走的是同一条命令、同一个闸门：奖品名一长，60 条的奖池也会超限。</summary>
    [Fact]
    public void 奖池下发_超字节预算被拒绝()
    {
        var json = RosterJson(students: 2000, nameLength: 60, prizes: true);

        var parsed = ControlRosterPushRequest.TryParse(Payload(json), out var request, out var reason);

        Assert.False(parsed);
        Assert.Null(request);
        AssertPayloadTooLarge(json, reason);
    }

    /// <summary>
    ///     正常班级规模必须照常通过：字节闸门不是拿来挡正常用法的。
    /// </summary>
    [Fact]
    public void 名单下发_正常班级规模在预算内通过()
    {
        var json = RosterJson(students: 60, nameLength: 3);

        Assert.True(
            Encoding.UTF8.GetByteCount(json) < ControlProtocolJson.PayloadBudgetBytes,
            "60 人的正常班级必须远在预算之内，否则这条用例的断言方向就反了");

        var parsed = ControlRosterPushRequest.TryParse(Payload(json), out var request, out var reason);

        Assert.True(parsed, reason);
        Assert.Equal(60, request!.Students.Count);
    }

    /// <summary>
    ///     一条超大的 <c>settings.write</c> patch 同样发不出去：同一条投递通道、同一个上限。
    /// </summary>
    [Fact]
    public void 设置白名单_超字节预算的patch被拒绝()
    {
        // 用一段超长字符串把整份 patch 顶过预算（真实设置项本身都有范围校验，撑不了这么大）。
        var filler = new string('x', ControlProtocolJson.PayloadBudgetBytes);
        var json = $$"""{ "patch": { "voice.volume": 30 }, "filler": "{{filler}}" }""";

        var planned = ControlSettingsWhitelist.TryPlan(Payload(json), out var changes, out var reason);

        Assert.False(planned);
        Assert.Empty(changes);
        AssertPayloadTooLarge(json, reason);
    }

    private static void AttachVoice(IAttachableSettingsObject target, string alias, string prefix, string suffix) =>
        target.AttachedObjects[ControlSpecificVoiceValues.SettingsId] = new SpecificAnnouncementAttachedSettings
        {
            IsAttachSettingsEnabled = true,
            TtsAlias = alias,
            Prefix = prefix,
            Suffix = suffix
        };

    private static SpecificAnnouncementAttachedSettings? Voice(IAttachableSettingsObject target) =>
        target.GetAttachedObject<SpecificAnnouncementAttachedSettings>(ControlSpecificVoiceValues.SettingsId);
}
