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
/// <param name="Value">已按该路径的类型与范围校验过的值（<see cref="int" /> 或 <see cref="bool" />）。</param>
public sealed record ControlSettingsChange(string Path, object Value);

/// <summary>
///     <c>settings.write</c> 可远程写入的设置白名单。
/// </summary>
/// <remarks>
///     <para>
///         为什么是白名单而不是黑名单：设置文件是整个应用的配置面，黑名单只要漏一项
///         （或者将来新增一项）就等于把那一项悄悄开放了。白名单漏项只是"暂时还不能改"，
///         看得见、也提得出需求。
///     </para>
///     <para>
///         <b>安全设置、集控自身设置、桌面集成、更新设置永远不进名单。</b>前两者是设备所有权
///         （能关掉密码／能把自己接到别的组、把地址指向别的服务器），后两者是持久化与运行面入口。
///     </para>
///     <para>
///         路径名是**协议的一部分**：控制台按它下发，改名等于让旧控制台发来的 patch 全部被拒。
///     </para>
/// </remarks>
public static class ControlSettingsWhitelist
{
    private delegate bool TryConvert(JsonElement element, out object value, out string reason);

    private sealed record PatchField(TryConvert Convert, Action<MainConfigModel, object> Apply);

    private static readonly Dictionary<string, PatchField> Fields = new(StringComparer.Ordinal)
    {
        ["roll_call.half_repeat"] =
            Integer(1, 20, static (model, value) => model.RollCallSettings.HalfRepeat = value),
        ["quick_draw.disable_after_click"] =
            Integer(1, 20, static (model, value) => model.QuickDrawSettings.DisableAfterClick = value),
        ["lottery.half_repeat"] =
            Integer(1, 20, static (model, value) => model.LotterySettings.HalfRepeat = value),

        ["notification.roll_call.enabled"] =
            Boolean(static (model, value) => model.NotificationSettings.RollCall.Enabled = value),
        ["notification.quick_draw.enabled"] =
            Boolean(static (model, value) => model.NotificationSettings.QuickDraw.Enabled = value),
        ["notification.lottery.enabled"] =
            Boolean(static (model, value) => model.NotificationSettings.Lottery.Enabled = value),

        ["voice.enable"] = Boolean(static (model, value) => model.VoiceSettings.VoiceEnable = value),
        ["voice.volume"] = Integer(0, 100, static (model, value) => model.VoiceSettings.VolumeSize = value),
        ["voice.speech_rate"] = Integer(50, 200, static (model, value) => model.VoiceSettings.SpeechRate = value)
    };

    /// <summary>可远程写入的路径清单（按路径排序，便于展示与比对）。</summary>
    public static IReadOnlyList<string> WritablePaths { get; } = [.. Fields.Keys.Order(StringComparer.Ordinal)];

    /// <summary>
    ///     校验整份 patch 并给出可应用的计划。**任何一项不可写就整体失败**，
    ///     原因里带上具体路径：否则管理员只看到"被拒绝"，分不清是自己发错了名字还是设备不支持。
    /// </summary>
    public static bool TryPlan(
        JsonElement? payload,
        out IReadOnlyList<ControlSettingsChange> changes,
        out string reason)
    {
        changes = [];
        reason = ControlRejectReasons.InvalidCommand;

        if (payload is not { ValueKind: JsonValueKind.Object } root)
            return false;

        if (!root.TryGetProperty("patch", out var patch) || patch.ValueKind != JsonValueKind.Object)
            return false;

        // 空 patch 视为无效：它既没表达意图，也不该被当成"成功执行"记进日志。
        if (!patch.EnumerateObject().Any())
            return false;

        var planned = new List<ControlSettingsChange>();

        foreach (var property in patch.EnumerateObject())
        {
            if (!Fields.TryGetValue(property.Name, out var field))
            {
                reason = $"not_writable:{property.Name}";
                return false;
            }

            if (!field.Convert(property.Value, out var value, out var detail))
            {
                reason = $"invalid_value:{property.Name}:{detail}";
                return false;
            }

            planned.Add(new ControlSettingsChange(property.Name, value));
        }

        changes = planned;
        return true;
    }

    /// <summary>把已校验的变更应用到配置模型上。调用方负责线程与落盘。</summary>
    public static void Apply(MainConfigModel model, IReadOnlyList<ControlSettingsChange> changes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(changes);

        foreach (var change in changes)
            Fields[change.Path].Apply(model, change.Value);
    }

    private static PatchField Boolean(Action<MainConfigModel, bool> apply) => new(
        (JsonElement element, out object value, out string reason) =>
        {
            if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = element.GetBoolean();
                reason = string.Empty;
                return true;
            }

            value = false;
            reason = "type_mismatch";
            return false;
        },
        (model, value) => apply(model, (bool)value));

    private static PatchField Integer(int minimum, int maximum, Action<MainConfigModel, int> apply) => new(
        (JsonElement element, out object value, out string reason) =>
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
            {
                if (number < minimum || number > maximum)
                {
                    value = 0;
                    reason = $"out_of_range:{minimum}..{maximum}";
                    return false;
                }

                value = number;
                reason = string.Empty;
                return true;
            }

            value = 0;
            reason = "type_mismatch";
            return false;
        },
        (model, value) => apply(model, (int)value));
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
///     <c>roster.write</c> 的载荷。
/// </summary>
/// <remarks>
///     这是集控里**唯一会引入学生姓名**的通道。解析阶段就要把不合法的输入挡掉：
///     名单名走与本地导入相同的校验，空名单整体拒绝（"把名单清空"不该是远程误操作的后果）。
/// </remarks>
public sealed record ControlRosterPushRequest(
    string ListName,
    string Mode,
    bool Activate,
    IReadOnlyList<Student> Students)
{
    /// <summary>单次下发的人数上限。</summary>
    public const int MaxStudents = 2000;

    public static bool TryParse(JsonElement? payload, out ControlRosterPushRequest? request, out string reason)
    {
        request = null;
        reason = ControlRejectReasons.InvalidCommand;

        if (payload is not { ValueKind: JsonValueKind.Object } root)
            return false;

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

        if (!root.TryGetProperty("students", out var studentsElement) || studentsElement.ValueKind != JsonValueKind.Array)
            return false;

        var students = new List<Student>();
        foreach (var element in studentsElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                reason = "invalid_student_entry";
                return false;
            }

            var student = new Student
            {
                Id = ReadString(element, "id"),
                Name = ReadString(element, "name"),
                Gender = ReadString(element, "gender"),
                Group = ReadString(element, "group"),
                // 缺 enabled 视为启用：兼容只发学号姓名的调用方。
                Exists = !element.TryGetProperty("enabled", out var enabledElement)
                         || enabledElement.ValueKind != JsonValueKind.False
            };

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

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? string.Empty).Trim()
            : string.Empty;
}

/// <summary><c>roster.write</c> 的合并规则。</summary>
public static class ControlRosterMerge
{
    /// <summary>
    ///     按学号匹配（学号为空时回落到姓名）：命中就**就地更新**，没命中就追加，
    ///     本地已有但载荷未提及的学生保持原样。
    /// </summary>
    /// <remarks>
    ///     就地更新而不是新建对象，是为了保住 <c>RecordId</c>——历史记录与公平性统计
    ///     都挂在它上面，重建学生等于把那个人的历史断掉。
    /// </remarks>
    public static List<Student> Merge(StudentList? existing, IReadOnlyList<Student> incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        if (existing is null)
            return [.. incoming];

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
                continue;
            }

            merged.Add(student);
            byKey[key] = student;
        }

        return merged;
    }

    internal static string MergeKey(Student student) => string.IsNullOrWhiteSpace(student.Id)
        ? $"name:{student.Name}"
        : $"id:{student.Id}";
}
