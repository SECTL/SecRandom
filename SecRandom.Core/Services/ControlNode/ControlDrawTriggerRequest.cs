using System.Text.Json;
using System.Text.Json.Serialization;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     <c>draw.trigger</c> 的载荷：一次远程抽取要抽谁、抽几个。
/// </summary>
/// <remarks>
///     <para>
///         为什么要有参数：最早的 <c>draw.trigger</c> 不带任何字段，语义只有"按这台机器的快抽默认名单
///         抽一次"。控制台能按的按钮，手机端却要"选名单 → 选条件 → 抽取"，这两者差的是**谁能决定抽谁**：
///         没有参数时，手机只能触发老师事先配好的那一档，等于把选择权留在教室机上。
///     </para>
///     <para>
///         <b>缺省 <c>target=quick</c>、缺省全部条件</b>，所以**不带 payload 的旧控制台按钮行为完全不变**：
///         它仍然走快抽默认名单，只是现在还会带回抽到了谁。向后兼容在这里不是"尽力而为"，
///         而是协议要求——旧控制台已经在生产里发了命令。
///     </para>
///     <para>
///         <b>抽奖与点名共用一条通道，但不共用条件。</b>点名可以按性别/分组筛人，奖品没有这两个属性，
///         所以 <c>target=lottery</c> 时 <c>gender</c>/<c>group</c> **存在即拒绝**
///         （<c>invalid_value:gender:not_applicable</c> / <c>invalid_value:group:not_applicable</c>）。
///         刻意不静默忽略：控制台/手机里留着一个"筛选条件"输入框、发过来却被丢掉，
///         用户会以为"按这个条件抽的"，而结果是按整池抽的——这种"看起来生效了"的谎言比直接拒绝更难发现。
///     </para>
///     <para>
///         条件的**取值合法性**不在这里判定：解析阶段不知道这台机器上有哪些名单、哪个名单里有谁，
///         那些判断归 <see cref="ControlDrawConditions" />（拿到名单成员或奖池奖品之后）与调用方（名单/奖池是否存在）。
///     </para>
/// </remarks>
/// <param name="Target">
///     抽取目标：<see cref="TargetQuick" />（快抽，默认）、<see cref="TargetRollCall" />（点名）
///     或 <see cref="TargetLottery" />（抽奖）。
/// </param>
/// <param name="ListName">点名要用的名单名 / 抽奖要用的奖池名；空表示用本机当前的默认点名名单或默认奖池。</param>
/// <param name="Count">抽取人数（点名）或数量（抽奖）；空表示 1。快抽固定抽 1 个。</param>
/// <param name="Gender">性别条件；空表示不限。取值必须存在于该名单；抽奖时存在即拒绝。</param>
/// <param name="Group">分组条件；空表示不限。取值必须存在于该名单；抽奖时存在即拒绝。</param>
/// <param name="Conditions">
///     抽奖专用条件集（v1：奖品标签 + 发放对象范围）。**点名/快抽出现即拒绝**——它们是抽奖的维度，
///     悄悄忽略等于"设了条件其实没生效"。见 <see cref="ControlDrawConditionSet" />。
/// </param>
public sealed record ControlDrawTriggerRequest(
    string Target,
    string? ListName,
    int? Count,
    string? Gender,
    string? Group,
    ControlDrawConditionSet? Conditions = null)
{
    /// <summary>快抽：按本机快抽默认名单抽 1 个。旧控制台的按钮就是这个。</summary>
    public const string TargetQuick = "quick";

    /// <summary>点名：在指定名单、指定条件下用点名会话抽取，结果留在教室机屏幕上。</summary>
    public const string TargetRollCall = "roll_call";

    /// <summary>抽奖：在指定奖池里用抽奖会话抽取，结果同样留在教室机屏幕上。</summary>
    public const string TargetLottery = "lottery";

    /// <summary>单次远程抽取的人数上限。</summary>
    /// <remarks>
    ///     真正的上限是名单里符合条件的人数（见 <see cref="ControlDrawConditions.TryResolve" />）
    ///     或奖池里的可用奖品数（见 <see cref="ControlDrawConditions.TryResolvePrizes" />）；
    ///     这个数只是"一眼看去就不像课堂操作"的粗闸门，免得一个手滑的 99999 被当成合法请求一路带到抽取层。
    /// </remarks>
    public const int MaxCount = 200;

    /// <summary>旧控制台（不带 payload）对应的请求。</summary>
    public static ControlDrawTriggerRequest QuickTarget { get; } = new(TargetQuick, null, null, null, null);

    public bool IsRollCall => string.Equals(Target, TargetRollCall, StringComparison.Ordinal);

    public bool IsLottery => string.Equals(Target, TargetLottery, StringComparison.Ordinal);

    /// <summary>解析载荷；失败时 <paramref name="reason" /> 是可直接回给控制台的原因码。</summary>
    /// <remarks>
    ///     结构坏掉（不是对象）算 <c>invalid_command</c>；字段本身写错（类型不对、目标不认识、人数越界）
    ///     算 <c>invalid_value:&lt;字段&gt;:&lt;为什么&gt;</c>——管理员看的是"我哪个字段填错了"，
    ///     而不是一句"载荷无效"。
    /// </remarks>
    public static bool TryParse(JsonElement? payload, out ControlDrawTriggerRequest request, out string reason)
    {
        request = QuickTarget;
        reason = ControlRejectReasons.InvalidCommand;

        // 没有载荷＝旧控制台：保持"快抽默认名单"的老行为。显式的 JSON null 同样按"没写"处理，
        // 因为"不带参数"是这条命令的既有语义，一个 null 不等于控制台想表达别的意思。
        if (payload is not { } element || element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return true;

        if (element.ValueKind != JsonValueKind.Object)
            return false;

        var target = TargetQuick;
        if (element.TryGetProperty("target", out var targetElement))
        {
            if (targetElement.ValueKind != JsonValueKind.String)
            {
                reason = "invalid_value:target:type_mismatch";
                return false;
            }

            target = (targetElement.GetString() ?? string.Empty).Trim();
            if (target is not (TargetQuick or TargetRollCall or TargetLottery))
            {
                reason = "invalid_value:target:unsupported";
                return false;
            }
        }

        if (!TryReadOptionalText(element, "list_name", out var listName, out reason)
            || !TryReadOptionalText(element, "gender", out var gender, out reason)
            || !TryReadOptionalText(element, "group", out var group, out reason))
            return false;

        // 抽奖没有性别/分组这两个维度：**存在即拒绝**，不静默丢掉（理由见类型注释）。
        // 顺序固定 gender 在前：两个都写了时先报性别，控制台改完一个再看下一个。
        if (string.Equals(target, TargetLottery, StringComparison.Ordinal))
        {
            if (gender is not null)
            {
                reason = "invalid_value:gender:not_applicable";
                return false;
            }

            if (group is not null)
            {
                reason = "invalid_value:group:not_applicable";
                return false;
            }
        }

        int? count = null;
        if (element.TryGetProperty("count", out var countElement))
        {
            if (countElement.ValueKind != JsonValueKind.Number || !countElement.TryGetInt32(out var parsed))
            {
                reason = "invalid_value:count:type_mismatch";
                return false;
            }

            if (parsed < 1 || parsed > MaxCount)
            {
                reason = $"invalid_value:count:out_of_range:1..{MaxCount}";
                return false;
            }

            count = parsed;
        }

        // 条件集只属于抽奖：点名/快抽带着它一律拒绝（不接受再忽略）。
        ControlDrawConditionSet? conditions = null;
        if (element.TryGetProperty(ConditionsFieldName, out var conditionsElement))
        {
            if (!string.Equals(target, TargetLottery, StringComparison.Ordinal))
            {
                reason = $"invalid_command:{ConditionsFieldName}:not_applicable";
                return false;
            }

            if (!ControlDrawConditionSet.TryParse(conditionsElement, out conditions, out reason))
                return false;

            if (conditions is { IsEmpty: true })
                conditions = null;
        }

        reason = string.Empty;
        request = new ControlDrawTriggerRequest(target, listName, count, gender, group, conditions);
        return true;
    }

    /// <summary>条件子对象的字段名（协议里就这一个名字，改它等于改协议）。</summary>
    public const string ConditionsFieldName = "conditions";

    private static bool TryReadOptionalText(JsonElement element, string name, out string? value, out string reason)
    {
        value = null;
        reason = string.Empty;

        if (!element.TryGetProperty(name, out var property))
            return true;

        if (property.ValueKind != JsonValueKind.String)
        {
            reason = $"invalid_value:{name}:type_mismatch";
            return false;
        }

        var text = (property.GetString() ?? string.Empty).Trim();
        value = text.Length == 0 ? null : text;
        return true;
    }
}

/// <summary>
///     <c>draw.trigger.conditions</c>：抽奖专用的条件集（版本 1）。
/// </summary>
/// <remarks>
///     <para>
///         <b>为什么单独一个子对象，而不是往顶层加字段</b>：本次改动之前的设备不认识 <c>conditions</c>，
///         于是会把它整体当成"没写"——但那正是我们要禁止的静默路径，所以真正的闸门是**能力声明**
///         （<c>draw.trigger.conditions</c>，控制台只在设备声明了它时才发这个子对象）。
///         子对象在这里的职责是另外两件事：给未来留版本位，以及让"这个设备认识的键"成为一个**封闭集合**——
///         里面出现不认识的键就整条拒绝，将来加字段绝不会被悄悄忽略。
///     </para>
///     <para>
///         <b>v1 的三个条件</b>：<see cref="PrizeTags" />（按标签筛奖品，**任一命中**）、
///         <see cref="StudentList" />（奖品发给这个名单的学生）、以及只在后者存在时有意义的
///         <see cref="Gender" />/<see cref="Group" />（筛的是**接收奖品的学生的范围**，不是奖品属性——
///         奖品没有性别与分组，这正是顶层 <c>gender</c>/<c>group</c> 在抽奖档仍被拒的原因）。
///     </para>
/// </remarks>
/// <param name="Version">条件集版本；v1 只接受 1，缺省即 1。</param>
/// <param name="PrizeTags">只抽带这些标签的奖品（任一命中）；空集合等价于不筛。</param>
/// <param name="StudentList">奖品的发放对象名单；出现即开启"奖品指定给学生"。</param>
/// <param name="Gender">发放对象的性别范围；**必须与 <paramref name="StudentList" /> 同现**。</param>
/// <param name="Group">发放对象的分组范围；**必须与 <paramref name="StudentList" /> 同现**。</param>
public sealed record ControlDrawConditionSet(
    int Version,
    IReadOnlyList<string> PrizeTags,
    string? StudentList,
    string? Gender,
    string? Group)
{
    /// <summary>本设备认识的 <c>conditions</c> 键的**封闭集合**；多一个键就整条拒绝。</summary>
    private static readonly string[] KnownFields = ["version", "prize_tags", "student_list", "gender", "group"];

    public const int CurrentVersion = 1;

    /// <summary>三样条件都没写（用来把"空条件集"折成"没有条件"，语义与不带 conditions 完全一致）。</summary>
    public bool IsEmpty =>
        PrizeTags.Count == 0 && StudentList is null && Gender is null && Group is null;

    /// <summary>解析条件子对象；失败原因直接回给控制台。</summary>
    public static bool TryParse(
        JsonElement element,
        out ControlDrawConditionSet? conditions,
        out string reason)
    {
        conditions = null;
        reason = string.Empty;

        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return true;

        if (element.ValueKind != JsonValueKind.Object)
        {
            reason = $"invalid_command:{ControlDrawTriggerRequest.ConditionsFieldName}:type_mismatch";
            return false;
        }

        // 未知键一律拒绝：这是**将来加字段仍然安全**的唯一保证。
        foreach (var property in element.EnumerateObject())
        {
            if (!KnownFields.Contains(property.Name, StringComparer.Ordinal))
            {
                reason = $"invalid_command:{ControlDrawTriggerRequest.ConditionsFieldName}:unsupported_field";
                return false;
            }
        }

        var version = CurrentVersion;
        if (element.TryGetProperty("version", out var versionElement))
        {
            if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out version))
            {
                reason = "invalid_value:conditions.version:type_mismatch";
                return false;
            }

            if (version != CurrentVersion)
            {
                reason = "invalid_command:conditions:version_unsupported";
                return false;
            }
        }

        var tags = Array.Empty<string>();
        if (element.TryGetProperty("prize_tags", out var tagsElement))
        {
            if (tagsElement.ValueKind != JsonValueKind.Array)
            {
                reason = "invalid_value:prize_tags:type_mismatch";
                return false;
            }

            var parsed = new List<string>();
            foreach (var tagElement in tagsElement.EnumerateArray())
            {
                if (tagElement.ValueKind != JsonValueKind.String)
                {
                    reason = "invalid_value:prize_tags:type_mismatch";
                    return false;
                }

                var tag = (tagElement.GetString() ?? string.Empty).Trim();
                if (tag.Length > 0 && !parsed.Contains(tag, StringComparer.Ordinal))
                    parsed.Add(tag);
            }

            tags = [.. parsed];
        }

        if (!TryReadOptionalText(element, "student_list", out var studentList, out reason)
            || !TryReadOptionalText(element, "gender", out var gender, out reason)
            || !TryReadOptionalText(element, "group", out var group, out reason))
            return false;

        // 没有发放对象名单时，性别/分组**没有可筛的东西**（奖品没有这两个属性）。
        // 这里必须拒绝而不是丢掉：否则控制台以为"按第一组发的"，实际是整池发。
        if (studentList is null && (gender is not null || group is not null))
        {
            reason = "invalid_command:student_list:required";
            return false;
        }

        conditions = new ControlDrawConditionSet(version, tags, studentList, gender, group);
        return true;
    }

    private static bool TryReadOptionalText(JsonElement element, string name, out string? value, out string reason)
    {
        value = null;
        reason = string.Empty;

        if (!element.TryGetProperty(name, out var property))
            return true;

        if (property.ValueKind != JsonValueKind.String)
        {
            reason = $"invalid_value:{name}:type_mismatch";
            return false;
        }

        var text = (property.GetString() ?? string.Empty).Trim();
        value = text.Length == 0 ? null : text;
        return true;
    }
}

/// <summary>
///     抽取条件的取值域：哪些性别/分组是这个名单里真实存在的，以及一份条件能筛出谁。
/// </summary>
/// <remarks>
///     <para>
///         条件的选项必须**从名单成员派生**，不能写死"男/女"和"第一组…第四组"：
///         名单里的分组名是老师自己输入的，写死的选项意味着手机上一个条件都选不对，
///         而设备侧又会把不存在的取值当成非法输入拒绝——两边一起错，用户只看到"抽不了"。
///     </para>
///     <para>
///         <b>候选取 <see cref="Student.IsCandidate" /></b>（启用且学号或姓名非空），与本地抽取的候选池同一条规则：
///         条件校验如果按"名单里有这个人"来判，那么一个全部停用的分组会通过校验然后在抽取时变成
///         "没有可抽取的成员"，管理员的下一步就无从下手。
///     </para>
/// </remarks>
public static class ControlDrawConditions
{
    /// <summary>名单里出现过的性别（去重、稳定排序）。空值不出现。</summary>
    public static IReadOnlyList<string> GenderOptions(IEnumerable<Student> members) => Options(members, static member => member.Gender);

    /// <summary>名单里出现过的分组（去重、稳定排序）。空值不出现。</summary>
    public static IReadOnlyList<string> GroupOptions(IEnumerable<Student> members) => Options(members, static member => member.Group);

    /// <summary>
    ///     按条件筛出可抽取的成员；<paramref name="reason" /> 是拒绝时可直接回给控制台的
    ///     <c>invalid_value:&lt;字段&gt;:&lt;为什么&gt;</c>。
    /// </summary>
    /// <remarks>
    ///     判定顺序就是管理员排查的顺序：先看条件取值在不在名单里（"这个班有第一组吗"），
    ///     再看筛完还剩不剩人（"第一组的人是不是都停用了"），最后才看人数超没超（"第一组只有 3 个人，你抽 5 个"）。
    /// </remarks>
    public static bool TryResolve(
        IReadOnlyList<Student> members,
        ControlDrawTriggerRequest request,
        out IReadOnlyList<Student> matched,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(request);

        matched = [];
        reason = string.Empty;

        if (request.Gender is { } gender && !GenderOptions(members).Contains(gender, StringComparer.Ordinal))
        {
            reason = "invalid_value:gender:not_in_list";
            return false;
        }

        if (request.Group is { } group && !GroupOptions(members).Contains(group, StringComparer.Ordinal))
        {
            reason = "invalid_value:group:not_in_list";
            return false;
        }

        var candidates = members
            .Where(static member => member.IsCandidate)
            .Where(member => request.Gender is null || string.Equals(member.Gender, request.Gender, StringComparison.Ordinal))
            .Where(member => request.Group is null || string.Equals(member.Group, request.Group, StringComparison.Ordinal))
            .ToList();

        if (candidates.Count == 0)
        {
            reason = request.Gender is not null
                ? "invalid_value:gender:no_matching_member"
                : request.Group is not null
                    ? "invalid_value:group:no_matching_member"
                    : "invalid_value:list_name:no_candidate";
            return false;
        }

        if (request.Count is { } count && count > candidates.Count)
        {
            reason = $"invalid_value:count:out_of_range:1..{candidates.Count}";
            return false;
        }

        matched = candidates;
        return true;
    }

    /// <summary>
    ///     校验"奖品发给谁"的范围；<paramref name="students" /> 为 <c>null</c> 表示该学生名单不存在。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         这是抽奖的**第二个维度**：奖品本身没有性别与分组，但"发给哪个范围的学生"有。
    ///         顶层 <c>gender</c>/<c>group</c> 在抽奖档仍按 <c>not_applicable</c> 拒绝，
    ///         抽奖要表达范围必须同时给出 <c>conditions.student_list</c>（解析阶段已强制这一条）。
    ///     </para>
    ///     <para>
    ///         取值同样必须来自该名单（<c>not_in_list</c>），筛完没人给 <c>no_matching_member</c>：
    ///         与点名那条一模一样的三段判定顺序，管理员排查时看到的说法也一致。
    ///     </para>
    /// </remarks>
    public static bool TryResolveRecipients(
        IReadOnlyList<Student>? students,
        ControlDrawConditionSet conditions,
        out IReadOnlyList<Student> matched,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(conditions);

        matched = [];
        reason = string.Empty;

        if (students is null)
        {
            reason = "invalid_value:student_list:not_found";
            return false;
        }

        if (conditions.Gender is { } gender && !GenderOptions(students).Contains(gender, StringComparer.Ordinal))
        {
            reason = "invalid_value:gender:not_in_list";
            return false;
        }

        if (conditions.Group is { } group && !GroupOptions(students).Contains(group, StringComparer.Ordinal))
        {
            reason = "invalid_value:group:not_in_list";
            return false;
        }

        var candidates = students
            .Where(static student => student.IsCandidate)
            .Where(student => conditions.Gender is null
                              || string.Equals(student.Gender, conditions.Gender, StringComparison.Ordinal))
            .Where(student => conditions.Group is null
                              || string.Equals(student.Group, conditions.Group, StringComparison.Ordinal))
            .ToList();

        if (candidates.Count == 0)
        {
            reason = conditions.Gender is not null
                ? "invalid_value:gender:no_matching_member"
                : conditions.Group is not null
                    ? "invalid_value:group:no_matching_member"
                    : "invalid_value:student_list:no_matching_member";
            return false;
        }

        matched = candidates;
        return true;
    }

    private static IReadOnlyList<string> Options(IEnumerable<Student> members, Func<Student, string> selector) =>
        members
            .Select(selector)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>奖池里出现过的标签（去重、稳定排序）。空值不出现。</summary>
    /// <remarks>
    ///     归一化复用 <c>roster.read</c> 的 <see cref="ControlRosterMemberPayload.NormalizeTags" />：
    ///     控制台先用 <c>roster.read</c> 拿到可选标签、再用同一批值发条件，
    ///     两边合一处的解析才不会出现"列表里有的标签，发过去说不存在"。
    /// </remarks>
    public static IReadOnlyList<string> PrizeTagOptions(IEnumerable<Prize> prizes)
    {
        ArgumentNullException.ThrowIfNull(prizes);

        return prizes
            .SelectMany(prize => ControlRosterMemberPayload.NormalizeTags(prize.Tags) ?? [])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     按奖池校验一次抽奖请求；奖池不存在（<paramref name="prizes" /> 为 <c>null</c>）也在这里拒绝。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>先筛后数</b>：标签筛选先作用在候选池上，<c>count</c> 的上界是**筛选之后**的候选数——
    ///         否则"按标签只剩 2 个奖品，却按整池的 5 个放行"会在抽取层变成一次说不清的失败。
    ///         调用方拿到 <paramref name="matched" /> 之后还会按本机剩余库存再收敛一次。
    ///     </para>
    ///     <para>
    ///         奖品本身仍然没有性别/分组：抽奖要表达"发给哪个范围的学生"必须用
    ///         <see cref="TryResolveRecipients" />（顶层 gender/group 在抽奖档依旧是 <c>not_applicable</c>）。
    ///     </para>
    ///     <para>
    ///         把"奖池不存在"也收进来，是为了让"名字写错"与"奖池里没有奖品"各有各的原因码：
    ///         调用方只要把从目录里读到的奖池快照递进来（读不到就递 <c>null</c>），
    ///         不必自己拼原因码，也不必先判断一次存在性再判断一次数量。
    ///     </para>
    ///     <para>
    ///         候选取 <see cref="Prize.IsCandidate" />（启用且编号或奖品名非空），与本地抽取的候选池同一条规则。
    ///     </para>
    /// </remarks>
    public static bool TryResolvePrizes(
        IReadOnlyList<Prize>? prizes,
        ControlDrawTriggerRequest request,
        out IReadOnlyList<Prize> matched,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(request);

        matched = [];
        reason = string.Empty;

        if (prizes is null)
        {
            reason = "invalid_value:list_name:not_found";
            return false;
        }

        var candidates = prizes.Where(static prize => prize.IsCandidate).ToList();
        if (candidates.Count == 0)
        {
            reason = "invalid_value:list_name:no_candidate";
            return false;
        }

        if (request.Conditions is { PrizeTags.Count: > 0 } conditions)
        {
            // 取值域取**整个奖池**（含停用的奖品），而不是只看候选：控制台是按 `roster.read` 的成员
            // 派生选项的，那份成员带着 `enabled` 标记、也包含停用的奖品。若这里只认候选的标签，
            // 控制台会拿到一个"列表里有、发过去说不存在"的值，而真实原因其实是"那个奖品被停用了"。
            var available = PrizeTagOptions(prizes);
            foreach (var tag in conditions.PrizeTags)
            {
                if (!available.Contains(tag, StringComparer.Ordinal))
                {
                    reason = "invalid_value:prize_tags:not_in_list";
                    return false;
                }
            }

            // v1 语义：**任一命中**（OR）。标签是老师自己打的，一个奖品同时带两个标签在真实用法里罕见，
            // 交集会永远筛空——多一个 tag_match 开关只是多一种把课堂抽空的方式。
            candidates = candidates
                .Where(prize => (ControlRosterMemberPayload.NormalizeTags(prize.Tags) ?? [])
                    .Any(tag => conditions.PrizeTags.Contains(tag, StringComparer.Ordinal)))
                .ToList();

            if (candidates.Count == 0)
            {
                reason = "invalid_value:prize_tags:no_matching_member";
                return false;
            }
        }

        if (request.Count is { } count && count > candidates.Count)
        {
            reason = $"invalid_value:count:out_of_range:1..{candidates.Count}";
            return false;
        }

        matched = candidates;
        return true;
    }
}

/// <summary>回执里"抽到了谁"的一条记录。</summary>
/// <remarks>
///     只带编号与名称：这是回执，不是名单查询——回执会进控制台日志与手机屏幕，
///     带的字段越少，被顺手记进别处的越多。两者都可能为空（名单允许只有姓名或只有学号）。
///     抽奖回执用的是奖品的编号与奖品名，形状完全一样——控制台与手机不需要区分两种实体。
/// </remarks>
public sealed record ControlDrawnMember(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name);

/// <summary>把抽到的成员或奖品投影成回执里的最小记录。</summary>
public static class ControlDrawnMembers
{
    public static IReadOnlyList<ControlDrawnMember> FromStudents(IEnumerable<Student> students) =>
        [.. students.Select(FromStudent)];

    public static ControlDrawnMember FromStudent(Student student)
    {
        ArgumentNullException.ThrowIfNull(student);

        return new ControlDrawnMember(
            string.IsNullOrWhiteSpace(student.Id) ? null : student.Id.Trim(),
            string.IsNullOrWhiteSpace(student.Name) ? null : student.Name.Trim());
    }

    /// <summary>抽奖回执：奖品的编号与名称（<c>Prize</c> 与 <c>Student</c> 的可见字段同名同义）。</summary>
    public static IReadOnlyList<ControlDrawnMember> FromPrizes(IEnumerable<Prize> prizes) =>
        [.. prizes.Select(FromPrize)];

    public static ControlDrawnMember FromPrize(Prize prize)
    {
        ArgumentNullException.ThrowIfNull(prize);

        return new ControlDrawnMember(
            string.IsNullOrWhiteSpace(prize.Id) ? null : prize.Id.Trim(),
            string.IsNullOrWhiteSpace(prize.Name) ? null : prize.Name.Trim());
    }
}

/// <summary><c>draw.trigger</c> 成功回执的 detail。</summary>
/// <remarks>
///     <c>count</c> 是**实际抽到的人数**，不是请求里的人数：点名会按候选池收敛，
///     控制台与手机拿到这个数才能知道"我请求 3 个、实际抽到 3 个"还是"名单里只剩 2 个"。
/// </remarks>
public sealed record ControlDrawTriggerDetail(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("list_name")] string? ListName,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("drawn")] IReadOnlyList<ControlDrawnMember> Drawn)
{
    public static ControlDrawTriggerDetail From(string target, string? listName, IEnumerable<Student> drawn)
    {
        var members = ControlDrawnMembers.FromStudents(drawn);
        return new ControlDrawTriggerDetail(target, listName, members.Count, members);
    }
}
