using System.Text.Json;
using System.Text.Json.Serialization;
using SecRandom.Core.Services.Profiles;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     <c>roster.read</c> 的请求：读这台机器上的名单或奖池。
/// </summary>
/// <remarks>
///     读名单是集控里**最敏感的一条只读通道**（含学生姓名），因此它按 Admin 授权，
///     并且默认**不返回已停用的学生**——停用通常就是"这个人不该再被抽到"，
///     把停用名单整份摊到控制台上没有收益。
/// </remarks>
public sealed record ControlRosterReadRequest(
    [property: JsonPropertyName("roster_kind")] string RosterKind,
    [property: JsonPropertyName("list_name")] string? ListName,
    [property: JsonPropertyName("include_disabled")] bool IncludeDisabled)
{
    public const string Students = "students";
    public const string Prizes = "prizes";

    /// <summary>一次最多返回多少份名单/奖池（控制台要的是"有哪些"，不是全量导出）。</summary>
    public const int MaxLists = 50;

    /// <summary>
    ///     每份名单最多返回多少人。单帧上限 64 KiB，超出后按 <c>truncated</c> 标记截断。
    /// </summary>
    public const int MaxMembersPerList = 500;

    public static bool TryParse(JsonElement? payload, out ControlRosterReadRequest? request, out string reason)
    {
        request = null;
        reason = ControlRejectReasons.InvalidCommand;

        var kind = Students;
        string? listName = null;
        var includeDisabled = false;

        if (payload is { ValueKind: JsonValueKind.Object } root)
        {
            if (root.TryGetProperty("roster_kind", out var kindElement) && kindElement.ValueKind == JsonValueKind.String)
                kind = (kindElement.GetString() ?? string.Empty).Trim();

            if (root.TryGetProperty("list_name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                var candidate = (nameElement.GetString() ?? string.Empty).Trim();
                listName = candidate.Length == 0 ? null : candidate;
            }

            includeDisabled = root.TryGetProperty("include_disabled", out var disabledElement)
                              && disabledElement.ValueKind == JsonValueKind.True;
        }
        else if (payload is not null and not { ValueKind: JsonValueKind.Null })
        {
            // 载荷存在但不是对象 → 明确拒绝，而不是"当作没传"。
            return false;
        }

        if (!string.Equals(kind, Students, StringComparison.Ordinal)
            && !string.Equals(kind, Prizes, StringComparison.Ordinal))
        {
            reason = $"unsupported_roster_kind:{kind}";
            return false;
        }

        request = new ControlRosterReadRequest(kind, listName, includeDisabled);
        return true;
    }
}

/// <summary><c>roster.read</c> 的返回：按名单分组的成员。</summary>
public sealed record ControlRosterReadResponse(
    [property: JsonPropertyName("roster_kind")] string RosterKind,
    [property: JsonPropertyName("lists")] IReadOnlyList<ControlRosterListPayload> Lists)
{
    /// <summary>
    ///     把响应缩到载荷预算之内：必要时丢掉末尾的成员，并把 <c>truncated</c> 标出来。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         为什么不是"超了就整条失败"：名单读取**有前缀可用**——控制台要的是"有哪些名单、
    ///         大概多少人"，看到前 300 人远比一条读不到更有用，而 <c>count</c>/<c>total</c>/<c>truncated</c>
    ///         三个字段本来就是为这件事设计的（界面上那句"只回传了前 N 条"就是它）。
    ///     </para>
    ///     <para>
    ///         成员的体积差别很大（有人带标签、有人不带，有人带特殊语音），所以先按实测字节估一个能留下的人数，
    ///         再实测一次收敛；最多收紧几轮就停，绝不无限循环。
    ///     </para>
    /// </remarks>
    public static ControlRosterReadResponse TrimToBudget(ControlRosterReadResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var current = response;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var bytes = ControlProtocolJson.MeasureBytes(current);
            if (bytes <= ControlProtocolJson.PayloadBudgetBytes)
                return current;

            var members = current.Lists.Sum(list => list.Members.Count);
            if (members == 0)
                return current;

            // 按实测比例缩，再留 2% 余量；至少砍掉一个，否则估算原地不动会白跑一轮。
            var ratio = (double)ControlProtocolJson.PayloadBudgetBytes / bytes;
            var target = (int)Math.Floor(members * ratio * 0.98);
            if (target >= members)
                target = members - 1;

            current = ShrinkTo(current, target);
        }

        return current;
    }

    /// <summary>按名单顺序把总成员数缩到 <paramref name="target" />，各份按原有人数等比例保留。</summary>
    private static ControlRosterReadResponse ShrinkTo(ControlRosterReadResponse response, int target)
    {
        var total = response.Lists.Sum(list => list.Members.Count);
        var lists = new List<ControlRosterListPayload>(response.Lists.Count);
        var remaining = Math.Max(0, target);

        for (var index = 0; index < response.Lists.Count; index++)
        {
            var list = response.Lists[index];
            var share = total == 0
                ? 0
                : (int)Math.Floor((double)list.Members.Count / total * target);

            // 最后一份吃掉取整误差，这样比例缩完的总数正好是 target。
            var keep = index == response.Lists.Count - 1
                ? Math.Min(list.Members.Count, remaining)
                : Math.Min(list.Members.Count, share);
            keep = Math.Clamp(keep, 0, remaining);
            remaining -= keep;

            lists.Add(list with
            {
                Count = keep,
                // `Total` 是"过滤后该名单真实有多少人"，因此 `keep < Total` 就是"这次没给全"。
                Truncated = list.Truncated || keep < list.Total,
                Members = keep == list.Members.Count ? list.Members : [.. list.Members.Take(keep)]
            });
        }

        return response with { Lists = lists };
    }
}

/// <param name="Name">名单/奖池名。</param>
/// <param name="IsDefault">是否是本机当前默认使用的那一份。</param>
/// <param name="Count">返回的成员数。</param>
/// <param name="Total">该名单的实际成员数（截断前）。</param>
/// <param name="Truncated">是否因为单帧上限被截断。</param>
public sealed record ControlRosterListPayload(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("is_default")] bool IsDefault,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("members")] IReadOnlyList<ControlRosterMemberPayload> Members);

/// <param name="Id">学号 / 奖品编号（可为空：名单允许只有姓名）。</param>
/// <param name="Count">奖品的数量（学生为 <c>null</c>）。</param>
/// <param name="Weight">奖品的权重（学生为 <c>null</c>）。</param>
/// <param name="Tags">
///     标签。客户端把标签存成一个空格分隔的串（导入时就是这么规范化的），这里拆成数组，
///     控制台才能像本机列表页那样逐条渲染"标签"列。没有标签时为 <c>null</c>。
/// </param>
/// <param name="SpecificVoiceAlias">
///     「特殊语音」的 TTS 别名（协议字段 <c>specific_voice_alias</c>）。
///     只有附加设置开关打开、且确实填了值的人才报；没设置时为 <c>null</c>。
/// </param>
/// <param name="SpecificVoicePrefix">「特殊语音」的播报前缀（协议字段 <c>specific_voice_prefix</c>），同上。</param>
/// <param name="SpecificVoiceSuffix">「特殊语音」的播报后缀（协议字段 <c>specific_voice_suffix</c>），同上。</param>
/// <remarks>
///     三个 <c>specific_voice_*</c> 字段是**同一件事**（见 <see cref="ControlSpecificVoiceValues" />），
///     三态与 <c>tags</c> 一致：没值时不写出键，写通道里"缺字段"是"这次不下发"、空串是"明确清空"。
/// </remarks>
public sealed record ControlRosterMemberPayload(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("gender")] string? Gender,
    [property: JsonPropertyName("group")] string? Group,
    [property: JsonPropertyName("count")] int? Count,
    [property: JsonPropertyName("weight")] double? Weight,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("tags")] IReadOnlyList<string>? Tags = null,
    [property: JsonPropertyName(ControlSpecificVoiceValues.AliasField)]
    string? SpecificVoiceAlias = null,
    [property: JsonPropertyName(ControlSpecificVoiceValues.PrefixField)]
    string? SpecificVoicePrefix = null,
    [property: JsonPropertyName(ControlSpecificVoiceValues.SuffixField)]
    string? SpecificVoiceSuffix = null)
{
    /// <summary>把一个学生投影成控制台看到的成员。</summary>
    /// <remarks>
    ///     <para>
    ///         投影留在 Core 而不是列表读取处理器里：**哪一项是"没有值"、哪一项是"有值"** 就是在这里定的，
    ///         放在应用层就只能靠人读代码，放在这里可以被单元测试逐条钉住。
    ///     </para>
    ///     <para>
    ///         特殊语音三个值一次读出来（<see cref="ControlSpecificVoiceValues.Read" />）：它要按附加设置开关
    ///         判"有没有生效"，逐字段各读一次既是三倍的 JsonElement 反序列化，也容易只改一处而漏掉另两处。
    ///     </para>
    /// </remarks>
    public static ControlRosterMemberPayload FromStudent(Student student)
    {
        var voice = ControlSpecificVoiceValues.Read(student);
        return new ControlRosterMemberPayload(
            NullIfBlank(student.Id),
            NullIfBlank(student.Name),
            NullIfBlank(student.Gender),
            NullIfBlank(student.Group),
            null,
            null,
            student.Exists,
            NormalizeTags(student.Tags),
            voice.Alias,
            voice.Prefix,
            voice.Suffix);
    }

    /// <summary>把一个奖品投影成控制台看到的成员（学生独有的性别与分组对奖品是 <c>null</c>）。</summary>
    /// <remarks>
    ///     特殊语音对学生与奖品是**同一套字段、同一个存储键**（控件声明的
    ///     <c>AttachedSettingsTargets.Student | AttachedSettingsTargets.Prize</c>），所以这里与
    ///     <see cref="FromStudent" /> 逐条相同。
    /// </remarks>
    public static ControlRosterMemberPayload FromPrize(Prize prize)
    {
        var voice = ControlSpecificVoiceValues.Read(prize);
        return new ControlRosterMemberPayload(
            NullIfBlank(prize.Id),
            NullIfBlank(prize.Name),
            null,
            null,
            prize.Count,
            prize.Weight,
            prize.Exists,
            NormalizeTags(prize.Tags),
            voice.Alias,
            voice.Prefix,
            voice.Suffix);
    }

    /// <summary>把列表里的标签串拆成协议里的标签数组；没有标签时返回 <c>null</c>。</summary>
    /// <remarks>
    ///     <para>
    ///         拆分沿用名单导入的 <see cref="RosterImportParser.SplitTags" />：去空白、丢空项、去掉重复，
    ///         并且认得逗号/分号/竖线这些分隔符。导入就是按它把标签规范化成一个空格分隔的串存下来的，
    ///         读出去时用同一条规则，才不会出现"导入三个标签、控制台看到四个"。
    ///     </para>
    ///     <para>
    ///         没有标签时给 <c>null</c> 而不是空数组——空数组和缺失在协议里都是"没有"，但成员是这条命令里
    ///         数量最多的对象，<c>null</c> 能省掉每一个成员的 <c>"tags":[]</c>（帧上限 64 KiB）。
    ///     </para>
    /// </remarks>
    public static IReadOnlyList<string>? NormalizeTags(string? tags) =>
        RosterImportParser.SplitTags(tags ?? string.Empty) is { Count: > 0 } split ? split : null;

    /// <summary>空字符串与缺失在协议里是同一件事：都没有值，不要用 <c>""</c> 冒充有值。</summary>
    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
