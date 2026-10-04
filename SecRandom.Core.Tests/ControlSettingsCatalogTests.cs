using System.Text.Json;
using SecRandom.Core.Models;
using SecRandom.Core.Services.ControlNode;

namespace SecRandom.Core.Tests;

/// <summary>
///     集控设置目录（读设置表单 + <c>settings.write</c> 校验）的行为。
/// </summary>
/// <remarks>
///     这些断言钉在"远程能看到什么、能改什么"的边界上：目录是反射推出来的，属性一多，最容易出的错
///     不是崩溃而是**悄悄多出一项可写设置**。因此这里既测正常读写，也逐条测排除面。
/// </remarks>
public sealed class ControlSettingsCatalogTests
{
    /// <summary>已经发到控制台的 9 条路径：改名或漏掉都会让旧控制台的 patch 全部被拒。</summary>
    private static readonly string[] ShippedPaths =
    [
        "roll_call.half_repeat",
        "quick_draw.disable_after_click",
        "lottery.half_repeat",
        "notification.roll_call.enabled",
        "notification.quick_draw.enabled",
        "notification.lottery.enabled",
        "voice.enable",
        "voice.volume",
        "voice.speech_rate"
    ];

    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ---------------------------------------------------------------- 读

    [Fact]
    public void 读取_新鲜配置给出足够多的字段与全部已知类目()
    {
        var model = new MainConfigModel();
        var described = ControlSettingsCatalog.Describe(model);

        Assert.True(
            described.Sum(category => category.Fields.Count) > 50,
            $"设置目录只有 {described.Sum(category => category.Fields.Count)} 个字段，读取能力等于没用");

        string[] expectedCategories =
        [
            "float_position", "general", "appearance", "fair_draw", "default_draw", "roll_call", "quick_draw",
            "lottery", "floating_window", "notification", "security", "linkage", "voice", "history", "update", "more"
        ];

        Assert.Equal(expectedCategories, described.Select(category => category.Id).ToArray());

        // 字段按路径字母序，类目内的顺序不随属性摆放位置变化。
        foreach (var category in described)
        {
            Assert.Equal(
                category.Fields.Select(field => field.Path).Order(StringComparer.Ordinal).ToArray(),
                category.Fields.Select(field => field.Path).ToArray());

            Assert.All(category.Fields, field => Assert.Equal(category.Id, field.Category));
        }
    }

    [Fact]
    public void 读取_值就是这台机器上的当前值且类型合法()
    {
        var model = new MainConfigModel();
        var fields = ControlSettingsCatalog.Describe(model)
            .SelectMany(category => category.Fields)
            .ToDictionary(field => field.Path, StringComparer.Ordinal);

        Assert.Equal(model.VoiceSettings.VolumeSize, fields["voice.volume"].Value);
        Assert.Equal("int", fields["voice.volume"].Type);
        Assert.Equal(0d, fields["voice.volume"].Min);
        Assert.Equal(100d, fields["voice.volume"].Max);

        Assert.Equal(model.VoiceSettings.VoiceEnable, fields["voice.enable"].Value);
        Assert.Equal(model.RollCallSettings.HalfRepeat, fields["roll_call.half_repeat"].Value);
        Assert.Equal(model.NotificationSettings.QuickDraw.Enabled, fields["notification.quick_draw.enabled"].Value);

        foreach (var field in fields.Values)
            Assert.Contains(field.Type, new[] { "bool", "int", "double", "string", "enum" });
    }

    [Fact]
    public void 读取_通知类目只暴露渠道开关与显示时长()
    {
        var fields = ControlSettingsCatalog.Describe(new MainConfigModel())
            .Single(category => category.Id == "notification")
            .Fields
            .Select(field => field.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // 渠道的窗口位置、透明度、服务类型也挂在同一个对象上，但它们属于这台机器的通知弹窗外观，
        // 远程改只会让弹窗位置对不上，因此不进目录。
        string[] expectedFields =
        [
            "notification.default.display_duration", "notification.default.enabled",
            "notification.lottery.display_duration", "notification.lottery.enabled",
            "notification.quick_draw.display_duration", "notification.quick_draw.enabled",
            "notification.roll_call.display_duration", "notification.roll_call.enabled"
        ];

        Assert.Equal(expectedFields, fields);
    }

    [Fact]
    public void 序列化_字段名是控制台消费的snake_case()
    {
        var described = ControlSettingsCatalog.Describe(new MainConfigModel());
        var volume = described.Single(category => category.Id == "voice")
            .Fields
            .Single(field => field.Path == "voice.volume");

        var json = JsonSerializer.Serialize(volume);

        Assert.Contains("\"path\":\"voice.volume\"", json);
        Assert.Contains("\"category\":\"voice\"", json);
        Assert.Contains("\"type\":\"int\"", json);
        Assert.Contains("\"writable\":true", json);
        Assert.Contains("\"min\":0", json);
        Assert.Contains("\"max\":100", json);
        Assert.DoesNotContain("\"Path\"", json);

        // 标签与说明也在同一条字段上：控制台的每一行既要知道改什么，也要知道这一行叫什么。
        Assert.Contains("\"label\":", json);
        Assert.Contains("\"description\":", json);
        Assert.Equal(ControlSettingsLabels.GetFieldLabel("voice.volume"), volume.Label);
        Assert.Equal(ControlSettingsLabels.GetFieldDescription("voice.volume"), volume.Description);
        Assert.False(string.IsNullOrWhiteSpace(volume.Label));

        // 控制台拿到的整体形状就是
        // { "categories": [ { "id": …, "label": …, "description": …, "fields": [ … ] } ] }。
        var envelope = JsonSerializer.Serialize(new { categories = described });
        Assert.StartsWith("{\"categories\":[{\"id\":\"float_position\",\"label\":\"", envelope);
        Assert.Contains("\",\"description\":null,\"fields\":[", envelope);
    }

    // ---------------------------------------------------------------- settings.write 的既有契约

    [Fact]
    public void 白名单_九条已发布路径仍然存在且可写()
    {
        var writable = ControlSettingsCatalog.WritablePaths.ToHashSet(StringComparer.Ordinal);

        foreach (var path in ShippedPaths)
            Assert.Contains(path, writable);

        // 旧的壳只是转发：两边的清单必须逐字一致，否则控制台拿到的清单和实际能改的不是一回事。
        Assert.Equal(
            ControlSettingsCatalog.WritablePaths.Order(StringComparer.Ordinal).ToArray(),
            ControlSettingsWhitelist.WritablePaths.Order(StringComparer.Ordinal).ToArray());

        // 已发布路径同时是控制台上最早出现的那批控件：加上标签/说明以后它们也得有名字，
        // 否则旧控制台看到的是一排突然变成空白的设置。
        var fields = ControlSettingsCatalog.Describe(new MainConfigModel())
            .SelectMany(category => category.Fields)
            .Where(field => ShippedPaths.Contains(field.Path, StringComparer.Ordinal))
            .ToList();

        Assert.Equal(ShippedPaths.Length, fields.Count);
        Assert.All(fields, field =>
        {
            Assert.True(field.Writable);
            Assert.False(string.IsNullOrWhiteSpace(field.Label));
        });
    }

    [Fact]
    public void 白名单_已发布路径的限值保持原样()
    {
        var fields = ControlSettingsCatalog.Describe(new MainConfigModel())
            .SelectMany(category => category.Fields)
            .ToDictionary(field => field.Path, StringComparer.Ordinal);

        Assert.Equal(1d, fields["roll_call.half_repeat"].Min);
        Assert.Equal(20d, fields["roll_call.half_repeat"].Max);
        Assert.Equal(1d, fields["lottery.half_repeat"].Min);
        Assert.Equal(20d, fields["lottery.half_repeat"].Max);
        Assert.Equal(1d, fields["quick_draw.disable_after_click"].Min);
        Assert.Equal(20d, fields["quick_draw.disable_after_click"].Max);
        Assert.Equal(50d, fields["voice.speech_rate"].Min);
        Assert.Equal(200d, fields["voice.speech_rate"].Max);

        // 没有已知限值的字段不编一个出来：控制台会把它当成"没有上下限"。
        Assert.Null(fields["appearance.font"].Min);
        Assert.Null(fields["floating_window.floating_window_opacity"].Min);
    }

    // ---------------------------------------------------------------- 排除面

    [Fact]
    public void 排除面_被排除的设置都不在可写清单里()
    {
        var described = ControlSettingsCatalog.Describe(new MainConfigModel());
        var writable = described.SelectMany(category => category.Fields)
            .Where(field => field.Writable)
            .Select(field => field.Path)
            .ToHashSet(StringComparer.Ordinal);

        // Describe 里标着可写的，与 WritablePaths 必须完全一致。
        Assert.Equal(
            ControlSettingsCatalog.WritablePaths.Order(StringComparer.Ordinal).ToArray(),
            writable.Order(StringComparer.Ordinal).ToArray());

        // 段级封禁：路径按 "." 拆开后，任一段与这些词完全相等就只读。
        // 这里刻意断言**段**而不是子串：子串匹配曾经把 voice.system_volume_control 和
        // more.*_control_panel_position 一起判成只读，它们跟设备所有权没有半点关系。
        foreach (var path in writable)
        {
            foreach (var segment in path.Split('.'))
            {
                Assert.DoesNotContain(
                    segment,
                    new[] { "security", "control", "update", "backup", "autostart", "protocol" });
            }
        }

        // 整类只读：安全、更新（以及通用下面的备份、证明留存）照常描述，但一个可写字段都没有。
        foreach (var categoryId in new[] { "security", "update" })
        {
            var fields = described.Single(category => category.Id == categoryId).Fields;
            Assert.NotEmpty(fields);
            Assert.All(fields, field => Assert.False(field.Writable));
        }

        foreach (var prefix in new[] { "general.backup.", "general.proof_retention." })
        {
            var fields = described.SelectMany(category => category.Fields)
                .Where(field => field.Path.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();

            Assert.NotEmpty(fields);
            Assert.All(fields, field => Assert.False(field.Writable));
        }

        // 桌面集成、隐藏配置、证明留存单独点名的字段同样只读。
        foreach (var path in new[]
                 {
                     "general.basic.autostart",
                     "general.basic.url_protocol",
                     "general.basic.main_window_topmost_mode",
                     "floating_window.floating_window_topmost_mode",
                     "general.basic.background_resident",
                     "general.basic.show_startup_window",
                     "general.basic.guide_completed",
                     "general.basic.accepted_privacy_policy_version",
                     "general.proof_retention.retention_days"
                 })
        {
            var field = described.SelectMany(category => category.Fields).Single(item => item.Path == path);
            Assert.False(field.Writable);
            Assert.DoesNotContain(path, writable);
        }

        // 描述不到的凭据类设置（如果有）也永远不该出现——这里钉住"目录里没有密码字段"。
        Assert.DoesNotContain(
            writable,
            path => path.Contains("password", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("credential", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("key", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     名字里带 <c>control</c>、但跟集控无关的设置必须保持可写。
    /// </summary>
    /// <remarks>
    ///     它们曾经被子串规则误伤：路径里出现 "control" 就整条只读，于是"系统音量控制"和
    ///     "控制面板在左还是在右"这两类纯本机偏好被当成设备所有权挡住，管理员只会看到"设备不允许改"。
    ///     这条断言钉住的是**判定口径**（按 "." 拆段后整段相等），不是这三个路径本身：
    ///     以后再有带 control 字样的普通设置，只要它不是单独成段，就不该被拦。
    /// </remarks>
    [Fact]
    public void 排除面_与集控无关的control字样设置保持可写()
    {
        string[] mustStayWritable =
        [
            "more.roll_call_control_panel_position",
            "more.lottery_control_panel_position",
            "voice.system_volume_control"
        ];

        var writable = ControlSettingsCatalog.WritablePaths.ToHashSet(StringComparer.Ordinal);
        foreach (var path in mustStayWritable)
            Assert.Contains(path, writable);

        var fields = ControlSettingsCatalog.Describe(new MainConfigModel())
            .SelectMany(category => category.Fields)
            .ToDictionary(field => field.Path, StringComparer.Ordinal);

        foreach (var path in mustStayWritable)
        {
            Assert.True(fields[path].Writable, $"{path} 又被误判成只读了");
            Assert.False(string.IsNullOrWhiteSpace(fields[path].Label));
        }

        // 可写不等于"整类开放"：安全与更新这两个类目仍然一个可写字段都没有，
        // 备份与证明留存同样照旧。
        foreach (var categoryId in new[] { "security", "update" })
            Assert.All(
                ControlSettingsCatalog.Describe(new MainConfigModel())
                    .Single(category => category.Id == categoryId)
                    .Fields,
                field => Assert.False(field.Writable));

        // 而且真的写得进去：校验与应用走一遍，确认放开的不只是清单上的一个字符串。
        var planned = ControlSettingsCatalog.TryPlan(
            Payload("""{ "patch": { "voice.system_volume_control": true, "more.roll_call_control_panel_position": "Left" } }"""),
            out var changes,
            out var reason);

        Assert.True(planned, reason);
        Assert.Equal(2, changes.Count);

        var model = new MainConfigModel();
        model.VoiceSettings.SystemVolumeControl = false;
        ControlSettingsCatalog.Apply(model, changes);

        Assert.True(model.VoiceSettings.SystemVolumeControl);
    }

    // ---------------------------------------------------------------- 写：校验
    [Fact]
    public void 写入_未知路径整体拒绝并带回具体路径()
    {
        var planned = ControlSettingsCatalog.TryPlan(
            Payload("""{ "patch": { "voice.volume": 50, "voice.mute": true } }"""),
            out var changes,
            out var reason);

        Assert.False(planned);
        Assert.Empty(changes);
        Assert.Equal("not_writable:voice.mute", reason);
    }

    [Fact]
    public void 写入_被描述但只读的路径同样拒绝()
    {
        // 控制台会从读取结果里看到 security.* 这些字段，但写回来必须被挡住。
        var planned = ControlSettingsCatalog.TryPlan(
            Payload("""{ "patch": { "security.security_enabled": false } }"""),
            out var changes,
            out var reason);

        Assert.False(planned);
        Assert.Empty(changes);
        Assert.Equal("not_writable:security.security_enabled", reason);
    }

    [Theory]
    // 类型不对
    [InlineData("""{ "patch": { "voice.volume": "大" } }""", "invalid_value:voice.volume:type_mismatch")]
    [InlineData("""{ "patch": { "voice.enable": "true" } }""", "invalid_value:voice.enable:type_mismatch")]
    [InlineData("""{ "patch": { "roll_call.default_class": 7 } }""", "invalid_value:roll_call.default_class:type_mismatch")]
    [InlineData("""{ "patch": { "appearance.theme": 3 } }""", "invalid_value:appearance.theme:type_mismatch")]
    [InlineData("""{ "patch": { "appearance.theme": "NoSuchTheme" } }""", "invalid_value:appearance.theme:type_mismatch")]
    // 越界
    [InlineData("""{ "patch": { "voice.volume": 101 } }""", "invalid_value:voice.volume:out_of_range:0..100")]
    [InlineData("""{ "patch": { "voice.volume": -1 } }""", "invalid_value:voice.volume:out_of_range:0..100")]
    [InlineData("""{ "patch": { "roll_call.half_repeat": 0 } }""", "invalid_value:roll_call.half_repeat:out_of_range:1..20")]
    [InlineData("""{ "patch": { "quick_draw.disable_after_click": 21 } }""", "invalid_value:quick_draw.disable_after_click:out_of_range:1..20")]
    [InlineData("""{ "patch": { "voice.speech_rate": 10 } }""", "invalid_value:voice.speech_rate:out_of_range:50..200")]
    public void 写入_非法取值整体拒绝且给出路径与原因(string json, string expectedReason)
    {
        var planned = ControlSettingsCatalog.TryPlan(Payload(json), out var changes, out var reason);

        Assert.False(planned);
        Assert.Empty(changes);
        Assert.Equal(expectedReason, reason);
    }

    [Theory]
    [InlineData("""{ "patch": {} }""")]
    [InlineData("""{ "patch": [] }""")]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    [InlineData("""{ "patch": "voice.volume" }""")]
    public void 写入_空patch与非对象载荷算无效命令(string json)
    {
        var planned = ControlSettingsCatalog.TryPlan(Payload(json), out var changes, out var reason);

        Assert.False(planned);
        Assert.Empty(changes);
        Assert.Equal("invalid_command", reason);
    }

    // ---------------------------------------------------------------- 写：应用

    [Fact]
    public void 写入_合法patch真的改到模型上()
    {
        var planned = ControlSettingsCatalog.TryPlan(
            Payload("""{ "patch": { "voice.volume": 30, "roll_call.half_repeat": 3 } }"""),
            out var changes,
            out var reason);

        Assert.True(planned, reason);
        Assert.Equal(2, changes.Count);

        var model = new MainConfigModel();
        ControlSettingsCatalog.Apply(model, changes);

        Assert.Equal(30, model.VoiceSettings.VolumeSize);
        Assert.Equal(3, model.RollCallSettings.HalfRepeat);

        // 再读一遍：目录立刻反映刚写进去的值。
        var fields = ControlSettingsCatalog.Describe(model)
            .SelectMany(category => category.Fields)
            .ToDictionary(field => field.Path, StringComparer.Ordinal);

        Assert.Equal(30, fields["voice.volume"].Value);
        Assert.Equal(3, fields["roll_call.half_repeat"].Value);
    }

    [Fact]
    public void 写入_枚举按成员名往返()
    {
        var model = new MainConfigModel();
        var field = ControlSettingsCatalog.Describe(model)
            .SelectMany(category => category.Fields)
            .First(item => item.Type == "enum");

        Assert.NotNull(field.Options);
        Assert.NotEmpty(field.Options!);
        Assert.Contains(field.Value as string, field.Options!);

        var target = field.Options![^1];
        var planned = ControlSettingsCatalog.TryPlan(
            Payload($$"""{ "patch": { "{{field.Path}}": "{{target}}" } }"""),
            out var changes,
            out var reason);

        Assert.True(planned, reason);

        ControlSettingsCatalog.Apply(model, changes);

        var after = ControlSettingsCatalog.Describe(model)
            .SelectMany(category => category.Fields)
            .Single(item => item.Path == field.Path);

        Assert.Equal(target, after.Value);
    }

    [Fact]
    public void 写入_绕过校验直接拼变更也写不进只读设置()
    {
        // 防御纵深：即使调用方不经过 TryPlan，只读路径也不该被 Apply 认下来。
        var model = new MainConfigModel();
        var before = model.SecuritySettings.SecurityEnabled;

        ControlSettingsCatalog.Apply(model, [new ControlSettingsChange("security.security_enabled", true)]);

        Assert.Equal(before, model.SecuritySettings.SecurityEnabled);
    }
}
