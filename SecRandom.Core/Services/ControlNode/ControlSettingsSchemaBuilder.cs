using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SecRandom.Core.Models;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.ControlNode.V4;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     v4.2 设置 schema 的导出与校验：把"这台机器允许老师远程改"的设置整理成控制台能直接渲染的表单。
/// </summary>
/// <remarks>
///     <para>
///         <b>为什么不是整份目录</b>：<see cref="ControlSettingsCatalog" /> 会描述几百条设置，
///         包括本机开关、节点身份、路径、桌面集成与只涉及界面外观的字段。这份 schema 只收
///         <b>一位老师站在教室前面真的会想改</b>的项：抽取方式、半重复、语音播报与通知开关/时长。
///         其余设置继续留在 <c>settings.read</c> 的完整目录里，但 schema 不承诺能写，控制台也不展示。
///     </para>
///     <para>
///         <b>为什么单独一份而不是复用目录</b>：目录的 <c>Writable</c> 是"技术上有写入口"，
///         这份 schema 是"产品上允许远程写"。两者混用会把 <c>more.*</c>、<c>floating_window.*</c>
///         这类不该由控制面改的项一起暴露出去。
///     </para>
///     <para>
///         <b>服务端是最终裁判</b>：服务端的 <c>settings-schema.mjs</c> 会按同一套上限逐项校验并丢弃
///         不合法的项。本机在发送前先按同样的上限自查，宁可不报一项，也不发一份会被服务端
///         悄悄削掉一截、导致 <c>revision</c> 与实际渲染内容对不上的 schema。
///     </para>
/// </remarks>
public static class ControlSettingsSchemaBuilder
{
    public const int MaxKeys = 40;
    public const int MaxGroups = 8;
    public const int MaxEnumValues = 20;
    public const int MaxLabelCodePoints = 24;
    public const int MaxHintCodePoints = 60;
    public const int MaxKeyChars = 48;
    public const int MaxSchemaBytes = 16 * 1024;
    public const int MaxStringLength = 500;
    public const int MaxAbsNumber = 1_000_000;
    public const int MaxDurationSeconds = 86_400;

    public const string BoolType = "bool";
    public const string EnumType = "enum";
    public const string IntType = "int";
    public const string DurationType = "duration";

    public const string UnitSeconds = "seconds";

    private static readonly HashSet<string> SupportedTypes = new(StringComparer.Ordinal)
    {
        BoolType, EnumType, IntType, DurationType
    };

    private static readonly Regex KeyPattern =
        new(@"^[a-z][a-z0-9_]*(?:[._][a-z0-9_]+)*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    ///     允许远程改的设置路径，按表单分组顺序排列。
    /// </summary>
    /// <remarks>
    ///     顺序也是"超长时先丢谁"的顺序：通知在末尾，丢它比丢掉抽取方式影响小。
    /// </remarks>
    private static readonly string[] AllowedPaths =
    [
        // 默认抽取：新的抽取页从这些值起步。
        "default_draw.draw_mode",
        "default_draw.half_repeat",
        "default_draw.voice_announcement_enabled",

        // 点名。
        "roll_call.draw_mode",
        "roll_call.half_repeat",
        "roll_call.voice_announcement_enabled",

        // 闪抽。
        "quick_draw.draw_mode",
        "quick_draw.half_repeat",
        "quick_draw.disable_after_click",
        "quick_draw.voice_announcement_enabled",

        // 抽奖。
        "lottery.draw_mode",
        "lottery.half_repeat",
        "lottery.voice_announcement_enabled",

        // 语音播报：引擎、声线等是设备安装环境（系统 SAPI / Edge / OmniTTS），
        // 远程列出来只会给出一堆改不了的选项，因此只报开关、音量、语速与播报内容。
        "voice.enable",
        "voice.volume",
        "voice.speech_rate",
        "voice.voice_wait_complete",
        "voice.announce_name",
        "voice.announce_id",

        // 通知：每个渠道只报开关与显示时长，弹窗位置/透明度留在本机。
        "notification.default.enabled",
        "notification.default.display_duration",
        "notification.roll_call.enabled",
        "notification.roll_call.display_duration",
        "notification.quick_draw.enabled",
        "notification.quick_draw.display_duration",
        "notification.lottery.enabled",
        "notification.lottery.display_duration"
    ];

    /// <summary>
    ///     生成 schema。纯函数：不读写文件、不抛异常；不合法的项被丢弃并通过 <see cref="ControlSettingsSchemaResult.Issues" /> 说明。
    /// </summary>
    public static ControlSettingsSchemaResult Build(MainConfigModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var issues = new List<string>();
        var categories = ControlSettingsCatalog.Describe(model);
        var fields = new Dictionary<string, ControlSettingField>(StringComparer.Ordinal);
        var categoryLabels = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var category in categories)
        {
            categoryLabels[category.Id] = category.Label;
            foreach (var field in category.Fields)
                fields[field.Path] = field;
        }

        var ordered = new List<string>(AllowedPaths.Length);
        var schema = new Dictionary<string, ControlV4SettingSchemaField>(StringComparer.Ordinal);

        foreach (var path in AllowedPaths)
        {
            if (!KeyPattern.IsMatch(path) || path.Length > MaxKeyChars)
            {
                issues.Add($"invalid_key:{path}");
                continue;
            }

            if (!fields.TryGetValue(path, out var field))
            {
                // 这条设置在设备上暂时读不到（例如某个通知渠道对象缺失）：不报，控制台按缺省处理。
                continue;
            }

            if (TryBuildField(field, categoryLabels, out var item, out var issue))
            {
                schema[path] = item;
                ordered.Add(path);
            }
            else
            {
                issues.Add(issue);
            }
        }

        // 分组上限：组名相同的项算一组。超出上限时丢最后的项，而不是发一份服务端会整份丢弃的表单。
        var groups = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < ordered.Count;)
        {
            var path = ordered[index];
            var group = schema[path].Group;
            if (group is not null && !groups.Contains(group))
            {
                if (groups.Count >= MaxGroups)
                {
                    schema.Remove(path);
                    ordered.RemoveAt(index);
                    issues.Add($"too_many_groups:{path}");
                    continue;
                }

                groups.Add(group);
            }

            index++;
        }

        // 逐项再自查一次：即便上游目录将来放宽类型，这里也不会把不合法项发出去。
        foreach (var path in ordered.ToArray())
        {
            var item = schema[path];
            if (!IsLegal(path, item, out var detail))
            {
                schema.Remove(path);
                ordered.Remove(path);
                issues.Add($"invalid_item:{path}:{detail}");
            }
        }

        // 字节预算：按 AllowedPaths 的相反顺序丢整项，直到服务端量到的字节数回到上限内。
        var serialized = Serialize(schema);
        while (Encoding.UTF8.GetByteCount(serialized) > MaxSchemaBytes && ordered.Count > 0)
        {
            var removed = ordered[^1];
            ordered.RemoveAt(ordered.Count - 1);
            schema.Remove(removed);
            issues.Add($"schema_too_large:{removed}");
            serialized = Serialize(schema);
        }

        return new ControlSettingsSchemaResult(schema, issues);
    }

    /// <summary>把 schema 字典序列化成 hello 里的 <c>schema</c> 对象。</summary>
    public static string Serialize(IReadOnlyDictionary<string, ControlV4SettingSchemaField> schema) =>
        JsonSerializer.Serialize(schema, ControlV4Json.Options);

    /// <summary>当前 schema 的内容哈希：用于判断 revision 是否需要 +1。</summary>
    public static string ComputeHash(IReadOnlyDictionary<string, ControlV4SettingSchemaField> schema) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(schema)))).ToLowerInvariant();

    /// <summary>
    ///     单调 revision 规则：内容没变就复用；旧状态（0）或哈希缺失从 1 开始；内容变了 +1。
    /// </summary>
    /// <remarks>
    ///     只靠 revision 本身无法判断"重启后 schema 是否变了"，所以哈希必须一起落盘。
    ///     revision 溢出时保持 <see cref="int.MaxValue" />：服务端按"严格更新"接受，
    ///     回绕到负数会被当作非法 revision 拒绝。
    /// </remarks>
    public static int ResolveRevision(int storedRevision, string? storedHash, string currentHash)
    {
        if (storedRevision <= 0 || string.IsNullOrWhiteSpace(storedHash))
            return 1;

        if (string.Equals(storedHash, currentHash, StringComparison.OrdinalIgnoreCase))
            return storedRevision;

        return storedRevision == int.MaxValue ? int.MaxValue : storedRevision + 1;
    }

    /// <summary>
    ///     读取 schema 中每个键的当前值。只返回 schema 里有的键；读不到的键不出现（而不是给 <c>null</c>）。
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ReadValues(MainConfigModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var schema = Build(model).Schema;
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var category in ControlSettingsCatalog.Describe(model))
        {
            foreach (var field in category.Fields)
            {
                if (schema.ContainsKey(field.Path))
                    values[field.Path] = field.Value;
            }
        }

        return values;
    }

    /// <summary>
    ///     按 schema 校验 <c>settings.write</c> 的 patch，并给出可直接交给
    ///     <see cref="ControlSettingsCatalog.Apply" /> 的变更计划。
    /// </summary>
    /// <remarks>
    ///     整份 patch 任何一项不合法就整体失败：设备上"改了一半"比整体失败更难排查。
    ///     未知键与类型/范围错误的错误码与服务端判定一致（<c>unknown_setting</c>/<c>bad_setting_value</c>），
    ///     <paramref name="field" /> 带上具体路径，控制台才能把错误标在那一行。
    /// </remarks>
    public static bool TryPlanPatch(
        MainConfigModel model,
        JsonElement? payload,
        out IReadOnlyList<ControlSettingsChange> changes,
        out string reason,
        out string? field)
    {
        ArgumentNullException.ThrowIfNull(model);

        changes = [];
        reason = ControlRejectReasons.InvalidCommand;
        field = null;

        if (payload is not { ValueKind: JsonValueKind.Object } root)
            return false;

        if (!root.TryGetProperty("patch", out var patch) || patch.ValueKind != JsonValueKind.Object)
            return false;

        if (!patch.EnumerateObject().Any())
            return false;

        var schema = Build(model).Schema;

        foreach (var property in patch.EnumerateObject())
        {
            field = property.Name;

            if (!schema.TryGetValue(property.Name, out var schemaField))
            {
                reason = ControlRejectReasons.UnknownSetting;
                return false;
            }

            if (!TryValidateValue(schemaField, property.Value, out _))
            {
                reason = ControlRejectReasons.BadSettingValue;
                field = property.Name;
                return false;
            }
        }

        // 值类型/范围已按 schema 检查过；最后交给目录做类型转换（尤其是 enum 成员名 → 枚举值）。
        // 目录还会再校验一次可写性与范围，形成第二道防线。
        if (!ControlSettingsCatalog.TryPlan(payload, out changes, out var catalogReason))
        {
            reason = catalogReason.StartsWith("not_writable:", StringComparison.Ordinal)
                ? ControlRejectReasons.UnknownSetting
                : ControlRejectReasons.BadSettingValue;
            return false;
        }

        field = null;
        reason = string.Empty;
        return true;
    }

    private static bool TryBuildField(
        ControlSettingField field,
        IReadOnlyDictionary<string, string> categoryLabels,
        out ControlV4SettingSchemaField item,
        out string issue)
    {
        item = new ControlV4SettingSchemaField();
        issue = string.Empty;

        var label = Sanitize(field.Label, MaxLabelCodePoints);
        if (label.Length == 0)
        {
            issue = $"empty_label:{field.Path}";
            return false;
        }

        var group = categoryLabels.TryGetValue(field.Category, out var categoryLabel)
            ? Sanitize(categoryLabel, MaxLabelCodePoints)
            : string.Empty;

        var hint = Sanitize(field.Description, MaxHintCodePoints);
        var groupName = group.Length > 0 ? group : null;
        var hintText = hint.Length > 0 ? hint : null;

        switch (field.Type)
        {
            case "bool":
                if (field.Value is not bool booleanDefault)
                {
                    issue = $"bad_default:{field.Path}";
                    return false;
                }

                item = new ControlV4SettingSchemaField
                {
                    Type = BoolType,
                    Label = label,
                    Group = groupName,
                    Hint = hintText,
                    Default = booleanDefault
                };
                return true;

            case "enum":
                if (field.Options is not { Count: > 0 } options || options.Count > MaxEnumValues)
                {
                    issue = $"bad_enum:{field.Path}";
                    return false;
                }

                var values = options
                    .Select(option => Sanitize(option, MaxLabelCodePoints))
                    .Where(option => option.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .Take(MaxEnumValues)
                    .ToArray();

                if (values.Length == 0)
                {
                    issue = $"empty_enum:{field.Path}";
                    return false;
                }

                var enumDefault = field.Value as string;
                if (enumDefault is null || !values.Contains(enumDefault, StringComparer.Ordinal))
                    enumDefault = values[0];

                item = new ControlV4SettingSchemaField
                {
                    Type = EnumType,
                    Label = label,
                    Group = groupName,
                    Hint = hintText,
                    Default = enumDefault,
                    Values = values
                };
                return true;

            case "int":
                if (field.Value is not int intDefault)
                {
                    issue = $"bad_default:{field.Path}";
                    return false;
                }

                // 服务端对未声明范围的整数默认 0..100；本地目录没有给范围时按同一口径补齐，
                // 否则控制台只能画一个没有上下限的数字框。
                var intMin = field.Min ?? 0;
                var intMax = field.Max ?? 100;

                if (field.Path.EndsWith(".display_duration", StringComparison.Ordinal))
                {
                    // 通知显示时长：目录里是 int，但产品语义是"秒数"，用 duration 表达，
                    // 控制台才会按秒渲染并套用 range，而不是一个无单位的普通整数。
                    var durationMin = Math.Max(1, (int)Math.Ceiling(intMin));
                    var durationMax = Math.Min(60, (int)Math.Floor(intMax));
                    if (durationMax < durationMin)
                        durationMax = durationMin;

                    intDefault = Math.Clamp(intDefault, durationMin, durationMax);

                    item = new ControlV4SettingSchemaField
                    {
                        Type = DurationType,
                        Label = label,
                        Group = groupName,
                        Hint = hintText,
                        Default = intDefault,
                        Min = durationMin,
                        Max = durationMax,
                        Step = 1,
                        Unit = UnitSeconds
                    };
                    return true;
                }

                int defaultInRange = Math.Clamp(intDefault, (int)intMin, (int)intMax);

                item = new ControlV4SettingSchemaField
                {
                    Type = IntType,
                    Label = label,
                    Group = groupName,
                    Hint = hintText,
                    Default = defaultInRange,
                    Min = intMin,
                    Max = intMax,
                    Step = 1
                };
                return true;

            default:
                // double/string 等类型刻意不进这份 schema：老师远程改浮点权重或自由文本
                // 既没有可用的校验，也容易把设备改成现场无法解释的状态。
                issue = $"unsupported_type:{field.Path}:{field.Type}";
                return false;
        }
    }

    private static bool TryValidateValue(ControlV4SettingSchemaField field, JsonElement value, out string detail)
    {
        detail = string.Empty;

        switch (field.Type)
        {
            case BoolType:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    detail = "expected_bool";
                    return false;
                }

                return true;

            case EnumType:
                if (value.ValueKind != JsonValueKind.String)
                {
                    detail = "expected_string";
                    return false;
                }

                var text = value.GetString() ?? string.Empty;
                if (field.Values is null || !field.Values.Contains(text, StringComparer.Ordinal))
                {
                    detail = "not_in_values";
                    return false;
                }

                return true;

            case IntType:
            case DurationType:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
                {
                    detail = "expected_int";
                    return false;
                }

                var min = field.Min ?? int.MinValue;
                var max = field.Max ?? int.MaxValue;

                if (field.Type == DurationType)
                {
                    min = Math.Max(1, min);
                    max = Math.Min(MaxDurationSeconds, max);
                }

                if (number < min || number > max)
                {
                    detail = "out_of_range";
                    return false;
                }

                return true;

            default:
                detail = "unsupported_type";
                return false;
        }
    }

    private static bool IsLegal(string path, ControlV4SettingSchemaField item, out string detail)
    {
        detail = string.Empty;

        if (!KeyPattern.IsMatch(path) || path.Length > MaxKeyChars)
        {
            detail = "key";
            return false;
        }

        if (!SupportedTypes.Contains(item.Type))
        {
            detail = "type";
            return false;
        }

        if (item.Label.Length == 0 || CodePointCount(item.Label) > MaxLabelCodePoints)
        {
            detail = "label";
            return false;
        }

        if (item.Group is { Length: > 0 } group && CodePointCount(group) > MaxLabelCodePoints)
        {
            detail = "group";
            return false;
        }

        switch (item.Type)
        {
            case EnumType:
                if (item.Values is not { Count: > 0 and <= MaxEnumValues })
                {
                    detail = "values";
                    return false;
                }

                if (item.Default is not string enumDefault || !item.Values.Contains(enumDefault, StringComparer.Ordinal))
                {
                    detail = "enum_default";
                    return false;
                }

                return true;

            case IntType:
            case DurationType:
                if (item.Min is not { } min || item.Max is not { } max || min > max)
                {
                    detail = "range";
                    return false;
                }

                if (item.Default is not int intDefault || intDefault < min || intDefault > max)
                {
                    detail = "numeric_default";
                    return false;
                }

                return true;

            case BoolType:
                if (item.Default is not bool)
                {
                    detail = "bool_default";
                    return false;
                }

                return true;

            default:
                detail = "type";
                return false;
        }
    }

    /// <summary>去掉服务端会清洗的字符并截断到指定码点数，避免"设备算一份、服务端算另一份"。</summary>
    internal static string Sanitize(string? value, int maxCodePoints)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (rune.Value is '<' or '>')
                continue;

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(rune.ToString());
        }

        var text = builder.ToString().Trim();
        return TruncateCodePoints(text, maxCodePoints);
    }

    private static string TruncateCodePoints(string text, int maxCodePoints)
    {
        if (maxCodePoints <= 0 || CodePointCount(text) <= maxCodePoints)
            return text;

        var builder = new StringBuilder(text.Length);
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count++ >= maxCodePoints)
                break;
            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    private static int CodePointCount(string text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes())
            count++;
        return count;
    }
}

/// <summary>schema 组装结果：合法项 + 被丢弃/截断的项说明（用于日志，不发给服务端）。</summary>
public sealed record ControlSettingsSchemaResult(
    IReadOnlyDictionary<string, ControlV4SettingSchemaField> Schema,
    IReadOnlyList<string> Issues);