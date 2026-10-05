using System.Text.Json;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     <c>draw.trigger</c> 的参数化载荷：解析、条件取值域与筛选。
/// </summary>
/// <remarks>
///     这里钉的是"手机说了什么、设备怎么理解"。向后兼容是其中最要紧的一条：
///     旧控制台发的是**不带载荷**的 <c>draw.trigger</c>，它必须仍然等于"按快抽默认名单抽一次"。
/// </remarks>
public sealed class ControlDrawTriggerPayloadTests
{
    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static Student Student(string id, string name, string gender = "", string group = "", bool exists = true) =>
        new() { Id = id, Name = name, Gender = gender, Group = group, Exists = exists };

    // ---------------------------------------------------------------- 解析与向后兼容

    [Fact]
    public void 没有载荷时等同于旧控制台的快抽()
    {
        Assert.True(ControlDrawTriggerRequest.TryParse(null, out var request, out var reason), reason);

        Assert.Equal(ControlDrawTriggerRequest.TargetQuick, request.Target);
        Assert.False(request.IsRollCall);
        Assert.Null(request.ListName);
        Assert.Null(request.Count);
        Assert.Null(request.Gender);
        Assert.Null(request.Group);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{ "target": "quick" }""")]
    [InlineData("""null""")]
    public void 空对象与显式快抽都落到同一份默认值(string json)
    {
        Assert.True(ControlDrawTriggerRequest.TryParse(Payload(json), out var request, out var reason), reason);

        Assert.Equal(ControlDrawTriggerRequest.QuickTarget, request);
    }

    [Fact]
    public void 点名载荷把五个字段都读出来()
    {
        var parsed = ControlDrawTriggerRequest.TryParse(
            Payload("""
                    { "target": "roll_call", "list_name": "  高一（1）班  ", "count": 3,
                      "gender": "男", "group": "第一组" }
                    """),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        Assert.True(request.IsRollCall);
        Assert.Equal("高一（1）班", request.ListName);
        Assert.Equal(3, request.Count);
        Assert.Equal("男", request.Gender);
        Assert.Equal("第一组", request.Group);
    }

    [Fact]
    public void 空白字符串等于没有条件()
    {
        var parsed = ControlDrawTriggerRequest.TryParse(
            Payload("""{ "target": "roll_call", "list_name": "   ", "gender": "", "group": "  " }"""),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        Assert.Null(request.ListName);
        Assert.Null(request.Gender);
        Assert.Null(request.Group);
    }

    [Fact]
    public void 未知字段被忽略而不是拒绝()
    {
        // 服务端/新控制台将来加字段时，旧设备必须还能抽——能力版本不同不该让命令整条失败。
        var parsed = ControlDrawTriggerRequest.TryParse(
            Payload("""{ "target": "quick", "future_field": { "a": 1 } }"""),
            out _,
            out var reason);

        Assert.True(parsed, reason);
    }

    [Theory]
    [InlineData("""[]""", "invalid_command")]
    [InlineData("\"draw\"", "invalid_command")]
    [InlineData("""5""", "invalid_command")]
    [InlineData("""{ "target": "raffle" }""", "invalid_value:target:unsupported")]
    [InlineData("""{ "target": 1 }""", "invalid_value:target:type_mismatch")]
    [InlineData("""{ "target": "roll_call", "list_name": 5 }""", "invalid_value:list_name:type_mismatch")]
    [InlineData("""{ "target": "roll_call", "gender": true }""", "invalid_value:gender:type_mismatch")]
    [InlineData("""{ "target": "roll_call", "group": [] }""", "invalid_value:group:type_mismatch")]
    [InlineData("""{ "count": "3" }""", "invalid_value:count:type_mismatch")]
    [InlineData("""{ "count": 0 }""", "invalid_value:count:out_of_range:1..200")]
    [InlineData("""{ "count": -1 }""", "invalid_value:count:out_of_range:1..200")]
    [InlineData("""{ "count": 201 }""", "invalid_value:count:out_of_range:1..200")]
    public void 非法载荷被拒绝并说明哪个字段错了(string json, string expectedReason)
    {
        var parsed = ControlDrawTriggerRequest.TryParse(Payload(json), out var request, out var reason);

        Assert.False(parsed);
        Assert.Equal(expectedReason, reason);
        Assert.Equal(ControlDrawTriggerRequest.QuickTarget, request);
    }

    // ---------------------------------------------------------------- 抽奖目标

    [Fact]
    public void 抽奖载荷带的是奖池名与数量()
    {
        var parsed = ControlDrawTriggerRequest.TryParse(
            Payload("""{ "target": "lottery", "list_name": "  元旦抽奖  ", "count": 2 }"""),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        Assert.True(request.IsLottery);
        Assert.False(request.IsRollCall);
        // 抽奖的 list_name 是**奖池名**：字段名与点名共用，含义由 target 决定。
        Assert.Equal("元旦抽奖", request.ListName);
        Assert.Equal(2, request.Count);
        Assert.Null(request.Gender);
        Assert.Null(request.Group);
    }

    [Theory]
    // 奖品没有性别与分组。存在即拒绝，**不静默忽略**：静默忽略会让控制台以为"按这个条件抽的"。
    [InlineData("""{ "target": "lottery", "list_name": "元旦抽奖", "gender": "男" }""", "invalid_value:gender:not_applicable")]
    [InlineData("""{ "target": "lottery", "list_name": "元旦抽奖", "group": "第一组" }""", "invalid_value:group:not_applicable")]
    public void 抽奖载荷带性别或分组时按不适用拒绝(string json, string expectedReason)
    {
        var parsed = ControlDrawTriggerRequest.TryParse(Payload(json), out _, out var reason);

        Assert.False(parsed);
        Assert.Equal(expectedReason, reason);
    }

    [Theory]
    // 空白串仍然等于"没写"：抽奖的 not_applicable 判的是**有值**，不是有字段。
    // （显式 null 不算"没写"——它不是字符串，与点名走同一条 type_mismatch 规则。）
    [InlineData("""{ "target": "lottery", "list_name": "元旦抽奖", "gender": "  ", "group": "" }""")]
    [InlineData("""{ "target": "lottery", "list_name": "元旦抽奖" }""")]
    public void 抽奖载荷里的空条件等于没写(string json)
    {
        var parsed = ControlDrawTriggerRequest.TryParse(Payload(json), out var request, out var reason);

        Assert.True(parsed, reason);
        Assert.Null(request.Gender);
        Assert.Null(request.Group);
    }

    // ---------------------------------------------------------------- 奖池条件

    [Fact]
    public void 奖池不存在时按名单未找到拒绝()
    {
        // 读不到奖池＝名字写错了（或那份奖池被删了）：调用方递进来的就是 null。
        var resolved = ControlDrawConditions.TryResolvePrizes(null, Lottery(null, 1), out var matched, out var reason);

        Assert.False(resolved);
        Assert.Empty(matched);
        Assert.Equal("invalid_value:list_name:not_found", reason);
    }

    [Fact]
    public void 奖池里没有可抽的奖品时拒绝()
    {
        // 停用的奖品与"编号和名称都空"的奖品都不算候选，与本地抽取的候选池同一条规则。
        var pool = new[] { Prize("P01", exists: false), Prize(string.Empty, name: string.Empty) };

        Assert.False(ControlDrawConditions.TryResolvePrizes(pool, Lottery(null, 1), out _, out var reason));
        Assert.Equal("invalid_value:list_name:no_candidate", reason);
    }

    [Fact]
    public void 抽奖数量超过奖池库存时按可用奖品数报越界()
    {
        var pool = new[] { Prize("P01"), Prize("P02"), Prize("P03", exists: false) };

        Assert.False(ControlDrawConditions.TryResolvePrizes(pool, Lottery(null, 3), out _, out var reason));
        // 上限是**可用奖品数**（2），不是池子里的总数（3）：控制台看到的上界必须真的抽得到。
        Assert.Equal("invalid_value:count:out_of_range:1..2", reason);
    }

    [Fact]
    public void 抽奖数量在库存之内时通过并且只留下可抽的奖品()
    {
        var pool = new[] { Prize("P01"), Prize("P02"), Prize("P03", exists: false) };

        Assert.True(ControlDrawConditions.TryResolvePrizes(pool, Lottery(null, 2), out var matched, out var reason), reason);
        Assert.Equal(["P01", "P02"], matched.Select(prize => prize.Id));
    }

    [Fact]
    public void 抽奖不写数量时按一个通过()
    {
        Assert.True(ControlDrawConditions.TryResolvePrizes([Prize("P01")], Lottery(null, null), out var matched, out var reason), reason);
        Assert.Single(matched);
    }

    private static ControlDrawTriggerRequest Lottery(string? listName, int? count) =>
        new(ControlDrawTriggerRequest.TargetLottery, listName, count, null, null);

    private static Prize Prize(string id, bool exists = true, string? name = null, string? tags = null) => new()
    {
        Id = id,
        Name = name ?? (id.Length == 0 ? string.Empty : $"{id} 号奖品"),
        Exists = exists,
        Tags = tags ?? string.Empty
    };

    // ---------------------------------------------------------------- 条件取值域

    [Fact]
    public void 性别与分组选项从名单派生而不是写死()
    {
        var members = new[]
        {
            Student("01", "张三", "男", "第一组"),
            Student("02", "李四", "女", "第二组"),
            Student("03", "王五", "男", "第一组"),
            Student("04", "赵六")
        };

        // 选项按序号（Ordinal）排序：中文字符的码点顺序不等于"男女"，但它是稳定的——
        // 这份列表只用于设备侧的取值校验，手机端的下拉框从 roster.read 的成员里自己派生。
        Assert.Equal(["女", "男"], ControlDrawConditions.GenderOptions(members));
        Assert.Equal(["第一组", "第二组"], ControlDrawConditions.GroupOptions(members));
    }

    // ---------------------------------------------------------------- 条件筛选

    [Fact]
    public void 指定名单抽取的条件只在这个名单里判定()
    {
        var request = new ControlDrawTriggerRequest(ControlDrawTriggerRequest.TargetRollCall, "高一（1）班", null, "男", "第一组");

        // 同一条请求、两份不同的名单：候选只能来自被指定的那一份。
        var first = new[] { Student("01", "张三", "男", "第一组"), Student("02", "李四", "女", "第一组") };
        var second = new[] { Student("11", "王五", "男", "第一组"), Student("12", "赵六", "男", "第一组") };

        Assert.True(ControlDrawConditions.TryResolve(first, request, out var firstMatched, out var firstReason), firstReason);
        Assert.Equal(["01"], firstMatched.Select(student => student.Id));

        Assert.True(ControlDrawConditions.TryResolve(second, request, out var secondMatched, out var secondReason), secondReason);
        Assert.Equal(["11", "12"], secondMatched.Select(student => student.Id));
    }

    [Fact]
    public void 条件取值不存在于名单时拒绝并指明字段()
    {
        var members = new[] { Student("01", "张三", "男", "第一组") };

        var gender = new ControlDrawTriggerRequest(ControlDrawTriggerRequest.TargetRollCall, null, null, "女", null);
        Assert.False(ControlDrawConditions.TryResolve(members, gender, out _, out var genderReason));
        Assert.Equal("invalid_value:gender:not_in_list", genderReason);

        var group = new ControlDrawTriggerRequest(ControlDrawTriggerRequest.TargetRollCall, null, null, null, "第九组");
        Assert.False(ControlDrawConditions.TryResolve(members, group, out _, out var groupReason));
        Assert.Equal("invalid_value:group:not_in_list", groupReason);
    }

    [Theory]
    // 条件是名单里真实存在的，但符合条件的人全部停用
    [InlineData("男", null, "invalid_value:gender:no_matching_member")]
    [InlineData(null, "第一组", "invalid_value:group:no_matching_member")]
    [InlineData(null, null, "invalid_value:list_name:no_candidate")]
    public void 筛选之后没有人时拒绝并说明是哪一类筛选掏空的(string? gender, string? group, string expectedReason)
    {
        var members = new[]
        {
            Student("01", "张三", "男", "第一组", exists: false),
            Student("02", "李四", "女", "第二组", exists: false)
        };

        var request = new ControlDrawTriggerRequest(ControlDrawTriggerRequest.TargetRollCall, null, null, gender, group);

        Assert.False(ControlDrawConditions.TryResolve(members, request, out _, out var reason));
        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void 人数超过符合条件的人数时按实际人数报越界()
    {
        var members = new[]
        {
            Student("01", "张三", "男", "第一组"),
            Student("02", "李四", "男", "第二组"),
            Student("03", "王五", "女", "第一组")
        };

        var tooMany = new ControlDrawTriggerRequest(ControlDrawTriggerRequest.TargetRollCall, null, 5, "男", null);
        Assert.False(ControlDrawConditions.TryResolve(members, tooMany, out _, out var reason));
        Assert.Equal("invalid_value:count:out_of_range:1..2", reason);

        var exact = new ControlDrawTriggerRequest(ControlDrawTriggerRequest.TargetRollCall, null, 2, "男", null);
        Assert.True(ControlDrawConditions.TryResolve(members, exact, out var matched, out var exactReason), exactReason);
        Assert.Equal(2, matched.Count);
    }

    [Fact]
    public void 不写条件时抽整份名单的候选人()
    {
        var members = new[]
        {
            Student("01", "张三", "男", "第一组"),
            Student("02", "李四", "女", "第二组"),
            Student("03", "", "", "", exists: false),
            Student("  ", "  ")
        };

        var request = new ControlDrawTriggerRequest(ControlDrawTriggerRequest.TargetRollCall, null, null, null, null);

        Assert.True(ControlDrawConditions.TryResolve(members, request, out var matched, out var reason), reason);
        Assert.Equal(["01", "02"], matched.Select(student => student.Id));
    }

    // ---------------------------------------------------------------- conditions（draw.trigger.conditions v1）

    [Fact]
    public void 条件集_v1把标签与发放范围都读出来()
    {
        var parsed = ControlDrawTriggerRequest.TryParse(
            Payload("""
                    { "target": "lottery", "list_name": "元旦抽奖", "count": 2,
                      "conditions": { "version": 1, "prize_tags": ["文具", " 文具 ", "书籍"],
                                      "student_list": "高一（1）班", "gender": "男", "group": "第一组" } }
                    """),
            out var request,
            out var reason);

        Assert.True(parsed, reason);
        var conditions = Assert.IsType<ControlDrawConditionSet>(request.Conditions);
        Assert.Equal(1, conditions.Version);
        Assert.Equal(["文具", "书籍"], conditions.PrizeTags); // 去空白 + 去重
        Assert.Equal("高一（1）班", conditions.StudentList);
        Assert.Equal("男", conditions.Gender);
        Assert.Equal("第一组", conditions.Group);
    }

    [Theory]
    // version 只认 1；未来版本由**新能力名**表达，不是在这里放行。
    [InlineData("""{ "target": "lottery", "conditions": { "version": 2 } }""", "invalid_command:conditions:version_unsupported")]
    [InlineData("""{ "target": "lottery", "conditions": { "version": "1" } }""", "invalid_value:conditions.version:type_mismatch")]
    // 条件集里的键是**封闭集合**：多一个就整条拒绝，将来加字段才不会被静默忽略。
    [InlineData("""{ "target": "lottery", "conditions": { "tag_match": "all" } }""", "invalid_command:conditions:unsupported_field")]
    // 类型写错
    [InlineData("""{ "target": "lottery", "conditions": [] }""", "invalid_command:conditions:type_mismatch")]
    [InlineData("""{ "target": "lottery", "conditions": { "prize_tags": "文具" } }""", "invalid_value:prize_tags:type_mismatch")]
    [InlineData("""{ "target": "lottery", "conditions": { "prize_tags": [1] } }""", "invalid_value:prize_tags:type_mismatch")]
    // 范围必须与发放名单同现：没有名单就没有可筛的东西（奖品没有性别/分组）。
    [InlineData("""{ "target": "lottery", "conditions": { "gender": "男" } }""", "invalid_command:student_list:required")]
    [InlineData("""{ "target": "lottery", "conditions": { "group": "第一组" } }""", "invalid_command:student_list:required")]
    // 条件集只属于抽奖：点名/快抽带着它一律拒绝，不静默忽略。
    [InlineData("""{ "target": "roll_call", "conditions": { "prize_tags": ["文具"] } }""", "invalid_command:conditions:not_applicable")]
    public void 条件集写错或写不认识的东西都被拒绝(string json, string expectedReason)
    {
        Assert.False(ControlDrawTriggerRequest.TryParse(Payload(json), out _, out var reason));
        Assert.Equal(expectedReason, reason);
    }

    [Theory]
    // 空条件集折成"没有条件"：下面这条回归同时钉住"不出现 conditions 时行为与今天逐字一致"。
    [InlineData("""{ "target": "lottery", "list_name": "元旦抽奖" }""")]
    [InlineData("""{ "target": "lottery", "list_name": "元旦抽奖", "conditions": {} }""")]
    [InlineData("""{ "target": "lottery", "list_name": "元旦抽奖", "conditions": { "prize_tags": [] } }""")]
    [InlineData("""{ "target": "lottery", "list_name": "元旦抽奖", "conditions": { "student_list": "  " } }""")]
    public void 没有条件或空条件集时与老语义完全一致(string json)
    {
        var parsed = ControlDrawTriggerRequest.TryParse(Payload(json), out var request, out var reason);

        Assert.True(parsed, reason);
        Assert.Null(request.Conditions);

        // 顶层 gender/group 对抽奖仍然是 not_applicable：旧原因码一个字都没改。
        Assert.False(ControlDrawTriggerRequest.TryParse(
            Payload("""{ "target": "lottery", "gender": "男" }"""), out _, out var rejected));
        Assert.Equal("invalid_value:gender:not_applicable", rejected);
    }

    [Fact]
    public void 标签选项从奖池派生而不是写死()
    {
        var pool = new[]
        {
            Prize("P01", tags: "文具 红色"),
            Prize("P02", tags: "书籍"),
            Prize("P03", tags: "文具"),
            Prize("P04")
        };

        // Ordinal 排序：书(4E66) < 文(6587) < 红(7EA2)。选项列表只用于取值校验，顺序稳定即可。
        Assert.Equal(["书籍", "文具", "红色"], ControlDrawConditions.PrizeTagOptions(pool));
    }

    [Fact]
    public void 标签筛选是任一命中并且只留下命中的奖品()
    {
        var pool = new[]
        {
            Prize("P01", tags: "文具"),
            Prize("P02", tags: "书籍"),
            Prize("P03", tags: "零食")
        };
        var request = Lottery("元旦抽奖", 2, new ControlDrawConditionSet(1, ["文具", "书籍"], null, null, null));

        Assert.True(ControlDrawConditions.TryResolvePrizes(pool, request, out var matched, out var reason), reason);
        Assert.Equal(["P01", "P02"], matched.Select(prize => prize.Id));
    }

    [Fact]
    public void 标签不在奖池里时拒绝并指明字段()
    {
        var pool = new[] { Prize("P01", tags: "文具") };
        var request = Lottery("元旦抽奖", 1, new ControlDrawConditionSet(1, ["不存在"], null, null, null));

        Assert.False(ControlDrawConditions.TryResolvePrizes(pool, request, out _, out var reason));
        Assert.Equal("invalid_value:prize_tags:not_in_list", reason);
    }

    [Fact]
    public void 标签筛完没有奖品时拒绝()
    {
        // 标签存在于池子里（贴在一个**停用**的奖品上），但没有任何可抽的奖品带它：
        // 这时才是"筛完没有候选"，而不是"这个标签不存在"——两者的下一步处置完全不同。
        var pool = new[] { Prize("P01", tags: "文具"), Prize("P02", tags: "书籍", exists: false) };
        var request = Lottery("元旦抽奖", 1, new ControlDrawConditionSet(1, ["书籍"], null, null, null));

        Assert.False(ControlDrawConditions.TryResolvePrizes(pool, request, out _, out var reason));
        Assert.Equal("invalid_value:prize_tags:no_matching_member", reason);
    }

    [Fact]
    public void 标签筛选之后数量按筛后的候选数收敛()
    {
        var pool = new[] { Prize("P01", tags: "文具"), Prize("P02", tags: "书籍"), Prize("P03", tags: "书籍") };
        var request = Lottery("元旦抽奖", 3, new ControlDrawConditionSet(1, ["书籍"], null, null, null));

        // 整池有 3 个，但按标签只剩 2 个：上界必须是 2，否则"请求 3 个"会被放行到抽取层再失败。
        Assert.False(ControlDrawConditions.TryResolvePrizes(pool, request, out _, out var reason));
        Assert.Equal("invalid_value:count:out_of_range:1..2", reason);
    }

    [Fact]
    public void 发放名单不存在时拒绝()
    {
        var conditions = new ControlDrawConditionSet(1, [], "不存在的名单", null, null);

        Assert.False(ControlDrawConditions.TryResolveRecipients(null, conditions, out _, out var reason));
        Assert.Equal("invalid_value:student_list:not_found", reason);
    }

    [Fact]
    public void 发放范围取值不在名单里时拒绝并指明字段()
    {
        var members = new[] { Student("01", "张三", "男", "第一组") };

        Assert.False(ControlDrawConditions.TryResolveRecipients(
            members, new ControlDrawConditionSet(1, [], "高一（1）班", "女", null), out _, out var genderReason));
        Assert.Equal("invalid_value:gender:not_in_list", genderReason);

        Assert.False(ControlDrawConditions.TryResolveRecipients(
            members, new ControlDrawConditionSet(1, [], "高一（1）班", null, "第二组"), out _, out var groupReason));
        Assert.Equal("invalid_value:group:not_in_list", groupReason);
    }

    [Fact]
    public void 发放范围筛完没有学生时拒绝()
    {
        var members = new[] { Student("01", "张三", "男", "第一组") };

        Assert.False(ControlDrawConditions.TryResolveRecipients(
            members, new ControlDrawConditionSet(1, [], "高一（1）班", "女", null), out _, out var genderReason));
        Assert.Equal("invalid_value:gender:not_in_list", genderReason);

        // 取值存在、但那个人被停用了：这时才是"筛完没有可发放的对象"。
        var disabledStudent = Student("01", "张三", "女", "第一组");
        disabledStudent.Exists = false;
        var disabled = new[] { disabledStudent };
        Assert.False(ControlDrawConditions.TryResolveRecipients(
            disabled, new ControlDrawConditionSet(1, [], "高一（1）班", "女", null), out _, out var emptyReason));
        Assert.Equal("invalid_value:gender:no_matching_member", emptyReason);
    }

    [Fact]
    public void 发放范围能筛出学生时按范围收敛()
    {
        var members = new[]
        {
            Student("01", "张三", "男", "第一组"),
            Student("02", "李四", "女", "第一组"),
            Student("03", "王五", "男", "第二组")
        };

        Assert.True(ControlDrawConditions.TryResolveRecipients(
            members, new ControlDrawConditionSet(1, [], "高一（1）班", "男", "第一组"),
            out var matched, out var reason), reason);
        Assert.Equal(["01"], matched.Select(student => student.Id));
    }

    [Fact]
    public void 设备声明了条件能力()
    {
        // 控制台据此决定要不要发 conditions——没有它就只能拒绝或明说"这台设备不支持条件"。
        Assert.Contains(ControlCapabilities.DrawTriggerConditions, ControlCapabilities.Known);

        var dispatcher = File.ReadAllText(GetRepositoryPath(
            @"SecRandom/Services/ControlNode/ControlCommandDispatcher.cs"));
        Assert.Contains("ControlCapabilities.DrawTriggerConditions,", dispatcher, StringComparison.Ordinal);
    }

    private static ControlDrawTriggerRequest Lottery(
        string? listName,
        int? count,
        ControlDrawConditionSet? conditions = null) =>
        new(ControlDrawTriggerRequest.TargetLottery, listName, count, null, null, conditions);

    private static string GetRepositoryPath(string relativePath) => Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")),
        relativePath);

    // ---------------------------------------------------------------- 回执

    [Fact]
    public void 回执只带学号与姓名且空值不伪装成空串()
    {
        var detail = ControlDrawTriggerDetail.From(
            ControlDrawTriggerRequest.TargetRollCall,
            "高一（1）班",
            [Student("01", "张三"), Student("", "李四"), Student("03", "")]);

        Assert.Equal("roll_call", detail.Target);
        Assert.Equal("高一（1）班", detail.ListName);
        Assert.Equal(3, detail.Count);
        Assert.Equal("01", detail.Drawn[0].Id);
        Assert.Equal("张三", detail.Drawn[0].Name);
        Assert.Null(detail.Drawn[1].Id);
        Assert.Null(detail.Drawn[2].Name);
    }

    [Fact]
    public void 回执按协议的snake_case序列化()
    {
        var json = JsonSerializer.SerializeToElement(
            ControlDrawTriggerDetail.From(
                ControlDrawTriggerRequest.TargetRollCall,
                "高一（1）班",
                [Student("01", "张三")]),
            SecRandom.Shared.Models.ControlNode.ControlProtocolJson.Options);

        // 逐字段断言而不是比字符串：序列化器会把中文转义，比字符串只会比出编码差异。
        Assert.Equal("roll_call", json.GetProperty("target").GetString());
        Assert.Equal("高一（1）班", json.GetProperty("list_name").GetString());
        Assert.Equal(1, json.GetProperty("count").GetInt32());
        Assert.Equal("01", json.GetProperty("drawn")[0].GetProperty("id").GetString());
        Assert.Equal("张三", json.GetProperty("drawn")[0].GetProperty("name").GetString());
    }
}
