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
///         条件的**取值合法性**不在这里判定：解析阶段不知道这台机器上有哪些名单、哪个名单里有谁，
///         那些判断归 <see cref="ControlDrawConditions" />（拿到名单成员之后）与调用方（名单是否存在）。
///     </para>
/// </remarks>
/// <param name="Target">抽取目标：<see cref="TargetQuick" />（快抽，默认）或 <see cref="TargetRollCall" />（点名）。</param>
/// <param name="ListName">点名抽取要用的名单名；空表示用本机当前的默认点名名单。</param>
/// <param name="Count">抽取人数；空表示 1。只对点名有意义（快抽固定抽 1 个）。</param>
/// <param name="Gender">性别条件；空表示不限。取值必须存在于该名单。</param>
/// <param name="Group">分组条件；空表示不限。取值必须存在于该名单。</param>
public sealed record ControlDrawTriggerRequest(
    string Target,
    string? ListName,
    int? Count,
    string? Gender,
    string? Group)
{
    /// <summary>快抽：按本机快抽默认名单抽 1 个。旧控制台的按钮就是这个。</summary>
    public const string TargetQuick = "quick";

    /// <summary>点名：在指定名单、指定条件下用点名会话抽取，结果留在教室机屏幕上。</summary>
    public const string TargetRollCall = "roll_call";

    /// <summary>单次远程抽取的人数上限。</summary>
    /// <remarks>
    ///     真正的上限是名单里符合条件的人数（见 <see cref="ControlDrawConditions.TryResolve" />）；
    ///     这个数只是"一眼看去就不像课堂操作"的粗闸门，免得一个手滑的 99999 被当成合法请求一路带到抽取层。
    /// </remarks>
    public const int MaxCount = 200;

    /// <summary>旧控制台（不带 payload）对应的请求。</summary>
    public static ControlDrawTriggerRequest QuickTarget { get; } = new(TargetQuick, null, null, null, null);

    public bool IsRollCall => string.Equals(Target, TargetRollCall, StringComparison.Ordinal);

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
            if (target is not (TargetQuick or TargetRollCall))
            {
                reason = "invalid_value:target:unsupported";
                return false;
            }
        }

        if (!TryReadOptionalText(element, "list_name", out var listName, out reason)
            || !TryReadOptionalText(element, "gender", out var gender, out reason)
            || !TryReadOptionalText(element, "group", out var group, out reason))
            return false;

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

        reason = string.Empty;
        request = new ControlDrawTriggerRequest(target, listName, count, gender, group);
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

    private static IReadOnlyList<string> Options(IEnumerable<Student> members, Func<Student, string> selector) =>
        members
            .Select(selector)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
}

/// <summary>回执里"抽到了谁"的一条记录。</summary>
/// <remarks>
///     只带学号与姓名：这是回执，不是名单查询——回执会进控制台日志与手机屏幕，
///     带的字段越少，被顺手记进别处的越多。两者都可能为空（名单允许只有姓名或只有学号）。
/// </remarks>
public sealed record ControlDrawnMember(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name);

/// <summary>把抽到的成员投影成回执里的最小记录。</summary>
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
