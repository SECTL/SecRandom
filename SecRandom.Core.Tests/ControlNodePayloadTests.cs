using System.Text.Json;
using SecRandom.Core.Models;
using SecRandom.Core.Services.ControlNode;
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
    ///     安全设置、集控自身设置、桌面集成、更新设置**永远**不在白名单里。
    ///     这条断言是防回归的：将来有人"顺手"把某项加进去，这里会直接红。
    /// </summary>
    [Fact]
    public void 设置白名单_永远不包含安全集控桌面集成与更新()
    {
        foreach (var path in ControlSettingsWhitelist.WritablePaths)
        {
            Assert.DoesNotContain("security", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("control", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("update", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("autostart", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("protocol", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("backup", path, StringComparison.OrdinalIgnoreCase);
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
            [new Student { Id = "01", Name = "张三丰", Group = "B" }, new Student { Id = "03", Name = "王五" }]);

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
        var merged = ControlRosterMerge.Merge(null, [new Student { Id = "01", Name = "张三" }]);

        Assert.Single(merged);
    }

    [Theory]
    [InlineData("""{ "list_name": "  ", "students": [ { "id": "01" } ] }""", "invalid_list_name")]
    [InlineData("""{ "list_name": "高一", "students": [] }""", "empty_roster")]
    [InlineData("""{ "list_name": "高一", "students": [ { "id": "" } ] }""", "empty_roster")]
    [InlineData("""{ "list_name": "高一", "students": "01,02" }""", "invalid_command")]
    [InlineData("""{ "students": [ { "id": "01" } ] }""", "invalid_command")]
    [InlineData("""{ "list_name": "高一", "mode": "upsert", "students": [ { "id": "01" } ] }""", "unsupported_mode:upsert")]
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
}
