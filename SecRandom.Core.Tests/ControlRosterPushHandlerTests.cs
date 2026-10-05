using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.ControlNode;
using SecRandom.Shared;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Interfaces;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     <c>roster.write</c> 落地的那一半：名单与奖池两条分支的备份、重建/合并与激活。
/// </summary>
/// <remarks>
///     <para>
///         解析与合并规则由 <see cref="ControlNodePayloadTests" /> 逐条钉住；这里钉的是**落到设备上之后**
///         的形状：<c>replace</c> 真的按载荷重建、<c>activate</c> 切的是对应那一侧的默认名单
///         （奖池绝不能写进默认班级）、写之前真的留了备份。这几件事没有别的地方能验。
///     </para>
///     <para>
///         走的是 <see cref="ControlRosterPushHandler.Apply" /> 而不是 <c>ExecuteAsync</c>：后者把副作用
///         投递到 Avalonia 的 UI 线程，而单测进程里没有消息循环，投递出去的作业没有线程能执行
///         （<c>RunJobs</c> 在别的线程上会抛 "different thread owns it"）。
///         投递本身只有一行，被它包住的判断与落盘才是有内容的部分。
///     </para>
/// </remarks>
public sealed class ControlRosterPushHandlerTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(), "SecRandom", "control-roster-push-tests", Guid.NewGuid().ToString("N"));

    public ControlRosterPushHandlerTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [Fact]
    public void 奖池下发_替换加激活会重建奖池并切默认奖池且先备份()
    {
        using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<IProfileCatalogManager>();
        var config = provider.GetRequiredService<MainConfigHandler>();
        var handler = CreateHandler(provider);

        // 先有一份旧奖池，备份才有东西可备。
        Assert.True(catalog.ReplacePrizes("元旦抽奖", [new Prize { Id = "old", Name = "旧奖品", Count = 9 }]));

        var outcome = handler.Apply(Parse(
            """
            { "list_name": "元旦抽奖", "mode": "replace", "activate": true, "roster_kind": "prizes",
              "prizes": [ { "id": "p1", "name": "一等奖", "count": 2, "weight": 1.5, "tags": [ "甲" ] },
                          { "id": "p2", "name": "二等奖" } ] }
            """));

        Assert.True(outcome.Ok, outcome.Reason);

        // replace：整份奖池按载荷重建，旧奖品不再存在；没下发 tags 的奖品在设备上就是没有标签。
        var list = catalog.LoadPrizeList("元旦抽奖");
        Assert.NotNull(list);
        Assert.Equal(2, list.Prizes.Count);
        var first = Assert.Single(list.Prizes, prize => prize.Id == "p1");
        Assert.Equal("一等奖", first.Name);
        Assert.Equal(2, first.Count);
        Assert.Equal(1.5, first.Weight);
        Assert.Equal("甲", first.Tags);
        Assert.Equal(string.Empty, Assert.Single(list.Prizes, prize => prize.Id == "p2").Tags);

        // activate：切的是默认**奖池**（LotterySettings.DefaultPool）。
        Assert.Equal("元旦抽奖", config.Data.LotterySettings.DefaultPool);

        // 写之前旧的那一份必须留在 backup/roster 下——回滚靠它，而不是靠"记得改了什么"。
        var backups = Directory.GetFiles(Utils.GetDirectoryPath("backup", "roster"));
        Assert.Contains(backups, path => Path.GetFileName(path).StartsWith("元旦抽奖_", StringComparison.Ordinal));
    }

    [Fact]
    public void 奖池下发_合并加激活保留记录身份且没下发的标签不动()
    {
        using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<IProfileCatalogManager>();
        var config = provider.GetRequiredService<MainConfigHandler>();
        var handler = CreateHandler(provider);

        var recordId = Guid.NewGuid();
        Assert.True(catalog.ReplacePrizes(
            "元旦抽奖",
            [new Prize { RecordId = recordId, Id = "p1", Name = "一等奖", Count = 2, Tags = "甲 乙" }]));

        var outcome = handler.Apply(Parse(
            """
            { "list_name": "元旦抽奖", "mode": "merge", "activate": true, "roster_kind": "prizes",
              "prizes": [ { "id": "p1", "name": "特等奖", "count": 3 } ] }
            """));

        Assert.True(outcome.Ok, outcome.Reason);

        var prize = Assert.Single(catalog.LoadPrizeList("元旦抽奖")!.Prizes);

        // 就地更新：落盘回来还是同一个 RecordId，抽奖历史不会因为一次改名而断掉。
        Assert.Equal(recordId, prize.RecordId);
        Assert.Equal("特等奖", prize.Name);
        Assert.Equal(3, prize.Count);
        Assert.Equal("甲 乙", prize.Tags);
        Assert.Equal("元旦抽奖", config.Data.LotterySettings.DefaultPool);
    }

    [Fact]
    public void 名单下发_学生分支照旧且不碰奖池那一侧()
    {
        using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<IProfileCatalogManager>();
        var config = provider.GetRequiredService<MainConfigHandler>();
        var handler = CreateHandler(provider);

        // 不带 roster_kind 的旧载荷必须与从前逐字同义。
        var defaultPoolBefore = config.Data.LotterySettings.DefaultPool;

        var outcome = handler.Apply(Parse(
            """
            { "list_name": "高一1班", "mode": "replace", "activate": true,
              "students": [ { "id": "01", "name": "张三", "tags": [ "组长" ] } ] }
            """));

        Assert.True(outcome.Ok, outcome.Reason);

        var list = catalog.LoadStudentList("高一1班");
        Assert.NotNull(list);
        var student = Assert.Single(list.Students);
        Assert.Equal("张三", student.Name);
        Assert.Equal("组长", student.Tags);
        Assert.Equal("高一1班", config.Data.RollCallSettings.DefaultClass);

        // 学生分支不该碰到奖池那一侧的默认值。
        Assert.Equal(defaultPoolBefore, config.Data.LotterySettings.DefaultPool);
    }

    /// <summary>类型不认识时连解析都过不去，更不会落盘。</summary>
    [Fact]
    public void 奖池下发_类型不认识时什么都不写()
    {
        using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<IProfileCatalogManager>();

        var parsed = ControlRosterPushRequest.TryParse(
            JsonDocument.Parse(
                    """{ "list_name": "元旦抽奖", "roster_kind": "teachers", "prizes": [ { "name": "一等奖" } ] }""")
                .RootElement.Clone(),
            out var request,
            out var reason);

        Assert.False(parsed);
        Assert.Null(request);
        Assert.Equal("unsupported_roster_kind:teachers", reason);
        Assert.Null(catalog.LoadPrizeList("元旦抽奖"));
    }

    /// <summary>
    ///     「特殊语音」跟着成员一起落盘，也一起被读回来。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         这条断言真正钉住的是**存储链**：附加设置存在 <c>Student.AttachedObjects</c> 里，
    ///         名单文件用 snake_case 写盘、读回来时值是 <c>JsonElement</c>，
    ///         必须由 <c>GetAttachedObject&lt;T&gt;</c> 按同一套命名策略反序列化。
    ///         这一环断了的表现是"写进去成功、读出来永远没有"——没有异常，也没有日志。
    ///     </para>
    ///     <para>
    ///         这里走的是 <see cref="IProfileCatalogManager.LoadStudentList" />（真的重新读文件），
    ///         而不是拿着内存里那个对象自查。
    ///     </para>
    /// </remarks>
    [Fact]
    public void 名单下发_特殊语音随名单落盘并能重新读回来()
    {
        using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<IProfileCatalogManager>();
        var handler = CreateHandler(provider);

        var outcome = handler.Apply(Parse(
            """
            { "list_name": "高一1班", "mode": "replace",
              "students": [ { "id": "01", "name": "张三",
                              "specific_voice_alias": "张老师",
                              "specific_voice_prefix": "请",
                              "specific_voice_suffix": "上台" },
                            { "id": "02", "name": "李四" } ] }
            """));

        Assert.True(outcome.Ok, outcome.Reason);

        var list = catalog.LoadStudentList("高一1班");

        Assert.NotNull(list);

        var voice = Voice(Assert.Single(list.Students, student => student.Id == "01"));

        Assert.NotNull(voice);
        Assert.True(voice!.IsAttachSettingsEnabled);
        Assert.Equal("张老师", voice.TtsAlias);
        Assert.Equal("请", voice.Prefix);
        Assert.Equal("上台", voice.Suffix);

        // replace 下没提这一项的行在设备上就是"没有这项附加设置"，不是"有三个空串"。
        Assert.Null(Voice(Assert.Single(list.Students, student => student.Id == "02")));

        // 读通道从同一个键取同一份值。
        var member = ControlRosterMemberPayload.FromStudent(Assert.Single(list.Students, student => student.Id == "01"));

        Assert.Equal("张老师", member.SpecificVoiceAlias);
    }

    /// <summary>
    ///     <c>merge</c> 下没提特殊语音时**一个字都不动**——控制台只回写改过的行，
    ///     把"没提"当成"清空"会让一次改名顺手把这台机器的专属语音抹掉。
    /// </summary>
    [Fact]
    public void 名单下发_合并模式没提特殊语音时不动设备上的值()
    {
        using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<IProfileCatalogManager>();
        var handler = CreateHandler(provider);

        var existing = new Student { Id = "01", Name = "张三" };
        AttachVoice(existing, "张老师", "请", "上台");
        Assert.True(catalog.ReplaceStudents("高一1班", [existing]));

        var outcome = handler.Apply(Parse(
            """
            { "list_name": "高一1班", "mode": "merge",
              "students": [ { "id": "01", "name": "张三丰" } ] }
            """));

        Assert.True(outcome.Ok, outcome.Reason);

        var student = Assert.Single(catalog.LoadStudentList("高一1班")!.Students);

        Assert.Equal("张三丰", student.Name);

        var voice = Voice(student);

        Assert.NotNull(voice);
        Assert.Equal("张老师", voice!.TtsAlias);
        Assert.Equal("请", voice.Prefix);
        Assert.Equal("上台", voice.Suffix);
        Assert.True(voice.IsAttachSettingsEnabled);
    }

    /// <summary>
    ///     显式空串是"明确清空"：三项都给空串 → 附加设置开关跟着关掉，读通道于是什么都不报。
    /// </summary>
    /// <remarks>
    ///     开关必须关：语音服务只在开关打开时读这三个值，留着一个"打开但三项全空"的附加设置，
    ///     下一次读通道回出去的是一组空值，控制台会把它当成"设备上真的存了空"。
    /// </remarks>
    [Fact]
    public void 名单下发_显式空串清空特殊语音并关掉开关()
    {
        using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<IProfileCatalogManager>();
        var handler = CreateHandler(provider);

        var existing = new Student { Id = "01", Name = "张三" };
        AttachVoice(existing, "张老师", "请", "上台");
        Assert.True(catalog.ReplaceStudents("高一1班", [existing]));

        var outcome = handler.Apply(Parse(
            """
            { "list_name": "高一1班", "mode": "merge",
              "students": [ { "id": "01", "name": "张三",
                              "specific_voice_alias": "",
                              "specific_voice_prefix": "",
                              "specific_voice_suffix": "" } ] }
            """));

        Assert.True(outcome.Ok, outcome.Reason);

        var student = Assert.Single(catalog.LoadStudentList("高一1班")!.Students);
        var voice = Voice(student);

        Assert.NotNull(voice);
        Assert.False(voice!.IsAttachSettingsEnabled);
        Assert.Equal(string.Empty, voice.TtsAlias);
        Assert.Equal(string.Empty, voice.Prefix);
        Assert.Equal(string.Empty, voice.Suffix);

        var member = ControlRosterMemberPayload.FromStudent(student);

        Assert.Null(member.SpecificVoiceAlias);
        Assert.Null(member.SpecificVoicePrefix);
        Assert.Null(member.SpecificVoiceSuffix);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static SpecificAnnouncementAttachedSettings? Voice(IAttachableSettingsObject target) =>
        target.GetAttachedObject<SpecificAnnouncementAttachedSettings>(ControlSpecificVoiceValues.SettingsId);

    private static void AttachVoice(IAttachableSettingsObject target, string alias, string prefix, string suffix) =>
        target.AttachedObjects[ControlSpecificVoiceValues.SettingsId] = new SpecificAnnouncementAttachedSettings
        {
            IsAttachSettingsEnabled = true,
            TtsAlias = alias,
            Prefix = prefix,
            Suffix = suffix
        };

    private static ControlRosterPushRequest Parse(string json)
    {
        var payload = JsonDocument.Parse(json).RootElement.Clone();
        Assert.True(ControlRosterPushRequest.TryParse(payload, out var request, out var reason), reason);
        return request!;
    }

    private static ControlRosterPushHandler CreateHandler(ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IProfileCatalogManager>(),
            provider.GetRequiredService<IProfileService>(),
            provider.GetRequiredService<ILogger<ControlRosterPushHandler>>());

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        return services.BuildServiceProvider();
    }

    private static void ConfigureDataRootForTests(string dataRoot) =>
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);

    private static void ResetDataRootForTests() =>
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);

    private static MethodInfo GetUtilsMethod(string name) =>
        typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Utils.{name} was not found.");
}
