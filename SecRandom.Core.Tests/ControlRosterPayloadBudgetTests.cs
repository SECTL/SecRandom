using System.Text.Json;
using System.Text.Json.Serialization;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     名单读写载荷的**帧预算**：特殊语音那三个字段值多少字节、超预算时还收不收敛。
/// </summary>
/// <remarks>
///     <para>
///         为什么值得单独一条测试：尺寸是**会随字段增长漂移**的东西，必须由断言看着，而不是靠人记着。
///         设置目录那边的同类断言（<c>ControlSettingsCatalogTests.控制台请求的五类设置载荷在单帧预算之内</c>）
///         就是拿一次真实故障换来的。
///     </para>
///     <para>
///         数字都是**实测**的（见每处的注释），不是估算；断言写的是几条关系，而不是把字节数钉死，
///         这样换 .NET 版本、换转义策略时是"关系被破坏"才失败。
///     </para>
/// </remarks>
public sealed class ControlRosterPayloadBudgetTests
{
    /// <summary>
    ///     控制台的写载荷在本仓库里最接近的替身：snake_case + 空值不写出
    ///     （协议两侧共用这一套约定，与 <see cref="ControlProtocolJson.Options" /> 的差别只在命名策略）。
    /// </summary>
    /// <remarks>
    ///     它**不是**线上格式：真正的写载荷由控制台生成（另一个仓库），而 <c>roster.write</c> 在这边只被
    ///     解析、不被序列化。这里量的是"同样这些字段值要花多少字节"，逐行误差约 1 字节
    ///     （<c>exists</c> 与协议里的 <c>enabled</c> 差一个字母），足够支撑下面的结论。
    /// </remarks>
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ---------------------------------------------------------------- 读通道

    /// <summary>
    ///     读通道：带特殊语音的名单超出预算时**照旧截断**，并把 <c>count</c>/<c>total</c>/<c>truncated</c> 如实报出来。
    /// </summary>
    /// <remarks>
    ///     实测（500 名成员，三人各带别名"张老师"+前缀"请"+后缀"上台"）：原始 125043 字节，
    ///     截到 240 人 / 60042 字节。不带特殊语音的同一份是 67043 → 449 人 / 60208 字节：
    ///     新字段让每个人贵约 116 字节，能装下的人数从 449 掉到 240，**但预算行为本身没有变**。
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 读通道_带不带特殊语音都在预算内截断并如实报数(bool withVoice)
    {
        var response = BuildStudentResponse(500, withVoice);
        var raw = ControlProtocolJson.MeasureBytes(response);

        Assert.True(
            raw > ControlProtocolJson.PayloadBudgetBytes,
            $"样本本身必须超预算才有意义，实测 {raw} 字节。");

        var trimmed = ControlRosterReadResponse.TrimToBudget(response);
        var bytes = ControlProtocolJson.MeasureBytes(trimmed);
        var list = trimmed.Lists[0];

        Assert.True(
            bytes <= ControlProtocolJson.PayloadBudgetBytes,
            $"裁完仍有 {bytes} 字节，超过预算 {ControlProtocolJson.PayloadBudgetBytes} 字节。");

        // 三个字段必须自洽：count 是实际回传数，total 是过滤后真实人数，truncated = count < total。
        Assert.Equal(list.Members.Count, list.Count);
        Assert.Equal(500, list.Total);
        Assert.True(list.Truncated);
        Assert.InRange(list.Members.Count, 1, 499);

        // 预算要用得上，而不是"砍到没人"——砍到 0 也能让尺寸过关，但那不是截断，是失败。
        Assert.True(bytes > ControlProtocolJson.PayloadBudgetBytes / 2, $"只用了 {bytes} 字节，截断过头了。");

        // 留下来的成员必须还是完整的（含特殊语音），不能截出半个人。
        var kept = list.Members[0];
        Assert.Equal("01", kept.Id);
        if (withVoice)
        {
            Assert.Equal("张老师", kept.SpecificVoiceAlias);
            Assert.Equal("请", kept.SpecificVoicePrefix);
            Assert.Equal("上台", kept.SpecificVoiceSuffix);
        }
        else
        {
            Assert.Null(kept.SpecificVoiceAlias);
            Assert.Null(kept.SpecificVoicePrefix);
            Assert.Null(kept.SpecificVoiceSuffix);
        }
    }

    /// <summary>
    ///     最坏情况：每个成员三项各 30 个汉字（转义后 180 字节/项）。原始载荷 206043 字节，
    ///     截到 146 人 / 60194 字节——仍然收敛，且收敛后远小于原始值。
    /// </summary>
    [Fact]
    public void 读通道_长文本最坏情况也收得进预算()
    {
        var longVoice = new string('语', 30);
        var members = Enumerable.Range(1, 500)
            .Select(index => ControlRosterMemberPayload.FromStudent(BuildStudent(index, longVoice)))
            .ToList();

        var response = new ControlRosterReadResponse(
            ControlRosterReadRequest.Students,
            [new ControlRosterListPayload("高一（1）班", true, members.Count, members.Count, false, members)]);

        var raw = ControlProtocolJson.MeasureBytes(response);
        var trimmed = ControlRosterReadResponse.TrimToBudget(response);
        var bytes = ControlProtocolJson.MeasureBytes(trimmed);
        var list = trimmed.Lists[0];

        Assert.True(raw > ControlProtocolJson.PayloadBudgetBytes);
        Assert.True(
            bytes <= ControlProtocolJson.PayloadBudgetBytes,
            $"裁完仍有 {bytes} 字节，超过预算 {ControlProtocolJson.PayloadBudgetBytes} 字节。");
        Assert.True(list.Truncated);
        Assert.Equal(list.Members.Count, list.Count);
        Assert.Equal(500, list.Total);

        // 一个人的三项就得 540 字节（转义后），所以这次留下的人明显少于不带长文本的那一档。
        Assert.InRange(list.Members.Count, 1, 300);
    }

    /// <summary>奖池与学生共用同一组字段，预算行为也必须一样（特殊语音是两种名单共用的附加设置）。</summary>
    [Fact]
    public void 读通道_奖池那边的截断行为与学生一致()
    {
        var members = Enumerable.Range(1, 500)
            .Select(index => ControlRosterMemberPayload.FromPrize(new Prize
            {
                Id = $"P{index}",
                Name = $"奖品{index}",
                Count = 1,
                Weight = 1,
                Exists = true,
                AttachedObjects =
                {
                    [ControlSpecificVoiceValues.SettingsId] = new SpecificAnnouncementAttachedSettings
                    {
                        IsAttachSettingsEnabled = true,
                        TtsAlias = "张老师",
                        Prefix = "请",
                        Suffix = "上台"
                    }
                }
            }))
            .ToList();

        var response = new ControlRosterReadResponse(
            ControlRosterReadRequest.Prizes,
            [new ControlRosterListPayload("元旦抽奖", true, members.Count, members.Count, false, members)]);

        var trimmed = ControlRosterReadResponse.TrimToBudget(response);
        var list = trimmed.Lists[0];

        Assert.True(ControlProtocolJson.MeasureBytes(trimmed) <= ControlProtocolJson.PayloadBudgetBytes);
        Assert.Equal(list.Members.Count, list.Count);
        Assert.Equal(500, list.Total);
        Assert.True(list.Truncated);
        Assert.Equal("张老师", list.Members[0].SpecificVoiceAlias);
    }

    /// <summary>
    ///     没有特殊语音的成员身上**一个相关字节都不该有**：三个键必须整个不出现。
    /// </summary>
    /// <remarks>
    ///     实测单个成员：不带 131 字节，带（别名 3 汉字 + 前后缀各 1 汉字）247 字节，
    ///     差 116 字节 = 三个"键 + 转义值 + 引号逗号"。想要"没设置就不要钱"，
    ///     靠的就是序列化时空值不写出，而不是靠值恰好是空串。
    /// </remarks>
    [Fact]
    public void 读通道_没有特殊语音的成员不写出三个键()
    {
        var plain = JsonSerializer.Serialize(
            ControlRosterMemberPayload.FromStudent(BuildStudent(1, null)), ControlProtocolJson.Options);
        var withVoice = JsonSerializer.Serialize(
            ControlRosterMemberPayload.FromStudent(BuildStudent(1, "张老师")), ControlProtocolJson.Options);

        Assert.DoesNotContain("specific_voice", plain, StringComparison.Ordinal);

        Assert.Contains("\"specific_voice_alias\"", withVoice, StringComparison.Ordinal);
        Assert.Contains("\"specific_voice_prefix\"", withVoice, StringComparison.Ordinal);
        Assert.Contains("\"specific_voice_suffix\"", withVoice, StringComparison.Ordinal);

        // 只有这三个扁平字段，没有嵌套对象（嵌套写法是另一套形状，控制台那边不认识）。
        Assert.DoesNotContain("\"specific_voice\":", withVoice, StringComparison.Ordinal);

        var delta = ControlProtocolJson.MeasureBytes(
                        ControlRosterMemberPayload.FromStudent(BuildStudent(1, "张老师")))
                    - ControlProtocolJson.MeasureBytes(
                        ControlRosterMemberPayload.FromStudent(BuildStudent(1, null)));

        Assert.InRange(delta, 80, 200);
    }

    // ---------------------------------------------------------------- 写通道

    /// <summary>
    ///     写通道：没下发的三个字段同样一个字节都不占；下发了就是三个扁平字段，不带派生对象。
    /// </summary>
    /// <remarks>
    ///     <c>ControlRosterStudentInput.SpecificVoice</c> 是个**派生**助手属性，一旦没有
    ///     <c>[JsonIgnore]</c>，System.Text.Json 会给每条成员补一个
    ///     <c>"specific_voice":{"has_any":false}</c>（35 字节）或把三个值重抄一遍（105 字节）。
    ///     线上虽然不序列化这个记录，但"量尺寸"这件事本身就会被它污染——所以这条断言要留着。
    /// </remarks>
    [Fact]
    public void 写通道_派生属性不进JSON且未下发时不占字节()
    {
        var plain = JsonSerializer.Serialize(
            new ControlRosterStudentInput("01", "张三", "男", "A组", true, ["住宿"]), WireOptions);
        var withVoice = JsonSerializer.Serialize(
            new ControlRosterStudentInput("01", "张三", "男", "A组", true, ["住宿"], "张老师", "请", "上台"),
            WireOptions);

        Assert.DoesNotContain("specific_voice", plain, StringComparison.Ordinal);
        Assert.Contains("\"specific_voice_alias\":", withVoice, StringComparison.Ordinal);
        Assert.Contains("\"specific_voice_prefix\":", withVoice, StringComparison.Ordinal);
        Assert.Contains("\"specific_voice_suffix\":", withVoice, StringComparison.Ordinal);
        Assert.DoesNotContain("\"specific_voice\":", withVoice, StringComparison.Ordinal);
    }

    /// <summary>
    ///     两千行的写载荷**本来就不止一帧**，新字段只是把"一帧能装多少人"再压低一档。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         实测（2000 行学生，snake_case）：
    ///     </para>
    ///     <list type="bullet">
    ///         <item>只有 id/姓名/性别/分组：189036 字节（94 字节/行）——**已经**是 64 KiB 帧上限的 2.9 倍。</item>
    ///         <item>再加标签：267036 字节（134 字节/行）。</item>
    ///         <item>再加三项特殊语音：499036 字节（250 字节/行），三项 +116 字节/行。</item>
    ///         <item>三项各 30 个汉字：1507036 字节（754 字节/行）。</item>
    ///     </list>
    ///     <para>
    ///         也就是说 <c>MaxStudents = 2000</c> 这个上限在**单帧**里根本达不到：按 250 字节/行算，
    ///         65536 字节只装得下 262 行。结论有两面——一面是"不设三个字段也早就装不下"，
    ///         所以这不是本次改动引入的问题；另一面是"没下发就不占字节"必须成立，
    ///         否则每一行都会被推得更贵（[JsonIgnore] 与空值不写出就是为此）。
    ///     </para>
    /// </remarks>
    [Fact]
    public void 写通道_两千行装不进一帧且未下发的字段不推高每行成本()
    {
        var bare = BuildStudentInputs(2000, withTags: false, withVoice: false);
        var tagged = BuildStudentInputs(2000, withTags: true, withVoice: false);
        var voiced = BuildStudentInputs(2000, withTags: true, withVoice: true);

        var bareBytes = MeasureWritePayload(bare);
        var taggedBytes = MeasureWritePayload(tagged);
        var voicedBytes = MeasureWritePayload(voiced);

        Assert.True(
            bareBytes > ControlProtocolJson.MaxFrameBytes,
            $"两千行**不带**任何附加字段的载荷实测 {bareBytes} 字节，本该已经超过单帧上限 " +
            $"{ControlProtocolJson.MaxFrameBytes} 字节——这条断言的意义是说明本改动没有把它推过线。");

        // 三个字段的每行代价：写通道与读通道实测都是 116 字节/人（两边都是转义后的 JSON）。
        var perRow = (voicedBytes - taggedBytes) / 2000;

        Assert.InRange(perRow, 80, 200);

        // 一帧能装下的行数：远小于 MaxStudents，所以"2000 行"这条上限必须配合分片/分批才有意义。
        var rowsPerFrame = ControlProtocolJson.MaxFrameBytes / (voicedBytes / 2000);

        Assert.InRange(rowsPerFrame, 100, 400);
        Assert.True(rowsPerFrame < ControlRosterPushRequest.MaxStudents);
    }

    // ---------------------------------------------------------------- 造数据

    private static int MeasureWritePayload(IReadOnlyList<ControlRosterStudentInput> students) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new ControlRosterPushRequest("高一（1）班", RosterWriteModes.Replace, false, students),
            WireOptions).Length;

    private static List<ControlRosterStudentInput> BuildStudentInputs(int count, bool withTags, bool withVoice)
    {
        string[]? tags = withTags ? ["住宿", "组长"] : null;

        return Enumerable.Range(1, count)
            .Select(index => new ControlRosterStudentInput(
                $"2024{index:D5}",
                $"学生{index}",
                "男",
                "A组",
                true,
                tags,
                withVoice ? "张老师" : null,
                withVoice ? "请" : null,
                withVoice ? "上台" : null))
            .ToList();
    }

    private static ControlRosterReadResponse BuildStudentResponse(int count, bool withVoice)
    {
        var members = Enumerable.Range(1, count)
            .Select(index => ControlRosterMemberPayload.FromStudent(
                BuildStudent(index, withVoice ? "张老师" : null)))
            .ToList();

        return new ControlRosterReadResponse(
            ControlRosterReadRequest.Students,
            [new ControlRosterListPayload("高一（1）班", true, members.Count, members.Count, false, members)]);
    }

    private static Student BuildStudent(int index, string? alias)
    {
        var student = new Student
        {
            Id = $"{index:D2}",
            Name = $"学生{index}",
            Gender = "男",
            Group = "A组",
            Tags = "住宿 组长",
            Exists = true
        };

        if (alias is null)
            return student;

        student.AttachedObjects[ControlSpecificVoiceValues.SettingsId] = new SpecificAnnouncementAttachedSettings
        {
            IsAttachSettingsEnabled = true,
            TtsAlias = alias,
            Prefix = "请",
            Suffix = "上台"
        };

        return student;
    }
}
