using System.Text.Json;
using SecRandom.Core.Models;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     <c>media.play</c> 的载荷。
/// </summary>
/// <remarks>
///     第一版只支持 <c>announce</c>（语音播报）。<c>show</c>（在屏幕上显示任意文本）本机还没有
///     承载它的界面，因此**明确拒绝**而不是假装成功——控制台要能区分"设备会播报"和"设备会上屏"。
/// </remarks>
public sealed record ControlMediaPlayRequest(string Action, string Text)
{
    /// <summary>播报文本上限（字符）。</summary>
    public const int MaxTextLength = 200;

    public const string AnnounceAction = "announce";

    /// <summary>把载荷解析成一个有效的播报请求；失败时给出可直接回给控制台的原因码。</summary>
    public static bool TryParse(JsonElement? payload, out ControlMediaPlayRequest? request, out string reason)
    {
        request = null;
        reason = ControlRejectReasons.InvalidCommand;

        if (payload is not { ValueKind: JsonValueKind.Object } root)
            return false;

        // 缺 action 时按最常用的播报处理；缺 text 或全空白一律算无效载荷。
        var action = AnnounceAction;
        if (root.TryGetProperty("action", out var actionElement) && actionElement.ValueKind == JsonValueKind.String)
            action = (actionElement.GetString() ?? string.Empty).Trim();

        if (!string.Equals(action, AnnounceAction, StringComparison.Ordinal))
        {
            reason = $"unsupported_action:{action}";
            return false;
        }

        if (!root.TryGetProperty("text", out var textElement) || textElement.ValueKind != JsonValueKind.String)
            return false;

        var text = (textElement.GetString() ?? string.Empty).Trim();
        if (text.Length == 0)
            return false;

        if (text.Length > MaxTextLength)
        {
            reason = "text_too_long";
            return false;
        }

        request = new ControlMediaPlayRequest(action, text);
        return true;
    }
}

/// <summary>一条经过校验的设置变更。</summary>
/// <param name="Path">协议里的路径名（如 <c>voice.volume</c>）。</param>
/// <param name="Value">已按该路径的类型与范围校验过的值（<see cref="bool" />、<see cref="int" />、<see cref="double" />、<see cref="string" /> 或枚举成员）。</param>
public sealed record ControlSettingsChange(string Path, object Value);

/// <summary>
///     <c>settings.write</c> 可远程写入的设置白名单。
/// </summary>
/// <remarks>
///     <para>
///         这个类现在只是 <see cref="ControlSettingsCatalog" /> 的对外壳：目录从
///         <c>MainConfigModel</c> 反射推导，新增一项设置不必再改这里。
///         <c>TryPlan</c> / <c>Apply</c> / <c>WritablePaths</c> 的签名与原因码保持不变，
///         因为桌面端的 <c>ControlSettingsPatchHandler</c> 和既有回归测试都按它们工作。
///     </para>
///     <para>
///         为什么仍然是白名单式：设置文件是整个应用的配置面，黑名单只要漏一项
///         （或者将来新增一项）就等于把那一项悄悄开放了。白名单漏项只是"暂时还不能改"，
///         看得见、也提得出需求。
///     </para>
///     <para>
///         <b>安全设置、集控自身设置、桌面集成、更新设置永远不进名单。</b>前两者是设备所有权
///         （能关掉密码／能把自己接到别的组、把地址指向别的服务器），后两者是持久化与运行面入口。
///         逐条排除规则见 <see cref="ControlSettingsCatalog" />。
///     </para>
///     <para>
///         路径名是**协议的一部分**：控制台按它下发，改名等于让旧控制台发来的 patch 全部被拒。
///     </para>
/// </remarks>
public static class ControlSettingsWhitelist
{
    /// <summary>可远程写入的路径清单（按路径排序，便于展示与比对）。</summary>
    public static IReadOnlyList<string> WritablePaths { get; } = [.. ControlSettingsCatalog.WritablePaths];

    /// <summary>
    ///     校验整份 patch 并给出可应用的计划。**任何一项不可写就整体失败**，
    ///     原因里带上具体路径：否则管理员只看到"被拒绝"，分不清是自己发错了名字还是设备不支持。
    /// </summary>
    public static bool TryPlan(
        JsonElement? payload,
        out IReadOnlyList<ControlSettingsChange> changes,
        out string reason) =>
        ControlSettingsCatalog.TryPlan(payload, out changes, out reason);

    /// <summary>把已校验的变更应用到配置模型上。调用方负责线程与落盘。</summary>
    public static void Apply(MainConfigModel model, IReadOnlyList<ControlSettingsChange> changes) =>
        ControlSettingsCatalog.Apply(model, changes);
}

/// <summary><c>roster.write</c> 的写入模式。</summary>
public static class RosterWriteModes
{
    /// <summary>整份覆盖：载荷里没有的学生会被删除。</summary>
    public const string Replace = "replace";

    /// <summary>按学号合并：本地已有但载荷未提及的学生保留。</summary>
    public const string Merge = "merge";

    public static bool IsKnown(string mode) =>
        string.Equals(mode, Replace, StringComparison.Ordinal) || string.Equals(mode, Merge, StringComparison.Ordinal);
}

/// <summary>
///     <c>roster.write</c> 载荷里的一名学生。
/// </summary>
/// <remarks>
///     <para>
///         不复用落盘的 <see cref="Student" />：协议里的 <c>tags</c> 是**三态**的——缺失或 <c>null</c>
///         是"这次不下发标签"，<c>[]</c> 是"明确清空"，非空是"覆盖"；而 <see cref="Student.Tags" />
///         是空格分隔的串，只有"有"和"空"两态。让这两者共用一个默认值，正是"改一个名字顺手清空
///         整台机器标签"的来源。
///     </para>
///     <para>
///         控制台只回写用户改过的行，所以"没提这一行的标签"必须能被表达出来——这就是 <c>null</c>
///         的含义。它和"清空"是两件事，任何一方都不许退化成 <c>string.Empty</c>。
///     </para>
/// </remarks>
/// <param name="Tags">
///     已规范化的标签（trim / 丢空项 / 去重，分隔符与名单导入一致）。
///     <c>null</c> = 本次未下发（合并时保留设备上的原值）；<c>[]</c> = 明确清空。
/// </param>
public sealed record ControlRosterStudentInput(
    string Id,
    string Name,
    string Gender,
    string Group,
    bool Exists,
    IReadOnlyList<string>? Tags = null)
{
    /// <summary>把这条输入落成一个名单成员（<c>replace</c> 重建与 <c>merge</c> 追加共用）。</summary>
    /// <remarks>
    ///     没下发标签时**显式**落成空串：<c>replace</c> 是按载荷重建整份名单，载荷里没给就是
    ///     设备上没有，不能指望 <see cref="Student.Tags" /> 的默认值"恰好是空的"。
    ///     合并模式命中已有成员时走不到这里，否则一次改名就会清掉那台机器的标签。
    /// </remarks>
    public Student ToStudent() => new()
    {
        Id = Id,
        Name = Name,
        Gender = Gender,
        Group = Group,
        Exists = Exists,
        Tags = Tags is { Count: > 0 } tags ? string.Join(' ', tags) : string.Empty
    };
}

/// <summary>
///     <c>roster.write</c> 载荷里的一个奖项。
/// </summary>
/// <remarks>
///     <para>
///         与学生输入同一套做法，也不复用落盘的 <see cref="Prize" />：协议里的 <c>tags</c> 是三态的
///         （缺失或 <c>null</c> 是"这次不下发标签"，<c>[]</c> 是"明确清空"，非空是"覆盖"），
///         而 <see cref="Prize.Tags" /> 是空格分隔的串，只有"有"和"空"两态。
///     </para>
///     <para>
///         <c>count</c> / <c>weight</c> **不是**三态：控制台回写的是它从 <c>roster.read</c> 读回来的整行，
///         这两个数它一直带着；缺省只出现在手写载荷里，那时按 <see cref="Prize" /> 自己的默认值（各 1）算，
///         与学生载荷里缺省的 <c>gender</c>/<c>group</c> 落成空串是同一条规则——**载荷是整行，不是补丁**。
///     </para>
/// </remarks>
/// <param name="Tags">
///     已规范化的标签（trim / 丢空项 / 去重，分隔符与名单导入一致）。
///     <c>null</c> = 本次未下发（合并时保留设备上的原值）；<c>[]</c> = 明确清空。
/// </param>
public sealed record ControlRosterPrizeInput(
    string Id,
    string Name,
    int Count,
    double Weight,
    bool Exists,
    IReadOnlyList<string>? Tags = null)
{
    /// <summary>把这条输入落成一个奖品（<c>replace</c> 重建与 <c>merge</c> 追加共用）。</summary>
    /// <remarks>没下发标签时显式落成空串，理由同 <see cref="ControlRosterStudentInput.ToStudent" />。</remarks>
    public Prize ToPrize() => new()
    {
        Id = Id,
        Name = Name,
        Count = Count,
        Weight = Weight,
        Exists = Exists,
        Tags = Tags is { Count: > 0 } tags ? string.Join(' ', tags) : string.Empty
    };
}

/// <summary>
///     <c>roster.write</c> 的载荷。
/// </summary>
/// <remarks>
///     <para>
///         这是集控里**唯一会引入学生姓名**的通道。解析阶段就要把不合法的输入挡掉：
///         名单名走与本地导入相同的校验，空名单整体拒绝（"把名单清空"不该是远程误操作的后果）。
///     </para>
///     <para>
///         点名名单与奖池共用这一条命令，靠 <c>roster_kind</c> 分流（数组名也跟着换）。
///         **缺省是点名名单**：旧控制台根本不发这个字段，它们的载荷必须与从前逐字同义。
///     </para>
/// </remarks>
/// <param name="Students">点名名单的成员；奖池载荷下为空。</param>
/// <param name="RosterKind"><c>students</c> 或 <c>prizes</c>。</param>
/// <param name="Prizes">奖池的奖品；点名载荷下为 <c>null</c>。</param>
public sealed record ControlRosterPushRequest(
    string ListName,
    string Mode,
    bool Activate,
    IReadOnlyList<ControlRosterStudentInput> Students,
    string RosterKind = ControlRosterPushRequest.StudentsKind,
    IReadOnlyList<ControlRosterPrizeInput>? Prizes = null)
{
    /// <summary>单次下发的人数上限。</summary>
    public const int MaxStudents = 2000;

    /// <summary>单次下发的奖品条数上限，**就是 <see cref="MaxStudents" />**。</summary>
    /// <remarks>
    ///     控制台对两种名单用同一条上限（<c>ROSTER_MAX_ROWS</c>），设备这边另写一个数只会有一种结局：
    ///     控制台按 2000 放行、设备按另一个数拒绝，用户看到的是"校验通过的载荷被判超限"。
    /// </remarks>
    public const int MaxPrizes = MaxStudents;

    /// <summary>点名名单：缺省类型。旧控制台不带 <c>roster_kind</c>，只能按它处理。</summary>
    public const string StudentsKind = ControlRosterReadRequest.Students;

    /// <summary>抽奖奖池：同一条能力，数组名换成 <c>prizes</c>。</summary>
    /// <remarks>复用读通道的常量：同一个协议值在两个通道里必须逐字一致，各抄一份字面量迟早会分叉。</remarks>
    public const string PrizesKind = ControlRosterReadRequest.Prizes;

    /// <summary>缺省的数量与权重：与 <see cref="Prize" /> 自己的属性默认值一致。</summary>
    private const int DefaultCount = 1;

    private const double DefaultWeight = 1;

    /// <summary>本次下发的是不是奖池。</summary>
    public bool IsPrizeRoster => string.Equals(RosterKind, PrizesKind, StringComparison.Ordinal);

    public static bool TryParse(JsonElement? payload, out ControlRosterPushRequest? request, out string reason)
    {
        request = null;
        reason = ControlRejectReasons.InvalidCommand;

        if (payload is not { ValueKind: JsonValueKind.Object } root)
            return false;

        // 先分流再校验：类型决定后面该读哪个数组，判不出类型就没法继续。
        var kind = StudentsKind;
        if (root.TryGetProperty("roster_kind", out var kindElement) && kindElement.ValueKind == JsonValueKind.String)
            kind = (kindElement.GetString() ?? string.Empty).Trim();

        if (!string.Equals(kind, StudentsKind, StringComparison.Ordinal)
            && !string.Equals(kind, PrizesKind, StringComparison.Ordinal))
        {
            // 与读通道同一个原因码：同一个字段在两个通道里必须说同一句话。
            reason = $"unsupported_roster_kind:{kind}";
            return false;
        }

        if (!root.TryGetProperty("list_name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
            return false;

        var listName = (nameElement.GetString() ?? string.Empty).Trim();
        if (listName.Length == 0)
        {
            reason = "invalid_list_name";
            return false;
        }

        var mode = RosterWriteModes.Replace;
        if (root.TryGetProperty("mode", out var modeElement) && modeElement.ValueKind == JsonValueKind.String)
        {
            mode = (modeElement.GetString() ?? string.Empty).Trim();
            if (!RosterWriteModes.IsKnown(mode))
            {
                reason = $"unsupported_mode:{mode}";
                return false;
            }
        }

        var activate = root.TryGetProperty("activate", out var activateElement)
                       && activateElement.ValueKind == JsonValueKind.True;

        if (string.Equals(kind, PrizesKind, StringComparison.Ordinal))
            return TryParsePrizes(root, listName, mode, activate, out request, out reason);

        if (!root.TryGetProperty("students", out var studentsElement) || studentsElement.ValueKind != JsonValueKind.Array)
            return false;

        var students = new List<ControlRosterStudentInput>();
        foreach (var element in studentsElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                reason = "invalid_student_entry";
                return false;
            }

            if (!TryReadTags(element, out var tags))
            {
                // 认得这个字段但值不合法就拒绝：把坏掉的 tags 当成"这次没下发"，
                // 控制台会以为标签已经写进去了，而设备上什么都没变。
                reason = ControlRejectReasons.InvalidCommand;
                return false;
            }

            var student = new ControlRosterStudentInput(
                ReadString(element, "id"),
                ReadString(element, "name"),
                ReadString(element, "gender"),
                ReadString(element, "group"),
                // 缺 enabled 视为启用：兼容只发学号姓名的调用方。
                !element.TryGetProperty("enabled", out var enabledElement)
                || enabledElement.ValueKind != JsonValueKind.False,
                tags);

            // 只排除"学号与姓名都空"的行（与本地导入同一条规则）。
            // **不能**用 IsCandidate 过滤：它要求 Exists（启用），而"已停用"是名单里的正常状态
            // （转学、休学），把它们丢掉等于远程下发一次就悄悄删人。
            if (string.IsNullOrWhiteSpace(student.Id) && string.IsNullOrWhiteSpace(student.Name))
                continue;

            students.Add(student);
        }

        if (students.Count == 0)
        {
            reason = "empty_roster";
            return false;
        }

        if (students.Count > MaxStudents)
        {
            reason = $"roster_too_large:{MaxStudents}";
            return false;
        }

        request = new ControlRosterPushRequest(listName, mode, activate, students);
        return true;
    }

    /// <summary>解析奖池载荷（<c>roster_kind: "prizes"</c> + <c>prizes[]</c>）。</summary>
    /// <remarks>
    ///     校验次序、原因码与点名那一侧逐条对应：结构错 → <c>invalid_command</c>，
    ///     某一行坏掉 → <c>invalid_prize_entry</c>，一行都不剩 → <c>empty_roster</c>，超上限 → <c>roster_too_large</c>。
    ///     坏 <c>tags</c> 用的仍是点名那侧的 <c>invalid_command</c>（<see cref="TryReadTags" />），
    ///     两个通道对同一个字段说的话必须一致。
    /// </remarks>
    private static bool TryParsePrizes(
        JsonElement root,
        string listName,
        string mode,
        bool activate,
        out ControlRosterPushRequest? request,
        out string reason)
    {
        request = null;
        reason = ControlRejectReasons.InvalidCommand;

        if (!root.TryGetProperty("prizes", out var prizesElement) || prizesElement.ValueKind != JsonValueKind.Array)
            return false;

        var prizes = new List<ControlRosterPrizeInput>();
        foreach (var element in prizesElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                reason = "invalid_prize_entry";
                return false;
            }

            if (!TryReadTags(element, out var tags))
            {
                reason = ControlRejectReasons.InvalidCommand;
                return false;
            }

            if (!TryReadCount(element, out var count) || !TryReadWeight(element, out var weight))
            {
                reason = "invalid_prize_entry";
                return false;
            }

            var prize = new ControlRosterPrizeInput(
                ReadString(element, "id"),
                ReadString(element, "name"),
                count,
                weight,
                // 缺 enabled 视为启用：与学生那一侧同一条规则。
                !element.TryGetProperty("enabled", out var enabledElement)
                || enabledElement.ValueKind != JsonValueKind.False,
                tags);

            // 只排除"编号与奖品名都空"的行（与本地导入、点名名单同一条规则）。
            if (string.IsNullOrWhiteSpace(prize.Id) && string.IsNullOrWhiteSpace(prize.Name))
                continue;

            prizes.Add(prize);
        }

        if (prizes.Count == 0)
        {
            reason = "empty_roster";
            return false;
        }

        if (prizes.Count > MaxPrizes)
        {
            reason = $"roster_too_large:{MaxPrizes}";
            return false;
        }

        request = new ControlRosterPushRequest(listName, mode, activate, [], PrizesKind, prizes);
        return true;
    }

    /// <summary>读一个奖品的 <c>count</c>；缺失或 <c>null</c> 用默认数量，给了就必须是整数。</summary>
    /// <remarks>
    ///     按 double 判定而不是 <c>TryGetInt32</c>：JSON 里 <c>2.0</c> 与 <c>2</c> 是同一个数，
    ///     手写载荷里带小数点的整数不该被判成坏数据；而 <c>2.5</c> 份奖品是没有意义的。
    ///     <c>null</c> 与缺失一视同仁，与"空串等于没有"是同一条协议约定。
    /// </remarks>
    private static bool TryReadCount(JsonElement element, out int count)
    {
        count = DefaultCount;

        if (!element.TryGetProperty("count", out var countElement) || countElement.ValueKind == JsonValueKind.Null)
            return true;

        if (countElement.ValueKind != JsonValueKind.Number
            || !countElement.TryGetDouble(out var raw)
            || !double.IsFinite(raw)
            || raw != Math.Floor(raw)
            || raw < int.MinValue
            || raw > int.MaxValue)
            return false;

        count = (int)raw;
        return true;
    }

    /// <summary>读一个奖品的 <c>weight</c>；缺失或 <c>null</c> 用默认权重，给了就必须是数。</summary>
    private static bool TryReadWeight(JsonElement element, out double weight)
    {
        weight = DefaultWeight;

        if (!element.TryGetProperty("weight", out var weightElement) || weightElement.ValueKind == JsonValueKind.Null)
            return true;

        if (weightElement.ValueKind != JsonValueKind.Number
            || !weightElement.TryGetDouble(out var parsed)
            || !double.IsFinite(parsed))
            return false;

        weight = parsed;
        return true;
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? string.Empty).Trim()
            : string.Empty;

    /// <summary>读一条学生输入里的 <c>tags</c>；返回 <c>false</c> 表示格式不合法。</summary>
    /// <remarks>
    ///     缺失与显式 <c>null</c> 都是 <c>null</c>（本次不下发），<c>[]</c> 给出空列表（明确清空）。
    ///     数组里混进非字符串、或者整个字段不是数组，就整条拒绝，而不是挑出能用的部分——
    ///     挑拣会让控制台拿到"成功"，却不知道自己发错的标签被丢了一半。
    ///     规范化复用读通道的 <see cref="ControlRosterMemberPayload.NormalizeTags" />：先按空格拼回一个串，
    ///     元素里自带的逗号/分号/竖线也按导入那套分隔符拆开，读出去什么样、写回来就该是什么样。
    /// </remarks>
    private static bool TryReadTags(JsonElement element, out IReadOnlyList<string>? tags)
    {
        tags = null;

        if (!element.TryGetProperty("tags", out var tagsElement) || tagsElement.ValueKind == JsonValueKind.Null)
            return true;

        if (tagsElement.ValueKind != JsonValueKind.Array)
            return false;

        var raw = new List<string>();
        foreach (var tagElement in tagsElement.EnumerateArray())
        {
            if (tagElement.ValueKind != JsonValueKind.String)
                return false;

            raw.Add(tagElement.GetString() ?? string.Empty);
        }

        // 给了标签但规范化后一个都不剩（全是空白）：那是"明确清空"，不能退化成"没下发"。
        tags = ControlRosterMemberPayload.NormalizeTags(string.Join(' ', raw)) ?? [];
        return true;
    }
}

/// <summary><c>roster.write</c> 的合并规则。</summary>
public static class ControlRosterMerge
{
    /// <summary>
    ///     按学号匹配（学号为空时回落到姓名）：命中就**就地更新**，没命中就追加，
    ///     本地已有但载荷未提及的学生保持原样。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         就地更新而不是新建对象，是为了保住 <c>RecordId</c>——历史记录与公平性统计
    ///         都挂在它上面，重建学生等于把那个人的历史断掉。
    ///     </para>
    ///     <para>
    ///         标签只在**本次下发了**的时候才写（<see cref="ControlRosterStudentInput.Tags" /> 为
    ///         <c>null</c> 就是没下发）。控制台常常只改一个人的名字，把"没提"当成"清空"
    ///         会让这一次改名变成整台机器的标签全灭。
    ///     </para>
    /// </remarks>
    public static List<Student> Merge(StudentList? existing, IReadOnlyList<ControlRosterStudentInput> incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        if (existing is null)
            return [.. incoming.Select(student => student.ToStudent())];

        var byKey = new Dictionary<string, Student>(StringComparer.Ordinal);
        var merged = new List<Student>(existing.Students);

        foreach (var student in merged)
            byKey[MergeKey(student)] = student;

        foreach (var student in incoming)
        {
            var key = MergeKey(student);
            if (byKey.TryGetValue(key, out var current))
            {
                current.Id = student.Id;
                current.Name = student.Name;
                current.Gender = student.Gender;
                current.Group = student.Group;
                current.Exists = student.Exists;

                // 没下发标签就保持设备上的原值：这一行是"改个名字不该毁掉标签"的落点。
                if (student.Tags is { } tags)
                    current.Tags = string.Join(' ', tags);

                continue;
            }

            var added = student.ToStudent();
            merged.Add(added);
            byKey[key] = added;
        }

        return merged;
    }

    internal static string MergeKey(Student student) => MergeKey(student.Id, student.Name);

    internal static string MergeKey(ControlRosterStudentInput student) => MergeKey(student.Id, student.Name);

    /// <summary>
    ///     按编号匹配（编号为空时回落到奖品名）：命中就**就地更新**，没命中就追加，
    ///     本地已有但载荷未提及的奖品保持原样。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         与 <see cref="Merge(StudentList?, IReadOnlyList{ControlRosterStudentInput})" /> 同一条规则，
    ///         连"为什么不能新建对象"的理由都一样：抽奖历史与公平性统计挂在 <c>RecordId</c> 上，
    ///         重建奖品等于把那个奖项的历史断掉（奖品被改了名字也要跟着它）。
    ///     </para>
    ///     <para>
    ///         标签只在**本次下发了**的时候才写（<see cref="ControlRosterPrizeInput.Tags" /> 为
    ///         <c>null</c> 就是没下发）；数量与权重是整行的字段，命中时照载荷覆盖。
    ///     </para>
    /// </remarks>
    public static List<Prize> MergePrizes(PrizeList? existing, IReadOnlyList<ControlRosterPrizeInput> incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        if (existing is null)
            return [.. incoming.Select(prize => prize.ToPrize())];

        var byKey = new Dictionary<string, Prize>(StringComparer.Ordinal);
        var merged = new List<Prize>(existing.Prizes);

        foreach (var prize in merged)
            byKey[MergeKey(prize)] = prize;

        foreach (var prize in incoming)
        {
            var key = MergeKey(prize);
            if (byKey.TryGetValue(key, out var current))
            {
                current.Id = prize.Id;
                current.Name = prize.Name;
                current.Count = prize.Count;
                current.Weight = prize.Weight;
                current.Exists = prize.Exists;

                // 没下发标签就保持设备上的原值：这一行是"改个奖品名不该毁掉标签"的落点。
                if (prize.Tags is { } tags)
                    current.Tags = string.Join(' ', tags);

                continue;
            }

            var added = prize.ToPrize();
            merged.Add(added);
            byKey[key] = added;
        }

        return merged;
    }

    internal static string MergeKey(Prize prize) => MergeKey(prize.Id, prize.Name);

    internal static string MergeKey(ControlRosterPrizeInput prize) => MergeKey(prize.Id, prize.Name);

    private static string MergeKey(string id, string name) =>
        string.IsNullOrWhiteSpace(id) ? $"name:{name}" : $"id:{id}";
}
