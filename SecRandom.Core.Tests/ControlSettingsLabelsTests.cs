using System.Globalization;
using SecRandom.Core.Models;
using SecRandom.Core.Services.ControlNode;

namespace SecRandom.Core.Tests;

/// <summary>
///     设置目录的文案来源：分类名、字段标签、字段说明。
/// </summary>
/// <remarks>
///     控制台只认设备下发的文案（"分类 + 标签 + 说明，和客户端本机设置页一致"），所以这里钉住的不是某一句
///     具体措辞，而是**文案一定存在、一定跟着设备界面语言走、一定不退化成协议路径**这三件事。
/// </remarks>
public sealed class ControlSettingsLabelsTests
{
    private static IReadOnlyList<ControlSettingCategory> DescribeAll() =>
        ControlSettingsCatalog.Describe(new MainConfigModel());

    private static IReadOnlyList<ControlSettingField> Fields() =>
        [.. DescribeAll().SelectMany(category => category.Fields)];

    private static ControlSettingField Field(string path) =>
        Fields().Single(field => field.Path == path);

    /// <summary>在指定界面语言下取值，用完立刻还原——不能把语言留在全局状态里影响别的测试。</summary>
    private static T UnderCulture<T>(string cultureName, Func<T> read)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            return read();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void 文案_每个类目与每个字段都有非空标签()
    {
        var described = DescribeAll();

        Assert.NotEmpty(described);
        Assert.All(described, category =>
        {
            Assert.False(
                string.IsNullOrWhiteSpace(category.Label),
                $"类目 {category.Id} 没有标签，控制台只会渲染出一个没有名字的分组");
            Assert.All(category.Fields, field => Assert.False(
                string.IsNullOrWhiteSpace(field.Label),
                $"字段 {field.Path} 没有标签，控制台只会渲染出一行没有名字的设置"));
        });
    }

    [Fact]
    public void 文案_标签不会退化成协议路径或类目ID()
    {
        foreach (var category in DescribeAll())
        {
            Assert.NotEqual(category.Id, category.Label);

            foreach (var field in category.Fields)
            {
                Assert.NotEqual(field.Path, field.Label);

                // 路径最后一段也不是答案：控制台管理员要读的是设置名，不是代码里的属性名。
                var token = field.Path[(field.Path.LastIndexOf('.') + 1)..];
                Assert.NotEqual(token, field.Label);
            }
        }
    }

    [Fact]
    public void 文案_枚举与开关这些需要解释的字段大多带说明()
    {
        var fields = Fields();
        var described = fields.Count(field => !string.IsNullOrWhiteSpace(field.Description));

        // 说明来自设置页自己的 *_D 文案，设置页没有写过的项（隐藏配置、页面开关）才允许为空。
        // 说明来自设置页自己的 *_D 文案，设置页没有写过的项（隐藏配置、页面开关）才允许为空。
        Assert.True(
            described >= 200,
            $"只有 {described}/{fields.Count} 个字段带说明，控制台里绝大多数设置只剩一个名字");

        // 可写字段是管理员真正会动手改的那些，它们更不该只有名字。
        var writable = fields.Where(field => field.Writable).ToList();
        var writableDescribed = writable.Count(field => !string.IsNullOrWhiteSpace(field.Description));
        Assert.True(
            writableDescribed * 10 >= writable.Count * 9,
            $"可写字段只有 {writableDescribed}/{writable.Count} 个带说明");
    }

    [Fact]
    public void 文案_按设备当前的界面语言取词()
    {
        // 这几条都是设置页自己写过的文案，因此三种语言都必须真的取到不同的话，
        // 否则"标签跟设备界面语言走"就只是句口号。
        string[] samples =
        [
            "voice.enable",
            "roll_call.half_repeat",
            "floating_window.floating_window_opacity",
            "linkage.data_source",
            "security.security_enabled"
        ];

        var chinese = UnderCulture("zh-CN", () => samples.Select(path => Field(path).Label).ToArray());
        var english = UnderCulture("en-US", () => samples.Select(path => Field(path).Label).ToArray());
        var japanese = UnderCulture("ja-JP", () => samples.Select(path => Field(path).Label).ToArray());

        for (var index = 0; index < samples.Length; index++)
        {
            Assert.NotEqual(chinese[index], english[index]);
            Assert.NotEqual(chinese[index], japanese[index]);
            Assert.All(new[] { chinese[index], english[index], japanese[index] }, label =>
                Assert.False(string.IsNullOrWhiteSpace(label)));
        }
    }

    [Fact]
    public void 文案_说明同样跟着界面语言走()
    {
        // 说明和标签走同一条回退链，这里只确认它没有被写死成中文。
        var chinese = UnderCulture("zh-CN", () => Field("voice.volume").Description);
        var english = UnderCulture("en-US", () => Field("voice.volume").Description);

        Assert.False(string.IsNullOrWhiteSpace(chinese));
        Assert.False(string.IsNullOrWhiteSpace(english));
        Assert.NotEqual(chinese, english);
    }

    [Fact]
    public void 文案_类目标签取客户端自己的分组说法()
    {
        Assert.Equal("语音", UnderCulture("zh-CN", () => DescribeAll().Single(category => category.Id == "voice").Label));
        Assert.Equal("点名设置", UnderCulture("zh-CN", () => DescribeAll().Single(category => category.Id == "roll_call").Label));
        Assert.Equal("Voice", UnderCulture("en-US", () => DescribeAll().Single(category => category.Id == "voice").Label));
    }

    [Fact]
    public void 文案_查不到的路径也会给一个能读的兜底标签()
    {
        // 目录将来新增一条设置、而这里还没来得及配对文案时走的就是这条兜底：不能是空串，也不能是路径。
        var label = ControlSettingsLabels.GetFieldLabel("future_group.voice_enable");

        Assert.Equal("Voice enable", label);
        Assert.Null(ControlSettingsLabels.GetFieldDescription("future_group.voice_enable"));
        Assert.Equal("Future group", ControlSettingsLabels.GetCategoryLabel("future_group"));
    }

    // ---------------------------------------------------------------- 三语映射

    /// <summary>
    ///     同一条文案一次给出中英日三语，控制台才能挑自己界面语言的那一份。
    /// </summary>
    /// <remarks>
    ///     这里的断言**不设置界面语言**，因为三语映射本来就不该随设备语言变化；下面那几条才在指定语言下取值。
    /// </remarks>
    [Fact]
    public void 三语_已知字段与类目一次给全三种语言()
    {
        var labels = ControlSettingsLabels.GetFieldLabels("voice.volume");

        Assert.NotNull(labels);
        Assert.Equal(new[] { "zh-CN", "en-US", "ja-JP" }, labels!.Keys.ToArray());
        Assert.Equal("语音音量", labels["zh-CN"]);
        Assert.Equal("Voice volume", labels["en-US"]);
        Assert.False(string.IsNullOrWhiteSpace(labels["ja-JP"]));

        var descriptions = ControlSettingsLabels.GetFieldDescriptions("voice.volume");

        Assert.NotNull(descriptions);
        Assert.Equal(new[] { "zh-CN", "en-US", "ja-JP" }, descriptions!.Keys.ToArray());
        Assert.Equal("设置播报音量", descriptions["zh-CN"]);
        Assert.Equal("Set announcement volume", descriptions["en-US"]);

        var categoryLabels = ControlSettingsLabels.GetCategoryLabels("voice");

        Assert.NotNull(categoryLabels);
        Assert.Equal(new[] { "zh-CN", "en-US", "ja-JP" }, categoryLabels!.Keys.ToArray());
        Assert.Equal("语音", categoryLabels["zh-CN"]);
        Assert.Equal("Voice", categoryLabels["en-US"]);

        // 类目只是分组，客户端从来没有写过类目级说明：这里永远是 null。
        Assert.Null(ControlSettingsLabels.GetCategoryDescriptions("voice"));
    }

    /// <summary>三语映射与设备当前的界面语言无关：同一台中文设备，英文控制台读到的仍然是英文。</summary>
    [Fact]
    public void 三语_不随设备界面语言变化()
    {
        var baseline = Rendered(ControlSettingsLabels.GetFieldLabels("voice.volume"));

        Assert.NotEmpty(baseline);
        Assert.Equal(baseline, UnderCulture("zh-CN", () => Rendered(ControlSettingsLabels.GetFieldLabels("voice.volume"))));
        Assert.Equal(baseline, UnderCulture("en-US", () => Rendered(ControlSettingsLabels.GetFieldLabels("voice.volume"))));
        Assert.Equal(baseline, UnderCulture("ja-JP", () => Rendered(ControlSettingsLabels.GetFieldLabels("voice.volume"))));
    }

    /// <summary>单语言的那两份按设备语言取值（旧控制台只认它们），请求带 locale 时按请求的语言取值。</summary>
    [Fact]
    public void 三语_单语言文案按设备语言_请求语言可以覆盖它()
    {
        var chinese = UnderCulture("zh-CN", () => Field("voice.volume"));
        var english = UnderCulture("en-US", () => Field("voice.volume"));

        Assert.Equal("语音音量", chinese.Label);
        Assert.Equal("Voice volume", english.Label);
        Assert.NotEqual(chinese.Label, english.Label);

        // 取词机制本身：三语映射的每一语与"那一语下的单语言取值"一致。
        Assert.Equal(chinese.Label, ControlSettingsLabels.GetFieldLabels("voice.volume")!["zh-CN"]);
        Assert.Equal(english.Label, ControlSettingsLabels.GetFieldLabels("voice.volume")!["en-US"]);
        Assert.Equal(chinese.Description, ControlSettingsLabels.GetFieldDescriptions("voice.volume")!["zh-CN"]);
        Assert.Equal(english.Description, ControlSettingsLabels.GetFieldDescriptions("voice.volume")!["en-US"]);

        // **请求的语言优先于设备语言**：中文设备 + 英文控制台要看英文，反之亦然。
        Assert.Equal("Voice volume", ControlSettingsLabels.GetFieldLabel("voice.volume", "en-US"));
        Assert.Equal("语音音量", ControlSettingsLabels.GetFieldLabel("voice.volume", "zh-CN"));

        // 认不出来的语言标签退回设备语言，而不是给出空标题。
        Assert.Equal(chinese.Label, ControlSettingsLabels.GetFieldLabel("voice.volume", "de-DE"));
        Assert.Equal(chinese.Label, ControlSettingsLabels.GetFieldLabel("voice.volume", null));

        // 类目标签同理。
        Assert.Equal(
            "语音",
            UnderCulture("zh-CN", () => DescribeAll().Single(category => category.Id == "voice").Label));
        Assert.Equal(
            "Voice",
            UnderCulture("en-US", () => DescribeAll().Single(category => category.Id == "voice").Label));
        Assert.Equal("Voice", ControlSettingsLabels.GetCategoryLabel("voice", "en-US"));
        Assert.Equal("音声", ControlSettingsLabels.GetCategoryLabel("voice", "ja-JP"));
    }

    /// <summary>把三语映射摊成可逐字比较的字符串：字典本身没有值相等语义，比不了内容。</summary>
    private static string[] Rendered(IReadOnlyDictionary<string, string>? map) =>
        map is null ? [] : [.. map.Select(pair => $"{pair.Key}={pair.Value}")];
}
