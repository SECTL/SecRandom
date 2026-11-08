using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Models;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.ControlNode.V4;

namespace SecRandom.Core.Tests;

/// <summary>
///     v4.2 设置 schema 的导出、revision 单调性与 <c>settings.read</c>/<c>settings.write</c> 的载荷契约。
/// </summary>
/// <remarks>
///     这些断言钉住"控制台看到的表单"与"设备真正接受的写入"是同一份 schema：
///     键集合一旦分叉，控制台会渲染出设备拒绝的控件，或者设备允许改的项在表单里根本不存在。
/// </remarks>
public sealed class ControlSettingsSchemaTests
{
    private static readonly Regex KeyPattern =
        new(@"^[a-z][a-z0-9_]*(?:[._][a-z0-9_]+)*$", RegexOptions.CultureInvariant);

    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static ControlSettingsSchemaResult Build(MainConfigModel model) =>
        ControlSettingsSchemaBuilder.Build(model);

    // ---------------------------------------------------------------- schema 形状

    [Fact]
    public void schema_键与类型合法且整份在上限内()
    {
        var model = new MainConfigModel();
        var result = Build(model);

        Assert.Empty(result.Issues);
        Assert.InRange(result.Schema.Count, 1, ControlSettingsSchemaBuilder.MaxKeys);

        var groups = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (path, item) in result.Schema)
        {
            Assert.Matches(KeyPattern, path);
            Assert.InRange(path.Length, 1, ControlSettingsSchemaBuilder.MaxKeyChars);
            Assert.Contains(item.Type, new[] { "bool", "enum", "int", "duration" });
            Assert.False(string.IsNullOrWhiteSpace(item.Label));
            Assert.InRange(CountCodePoints(item.Label), 1, ControlSettingsSchemaBuilder.MaxLabelCodePoints);

            if (item.Group is { Length: > 0 } group)
            {
                Assert.InRange(CountCodePoints(group), 1, ControlSettingsSchemaBuilder.MaxLabelCodePoints);
                groups.Add(group);
            }

            if (item.Hint is { Length: > 0 } hint)
                Assert.InRange(CountCodePoints(hint), 1, ControlSettingsSchemaBuilder.MaxHintCodePoints);

            switch (item.Type)
            {
                case "bool":
                    Assert.IsType<bool>(item.Default);
                    break;

                case "enum":
                    Assert.NotNull(item.Values);
                    Assert.InRange(item.Values!.Count, 1, ControlSettingsSchemaBuilder.MaxEnumValues);
                    Assert.Contains(item.Default as string, item.Values!);
                    break;

                case "int":
                    Assert.IsType<int>(item.Default);
                    Assert.NotNull(item.Min);
                    Assert.NotNull(item.Max);
                    Assert.True(item.Min <= item.Max);
                    Assert.InRange((int)item.Default!, (int)item.Min!.Value, (int)item.Max!.Value);
                    break;

                case "duration":
                    Assert.IsType<int>(item.Default);
                    Assert.Equal(ControlSettingsSchemaBuilder.UnitSeconds, item.Unit);
                    Assert.NotNull(item.Min);
                    Assert.NotNull(item.Max);
                    Assert.InRange((int)item.Default!, (int)item.Min!.Value, (int)item.Max!.Value);
                    break;
            }
        }

        Assert.InRange(groups.Count, 1, ControlSettingsSchemaBuilder.MaxGroups);

        var json = ControlSettingsSchemaBuilder.Serialize(result.Schema);
        Assert.InRange(Encoding.UTF8.GetByteCount(json), 1, ControlSettingsSchemaBuilder.MaxSchemaBytes);
    }

    [Fact]
    public void schema_只收老师会远程改的项而排除本机开关与身份()
    {
        var result = Build(new MainConfigModel());
        var paths = result.Schema.Keys.ToHashSet(StringComparer.Ordinal);

        // 产品承诺会出现的键：抽取方式、半重复、语音、通知。
        foreach (var path in new[]
                 {
                     "default_draw.draw_mode",
                     "roll_call.half_repeat",
                     "quick_draw.disable_after_click",
                     "lottery.draw_mode",
                     "voice.enable",
                     "voice.volume",
                     "voice.speech_rate",
                     "notification.roll_call.enabled",
                     "notification.roll_call.display_duration"
                 })
        {
            Assert.Contains(path, paths);
        }

        // 本机开关、身份、密钥、路径与纯外观字段一律不出现。
        Assert.DoesNotContain(paths, path => path.Contains("remote", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paths, path => path.Contains("node", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paths, path => path.Contains("server", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paths, path => path.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paths, path => path.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paths, path => path.StartsWith("general.", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, path => path.StartsWith("floating_window.", StringComparison.Ordinal));
        Assert.DoesNotContain("voice.voice_engine", paths);

        // 通知时长按 duration 表达（秒），而不是没有单位的普通整数。
        var duration = result.Schema["notification.quick_draw.display_duration"];
        Assert.Equal("duration", duration.Type);
        Assert.Equal("seconds", duration.Unit);
        Assert.Equal(1d, duration.Min);
        Assert.Equal(60d, duration.Max);
    }

    [Fact]
    public void schema_枚举默认值是成员名且始终在可选值内()
    {
        var model = new MainConfigModel();
        model.DefaultDrawSettings.DrawMode = SecRandom.Core.Enums.Configs.DrawMode.Repeat;

        var result = Build(model);
        var drawMode = result.Schema["default_draw.draw_mode"];

        Assert.Equal("enum", drawMode.Type);
        Assert.NotNull(drawMode.Values);
        Assert.Contains("Repeat", drawMode.Values!);
        Assert.Equal("Repeat", drawMode.Default);
    }

    // ---------------------------------------------------------------- revision

    [Fact]
    public void revision_内容不变复用_内容变化加一_重启不回退()
    {
        var model = new MainConfigModel();
        var store = new MemoryStateStore();
        var source = new ControlSettingsReportSource(
            () => model, store, NullLogger<ControlSettingsReportSource>.Instance);

        var first = source.Build();
        Assert.NotNull(first);
        Assert.Equal(1, first!.Revision);

        var second = source.Build();
        Assert.NotNull(second);
        Assert.Equal(first.Revision, second!.Revision);

        // 改一项就会改变 schema 里的 default 内容，因此下一个 revision。
        model.VoiceSettings.VolumeSize = 35;
        var changed = source.Build();
        Assert.NotNull(changed);
        Assert.Equal(first.Revision + 1, changed!.Revision);
        Assert.Equal(
            ControlSettingsSchemaBuilder.ComputeHash(changed.Schema),
            store.Current.SettingsSchemaHash);

        // "重启"=新建一个 report source 读同一份持久化状态：内容没变，不能从 1 重数。
        var restarted = new ControlSettingsReportSource(
            () => model, store, NullLogger<ControlSettingsReportSource>.Instance);
        var afterRestart = restarted.Build();
        Assert.NotNull(afterRestart);
        Assert.Equal(changed.Revision, afterRestart!.Revision);

        // 再改一项继续 +1。
        model.VoiceSettings.VolumeSize = 36;
        var next = restarted.Build();
        Assert.NotNull(next);
        Assert.Equal(changed.Revision + 1, next!.Revision);
    }

    [Fact]
    public void revision_旧状态没有哈希时从1开始并立即落盘()
    {
        var model = new MainConfigModel();
        var store = new MemoryStateStore();
        store.Current = store.Current with { SettingsSchemaRevision = 42, SettingsSchemaHash = null };

        var source = new ControlSettingsReportSource(
            () => model, store, NullLogger<ControlSettingsReportSource>.Instance);
        var report = source.Build();

        Assert.NotNull(report);
        Assert.Equal(1, report!.Revision);
        Assert.Equal(1, store.Current.SettingsSchemaRevision);
        Assert.False(string.IsNullOrWhiteSpace(store.Current.SettingsSchemaHash));
    }

    [Fact]
    public void revision_哈希不同而revision相同才加一()
    {
        Assert.Equal(1, ControlSettingsSchemaBuilder.ResolveRevision(0, null, "abc"));
        Assert.Equal(7, ControlSettingsSchemaBuilder.ResolveRevision(7, "abc", "ABC"));
        Assert.Equal(8, ControlSettingsSchemaBuilder.ResolveRevision(7, "abc", "def"));
        Assert.Equal(int.MaxValue,
            ControlSettingsSchemaBuilder.ResolveRevision(int.MaxValue, "abc", "def"));
    }

    // ---------------------------------------------------------------- settings.read

    [Fact]
    public void settings_read_只返回schema键的当前值()
    {
        var model = new MainConfigModel();
        var schema = Build(model).Schema;
        var values = ControlSettingsSchemaBuilder.ReadValues(model);

        Assert.Equal(
            schema.Keys.Order(StringComparer.Ordinal).ToArray(),
            values.Keys.Order(StringComparer.Ordinal).ToArray());

        // 目录里可写但不在 schema 里的设置不出现。
        Assert.DoesNotContain("voice.voice_engine", values.Keys);
        Assert.Equal(model.VoiceSettings.VolumeSize, values["voice.volume"]);
        Assert.Equal(model.NotificationSettings.QuickDraw.DisplayDuration,
            values["notification.quick_draw.display_duration"]);
        Assert.Equal(model.VoiceSettings.VoiceEnable, values["voice.enable"]);
    }

    // ---------------------------------------------------------------- settings.write

    [Fact]
    public void settings_write_未知键整体拒绝且不改模型()
    {
        var model = new MainConfigModel();
        var before = model.VoiceSettings.VolumeSize;

        var planned = ControlSettingsSchemaBuilder.TryPlanPatch(
            model,
            Payload("""{ "patch": { "voice.volume": 35, "voice.mute": true } }"""),
            out var changes,
            out var reason,
            out var field);

        Assert.False(planned);
        Assert.Empty(changes);
        Assert.Equal(ControlRejectReasons.UnknownSetting, reason);
        Assert.Equal("voice.mute", field);
        Assert.Equal(before, model.VoiceSettings.VolumeSize);
    }

    [Fact]
    public void settings_write_目录可写但不在schema里的键也算未知()
    {
        var model = new MainConfigModel();

        var planned = ControlSettingsSchemaBuilder.TryPlanPatch(
            model,
            Payload("""{ "patch": { "voice.voice_engine": 2 } }"""),
            out var changes,
            out var reason,
            out var field);

        Assert.False(planned);
        Assert.Empty(changes);
        Assert.Equal(ControlRejectReasons.UnknownSetting, reason);
        Assert.Equal("voice.voice_engine", field);
    }

    [Theory]
    [InlineData("""{ "patch": { "voice.volume": 101 } }""", "voice.volume")]
    [InlineData("""{ "patch": { "voice.volume": -1 } }""", "voice.volume")]
    [InlineData("""{ "patch": { "voice.enable": "true" } }""", "voice.enable")]
    [InlineData("""{ "patch": { "default_draw.draw_mode": "NoSuchMode" } }""", "default_draw.draw_mode")]
    [InlineData("""{ "patch": { "notification.roll_call.display_duration": 0 } }""",
        "notification.roll_call.display_duration")]
    [InlineData("""{ "patch": { "notification.roll_call.display_duration": 61 } }""",
        "notification.roll_call.display_duration")]
    public void settings_write_非法值整体拒绝并指出字段(string json, string expectedField)
    {
        var model = new MainConfigModel();

        var planned = ControlSettingsSchemaBuilder.TryPlanPatch(
            model, Payload(json), out var changes, out var reason, out var field);

        Assert.False(planned);
        Assert.Empty(changes);
        Assert.Equal(ControlRejectReasons.BadSettingValue, reason);
        Assert.Equal(expectedField, field);
    }

    [Theory]
    [InlineData("""{ "patch": {} }""")]
    [InlineData("""{ "patch": [] }""")]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    public void settings_write_空或非对象patch算无效命令(string json)
    {
        var model = new MainConfigModel();

        var planned = ControlSettingsSchemaBuilder.TryPlanPatch(
            model, Payload(json), out var changes, out var reason, out _);

        Assert.False(planned);
        Assert.Empty(changes);
        Assert.Equal(ControlRejectReasons.InvalidCommand, reason);
    }

    [Fact]
    public void settings_write_合法patch应用后读回新值()
    {
        var model = new MainConfigModel();

        var planned = ControlSettingsSchemaBuilder.TryPlanPatch(
            model,
            Payload(
                """{ "patch": { "voice.volume": 35, "default_draw.draw_mode": "Repeat", "notification.roll_call.display_duration": 5 } }"""),
            out var changes,
            out var reason,
            out var field);

        Assert.True(planned, reason);
        Assert.Null(field);
        Assert.Equal(3, changes.Count);

        ControlSettingsCatalog.Apply(model, changes);

        Assert.Equal(35, model.VoiceSettings.VolumeSize);
        Assert.Equal(SecRandom.Core.Enums.Configs.DrawMode.Repeat, model.DefaultDrawSettings.DrawMode);
        Assert.Equal(5, model.NotificationSettings.RollCall.DisplayDuration);

        // 回执 values 是应用后的完整快照。
        var values = ControlSettingsSchemaBuilder.ReadValues(model);
        Assert.Equal(35, values["voice.volume"]);
        Assert.Equal("Repeat", values["default_draw.draw_mode"]);
        Assert.Equal(5, values["notification.roll_call.display_duration"]);
    }

    // ---------------------------------------------------------------- 工具

    private static int CountCodePoints(string text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes())
            count++;
        return count;
    }

    private sealed class MemoryStateStore : IControlNodeStateStore
    {
        public ControlNodeState Current { get; set; } = new();

        public void Update(Func<ControlNodeState, ControlNodeState> mutate) => Current = mutate(Current);

        public event EventHandler<ControlNodeState>? Changed;
    }
}