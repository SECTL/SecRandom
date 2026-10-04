using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Models.SubConfigs.Personalized;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     设置目录：这台机器上有哪些设置可读、哪些可写、值怎么校验。
/// </summary>
/// <remarks>
///     <para>
///         为什么要反射而不是再写一张表：手写的白名单只能回答"允许改哪 9 项"，回答不了
///         "这台机器还有什么设置"，控制台就永远渲染不出一份完整的设置表单，加一项设置就得同时改协议、
///         改 Core、改控制台。目录改成从 <see cref="MainConfigModel" /> 现推之后，
///         新增一个设置属性，控制台下一次读到的表单里就有它。
///     </para>
///     <para>
///         为什么仍然是白名单式（默认只开放能证明安全的）：设置文件是整个应用的配置面，
///         黑名单只要漏一项、或者将来新增一项，就等于把那一项悄悄开放了。这里**只把能渲染、
///         能安全改的类型放进来**，其余一律不出现在目录里。
///     </para>
///     <para>
///         <b>刻意跳过的类型</b>：Avalonia 的 <c>Color</c>（没有可输入的标量值）、
///         列表与字典（控制台画不出编辑器，远程改它等于让管理员盲写一个集合）、
///         通知渠道之外的嵌套对象，以及所有可空值类型（<c>int?</c>/<c>bool?</c>）、
///         仅用于旧文件迁移的 <c>Legacy*</c> 属性、<c>[JsonIgnore]</c> 兼容桥和只读派生值
///         （例如 <c>ShouldInitializeSentryTelemetry</c>：它由别的字段算出来，暴露出去只会变成
///         一个永远改不动的重复控件）。这些一律**不描述**。
///     </para>
///     <para>
///         相对的，策略性排除（安全、集控、更新、备份、桌面集成……）**照常描述但 Writable=false**：
///         控制台要能告诉管理员"这台机器有这项设置，是控制面不允许改"，而不是让人以为设备没有它，
///         于是去猜是自己名字写错了。每一类只读的原因见 <see cref="ReadOnlyPathSegments" /> 与
///         <see cref="ReadOnlyPaths" />。
///     </para>
///     <para>
///         路径名是**协议的一部分**：控制台按它下发，改名等于让旧控制台发来的 patch 全部被拒。
///         类目 id 与顺序同样已经发到了控制台，所以它们钉在 <see cref="CategoryOrder" /> 里，
///         不跟随反射顺序——分部类与源生成器会让属性的元数据顺序与源码书写顺序不一致，
///         而"表单里第几组"是用户能看见的东西。
///     </para>
/// </remarks>
public static class ControlSettingsCatalog
{
    private const string BoolType = "bool";
    private const string IntType = "int";
    private const string DoubleType = "double";
    private const string StringType = "string";
    private const string EnumType = "enum";

    /// <summary>类目 id（协议名）与顺序，与 <see cref="MainConfigModel" /> 里的属性声明顺序一致。</summary>
    private static readonly (Type Type, string Id)[] CategoryOrder =
    [
        (typeof(FloatPositionConfig), "float_position"),
        (typeof(GeneralSettingsConfig), "general"),
        (typeof(AppearanceSettingsConfig), "appearance"),
        (typeof(FairDrawSettingsConfig), "fair_draw"),
        (typeof(DefaultDrawSettingsConfig), "default_draw"),
        (typeof(RollCallSettingsConfig), "roll_call"),
        (typeof(QuickDrawSettingsConfig), "quick_draw"),
        (typeof(LotterySettingsConfig), "lottery"),
        (typeof(FloatingWindowSettingsConfig), "floating_window"),
        (typeof(NotificationSettingsConfig), "notification"),
        (typeof(SecuritySettingsConfig), "security"),
        (typeof(LinkageSettingsConfig), "linkage"),
        (typeof(VoiceSettingsConfig), "voice"),
        (typeof(HistoryManagementSettingsConfig), "history"),
        (typeof(UpdateSettingsConfig), "update"),
        (typeof(MoreSettingsConfig), "more")
    ];

    /// <summary>已发布到控制台的路径别名（自然名 → 协议名）。</summary>
    /// <remarks>
    ///     这三条是**协议的一部分**，不是命名风格问题：<c>voice.enable</c> / <c>voice.volume</c> /
    ///     <c>voice.speech_rate</c> 已经发到控制台并被它们使用，属性名 <c>VoiceEnable</c> /
    ///     <c>VolumeSize</c> 拼出来的 <c>voice.voice_enable</c> / <c>voice.volume_size</c>
    ///     会让旧控制台发来的 patch 全部被拒。其余六条已发布路径
    ///     （<c>roll_call.half_repeat</c>、<c>quick_draw.disable_after_click</c>、<c>lottery.half_repeat</c>、
    ///     <c>notification.{roll_call,quick_draw,lottery}.enabled</c>）本来就是自然名，不需要重映射，
    ///     由测试钉住。
    /// </remarks>
    private static readonly Dictionary<string, string> PathAliases = new(StringComparer.Ordinal)
    {
        ["voice.voice_enable"] = "voice.enable",
        ["voice.volume_size"] = "voice.volume",
        ["voice.speech_rate"] = "voice.speech_rate"
    };

    /// <summary>数值范围：按属性名给，不按类目给。</summary>
    /// <remarks>
    ///     同一个设置名在多个类目里出现（<c>HalfRepeat</c> 在通用/点名/快捷/抽奖四处都有），
    ///     限值就该一致：控制台看到"半次重复上限 20"，换一个类目变成不限，只会让人以为看错了。
    /// </remarks>
    private static readonly Dictionary<string, (double Min, double Max)> KnownRanges = new(StringComparer.Ordinal)
    {
        [nameof(RollCallSettingsConfig.HalfRepeat)] = (1, 20),
        [nameof(QuickDrawSettingsConfig.DisableAfterClick)] = (1, 20),
        [nameof(VoiceSettingsConfig.VolumeSize)] = (0, 100),
        [nameof(VoiceSettingsConfig.SpeechRate)] = (50, 200)
    };

    /// <summary>路径**按 <c>.</c> 拆开后某一段与这些词完全相等**就只读。</summary>
    /// <remarks>
    ///     <para>
    ///         <c>security</c>/<c>update</c> 是设备所有权（能关掉密码、能改更新源），<c>backup</c> 是持久化入口，
    ///         <c>autostart</c> 是开机集成：这些整类都不该由控制面改，整段挡住比逐条列举更难漏。
    ///     </para>
    ///     <para>
    ///         这里是**段相等**而不是子串匹配。子串规则曾经把 <c>voice.system_volume_control</c>
    ///         （系统音量控制）和 <c>more.*_control_panel_position</c>（控制面板在左还是在右）一起判成只读——
    ///         它们跟"集控"毫无关系，只是名字里带了 control，管理员看到的是"这项设备不支持远程改"这种假原因。
    ///         按段比较以后，只有真的整段叫 <c>control</c> 的路径（目前不存在：集控自身的开关、组 ID、
    ///         节点地址都住在 <c>data/config/control/node-state.json</c>，从来不在 settings.json 里）才会被挡住；
    ///         将来真出现 <c>general.control.*</c> 这类字段，它同样会被这一段规则挡住。
    ///     </para>
    /// </remarks>
    private static readonly HashSet<string> ReadOnlyPathSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "security", "update", "backup", "autostart", "protocol"
    };

    /// <summary>这些字段单独点名只读（段规则盖不住它们）。</summary>
    private static readonly HashSet<string> ReadOnlyPaths = new(StringComparer.Ordinal)
    {
        // 置顶模式：UiAccess 会请求提权并在重启后生效，不是控制面能替这台机器决定的事。
        "general.basic.main_window_topmost_mode",
        "floating_window.floating_window_topmost_mode",

        // 桌面集成/常驻：远程开自启或常驻＝远程让这个节点"关不掉"。
        "general.basic.background_resident",
        "general.basic.show_startup_window",

        // URL 协议注册：它的路径段是 url_protocol 而不是 protocol，段规则盖不住，
        // 但它和 autostart 一样是系统级集成（注册 secrandom:// 并常驻 IPC），必须逐条点名。
        "general.basic.url_protocol",

        // 模型里标着 Hidden Configs 的项：引导完成标记与四份协议/声明的"已同意"版本。
        // 远程改它们等于替这台机器的主人按下"我已阅读并同意"。
        "general.basic.guide_completed",
        "general.basic.accepted_eula_version",
        "general.basic.accepted_privacy_policy_version",
        "general.basic.accepted_gpl_version",
        "general.basic.accepted_verification_notice_version"
    };

    /// <summary>这些路径前缀整体只读。</summary>
    private static readonly string[] ReadOnlyPathPrefixes =
    [
        // 证明留存是"本机证据链还能留多久"的所有权，控制面把它调短等于替管理员销毁证据。
        "general.proof_retention."
    ];

    private static readonly IReadOnlyList<CategoryPlan> Categories;

    private static readonly Dictionary<string, FieldPlan> FieldsByPath;

    static ControlSettingsCatalog()
    {
        Categories = BuildCategories(out var fieldsByPath);
        FieldsByPath = fieldsByPath;
        WritablePaths =
        [
            .. FieldsByPath.Values
                .Where(static plan => plan.Writable)
                .Select(static plan => plan.Path)
                .Order(StringComparer.Ordinal)
        ];
    }

    /// <summary><c>settings.write</c> 接受的每一条路径（即所有 <c>Writable</c> 为真的字段）。</summary>
    /// <remarks>按路径排序：控制台拿它当"这台机器允许改什么"的清单，顺序稳定才便于比对。</remarks>
    public static IReadOnlyCollection<string> WritablePaths { get; }

    /// <summary>把 <paramref name="model" /> 上所有可读设置按类目分组返回，顺序稳定。</summary>
    /// <remarks>
    ///     只读字段也在这里出现（<c>Writable=false</c>）：控制台要能显示"有这项设置，但不许远程改"。
    ///     子配置对象缺失（反序列化出 <c>null</c>）时，那边的字段与空类目都不会出现——
    ///     目录不假装一个读不到的对象存在。
    /// </remarks>
    public static IReadOnlyList<ControlSettingCategory> Describe(MainConfigModel model) =>
        Describe(model, locale: null);

    /// <summary>
    ///     同 <see cref="Describe(MainConfigModel)" />，但按**控制台请求的语言**取标签与说明。
    /// </summary>
    /// <param name="locale">
    ///     控制台的界面语言（如 <c>zh-CN</c>）。<c>null</c> / 认不出来时按设备当前的界面语言取值。
    /// </param>
    /// <remarks>
    ///     为什么语言在请求里而不是把三语映射一次给全：三语映射等于每个字段多带 6 段文案，
    ///     五类设置一起读就会顶穿单帧上限——这不是估算，是一次真实故障（帧根本没发出去，
    ///     控制台干等到过期）。控制台一次只显示一种语言，让**请求**说它要哪一种就够了。
    /// </remarks>
    public static IReadOnlyList<ControlSettingCategory> Describe(MainConfigModel model, string? locale)
    {
        ArgumentNullException.ThrowIfNull(model);

        var categories = new List<ControlSettingCategory>(Categories.Count);
        foreach (var category in Categories)
        {
            var fields = new List<ControlSettingField>(category.Fields.Count);
            foreach (var plan in category.Fields)
            {
                var holder = plan.Holder(model);
                if (holder is null)
                    continue;

                fields.Add(new ControlSettingField(
                    plan.Path,
                    category.Id,
                    plan.Type,
                    ReadValue(plan, holder),
                    plan.Writable,
                    plan.Min,
                    plan.Max,
                    plan.Options,
                    ControlSettingsLabels.GetFieldLabel(plan.Path, locale),
                    ControlSettingsLabels.GetFieldDescription(plan.Path, locale)));
            }

            // 空类目不出现在表单里：一个没有字段的分组只会让人以为加载失败了。
            if (fields.Count > 0)
                categories.Add(new ControlSettingCategory(
                    category.Id,
                    ControlSettingsLabels.GetCategoryLabel(category.Id, locale),
                    null,
                    fields));
        }

        return categories;
    }

    /// <summary>
    ///     校验整份 patch 并给出可应用的计划。**任何一项不可写、类型不对或越界就整体失败**，
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
            // 未知路径与被描述但只读的路径给同一个原因：控制台需要知道的都是"这条我改不了"。
            if (!FieldsByPath.TryGetValue(property.Name, out var plan) || !plan.Writable)
            {
                reason = $"not_writable:{property.Name}";
                return false;
            }

            if (!TryConvert(plan, property.Value, out var value, out var detail))
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
    /// <remarks>
    ///     未知路径与只读路径**直接忽略**（而不是抛异常）：这是防御纵深，
    ///     即使将来有人绕过 <see cref="TryPlan" /> 直接拼一份变更，也不可能写到被排除的设置上。
    /// </remarks>
    public static void Apply(MainConfigModel model, IReadOnlyList<ControlSettingsChange> changes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(changes);

        foreach (var change in changes)
        {
            if (!FieldsByPath.TryGetValue(change.Path, out var plan) || !plan.Writable)
                continue;

            var holder = plan.Holder(model);
            if (holder is null)
                continue;

            plan.Property.SetValue(holder, change.Value);
        }
    }

    private static IReadOnlyList<CategoryPlan> BuildCategories(out Dictionary<string, FieldPlan> fieldsByPath)
    {
        fieldsByPath = new Dictionary<string, FieldPlan>(StringComparer.Ordinal);
        var categories = new List<CategoryPlan>();

        var roots = typeof(MainConfigModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static (property, index) => (Property: property, Index: index))
            .Where(static item => IsUsable(item.Property) && IsConfigObject(item.Property.PropertyType))
            .OrderBy(static item => RankOf(item.Property.PropertyType))
            .ThenBy(static item => item.Index)
            .ToList();

        foreach (var (root, _) in roots)
        {
            var id = ResolveCategoryId(root.PropertyType);
            var plans = new List<FieldPlan>();
            AppendCategoryFields(root, id, plans);

            // 字段按路径字母序：控制台的表单顺序不该随代码里属性的摆放位置变化。
            plans.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));

            foreach (var plan in plans)
                fieldsByPath[plan.Path] = plan;

            categories.Add(new CategoryPlan(id, plans));
        }

        return categories;
    }

    private static void AppendCategoryFields(PropertyInfo root, string categoryId, List<FieldPlan> plans)
    {
        var categoryType = root.PropertyType;
        var prefix = categoryId + ".";

        foreach (var property in ReadableProperties(categoryType))
        {
            // 通知渠道是唯一"目录里还要再下一层"的特例，而且**只暴露渠道开关与显示时长**：
            // 渠道对象自己还带着窗口位置、透明度这些外观字段，远程改它们只会让这台机器的
            // 通知弹窗位置对不上，它们属于通知窗口自己的设置。
            if (typeof(NotificationChannelSettings).IsAssignableFrom(property.PropertyType))
            {
                var channelPrefix = prefix + SnakeCase(property.Name) + ".";
                foreach (var channelField in ReadableProperties(property.PropertyType))
                {
                    if (channelField.Name is not (nameof(NotificationChannelSettings.Enabled)
                        or nameof(NotificationChannelSettings.DisplayDuration)))
                        continue;

                    TryAddScalar(root, property, channelField, channelPrefix, plans);
                }

                continue;
            }

            // 一级字段：直接挂在类目对象上（voice.volume、more.lottery_enabled ……）。
            if (TryAddScalar(root, null, property, prefix, plans))
                continue;

            // 通用设置是一层容器（general.basic / general.backup / general.privacy_settings …），
            // 只展开这一层：再往下就会把整棵配置树摊平成几百条平铺路径，控制台也没法分组。
            if (!IsConfigObject(property.PropertyType))
                continue;

            var nestedPrefix = prefix + SnakeCase(property.Name) + ".";
            foreach (var nested in ReadableProperties(property.PropertyType))
                TryAddScalar(root, property, nested, nestedPrefix, plans);
        }
    }

    private static bool TryAddScalar(
        PropertyInfo root,
        PropertyInfo? nested,
        PropertyInfo property,
        string prefix,
        List<FieldPlan> plans)
    {
        var type = ClassifyType(property.PropertyType);
        if (type is null)
            return false;

        var path = ResolvePath(prefix + SnakeCase(property.Name));

        double? minimum = null;
        double? maximum = null;
        if (KnownRanges.TryGetValue(property.Name, out var range))
        {
            minimum = range.Min;
            maximum = range.Max;
        }

        plans.Add(new FieldPlan(
            path,
            type,
            IsWritable(path),
            minimum,
            maximum,
            type == EnumType ? property.PropertyType.GetEnumNames() : null,
            ComposeGetter(root, nested),
            property));

        return true;
    }

    private static object? ReadValue(FieldPlan plan, object holder)
    {
        var raw = plan.Property.GetValue(holder);
        if (plan.Type != EnumType || raw is null)
            return raw;

        // 枚举在协议里走**名字**而不是序号：序号会随成员顺序变化，控制台也没法显示它。
        return Enum.GetName(plan.Property.PropertyType, raw) ?? raw.ToString();
    }

    private static bool TryConvert(FieldPlan plan, JsonElement element, out object value, out string detail)
    {
        value = string.Empty;
        detail = "type_mismatch";

        switch (plan.Type)
        {
            case BoolType:
                if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return false;

                value = element.GetBoolean();
                detail = string.Empty;
                return true;

            case IntType:
                if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var integer))
                    return false;

                if (!InRange(plan, integer, out detail))
                    return false;

                value = integer;
                detail = string.Empty;
                return true;

            case DoubleType:
                if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var real))
                    return false;

                if (!InRange(plan, real, out detail))
                    return false;

                value = real;
                detail = string.Empty;
                return true;

            case StringType:
                if (element.ValueKind != JsonValueKind.String)
                    return false;

                value = element.GetString() ?? string.Empty;
                detail = string.Empty;
                return true;

            case EnumType:
                if (element.ValueKind != JsonValueKind.String)
                    return false;

                var name = element.GetString() ?? string.Empty;
                if (plan.Options is null || !plan.Options.Contains(name, StringComparer.Ordinal))
                    return false;

                value = Enum.Parse(plan.Property.PropertyType, name);
                detail = string.Empty;
                return true;

            default:
                return false;
        }
    }

    private static bool InRange(FieldPlan plan, double candidate, out string detail)
    {
        detail = string.Empty;

        if ((plan.Min is { } minimum && candidate < minimum)
            || (plan.Max is { } maximum && candidate > maximum))
        {
            // 原因码用不变文化格式化：管理员看到的是 "0..100"，不该随控制台的区域设置变成 "0,100"。
            detail = FormattableString.Invariant($"out_of_range:{plan.Min}..{plan.Max}");
            return false;
        }

        return true;
    }

    private static bool IsWritable(string path)
    {
        // 段相等而不是子串：只有整段就叫 security/update/backup/autostart/protocol 的路径才是设备所有权，
        // 名字里带这些字样的普通设置（system_volume_control、*_control_panel_position）不该被误伤。
        foreach (var segment in path.Split('.'))
        {
            if (ReadOnlyPathSegments.Contains(segment))
                return false;
        }

        foreach (var prefix in ReadOnlyPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
                return false;
        }

        return !ReadOnlyPaths.Contains(path);
    }

    /// <summary>
    ///     一个设置项要进目录必须**同时可读可写**。
    /// </summary>
    /// <remarks>
    ///     只读派生值、<c>[JsonIgnore]</c> 兼容桥、只用于旧文件迁移的 <c>Legacy*</c> 属性都不是设置，
    ///     它们是历史包袱：描述出来只会得到一个改不动的重复控件。
    /// </remarks>
    private static bool IsUsable(PropertyInfo property) =>
        property.GetIndexParameters().Length == 0
        && property.GetMethod is { IsPublic: true }
        && property.SetMethod is { IsPublic: true }
        && property.GetCustomAttribute<JsonIgnoreAttribute>() is null
        && !property.Name.StartsWith("Legacy", StringComparison.Ordinal);

    private static IEnumerable<PropertyInfo> ReadableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(IsUsable);

    /// <summary>子配置对象：类是具体的、不是字符串、也不是集合。</summary>
    /// <remarks>
    ///     集合显式排除（<c>List&lt;string&gt;</c> 也是类）：控制台画不出集合编辑器，
    ///     远程盲写一个集合只会把本机已有的选择整个冲掉。结构体（<c>Color</c>、<c>Guid</c>）同样排除。
    /// </remarks>
    private static bool IsConfigObject(Type type) =>
        type is { IsClass: true, IsAbstract: false }
        && type != typeof(string)
        && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

    private static string? ClassifyType(Type type)
    {
        if (type == typeof(bool))
            return BoolType;

        if (type == typeof(int))
            return IntType;

        if (type == typeof(double))
            return DoubleType;

        if (type == typeof(string))
            return StringType;

        return type.IsEnum ? EnumType : null;
    }

    /// <summary>CLR 属性名 → 协议里的 snake_case 路径段（<c>HalfRepeat</c> → <c>half_repeat</c>）。</summary>
    private static string SnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);

        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];

            // 大驼峰边界插下划线；连续大写（缩写）只在它后面跟着小写字母时才断开，
            // 否则 IPAddress 这样的属性会被切成 i_p_address。
            if (char.IsUpper(current) && index > 0
                && (!char.IsUpper(name[index - 1])
                    || (index + 1 < name.Length && char.IsLower(name[index + 1]))))
                builder.Append('_');

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }

    private static string ResolveCategoryId(Type type)
    {
        foreach (var (candidate, id) in CategoryOrder)
        {
            if (candidate == type)
                return id;
        }

        // 表里没有的类型（将来新增的子配置）仍然会被发现：去掉 Settings/Config 后缀再转 snake_case。
        var name = type.Name;
        while (true)
        {
            if (name.EndsWith("Settings", StringComparison.Ordinal))
            {
                name = name[..^"Settings".Length];
                continue;
            }

            if (name.EndsWith("Config", StringComparison.Ordinal))
            {
                name = name[..^"Config".Length];
                continue;
            }

            break;
        }

        return SnakeCase(name);
    }

    private static int RankOf(Type type)
    {
        for (var index = 0; index < CategoryOrder.Length; index++)
        {
            if (CategoryOrder[index].Type == type)
                return index;
        }

        return int.MaxValue;
    }

    private static string ResolvePath(string path) =>
        PathAliases.TryGetValue(path, out var alias) ? alias : path;

    private static Func<MainConfigModel, object?> ComposeGetter(PropertyInfo root, PropertyInfo? nested) =>
        nested is null
            ? model => root.GetValue(model)
            : model =>
            {
                var holder = root.GetValue(model);
                return holder is null ? null : nested.GetValue(holder);
            };

    private sealed record CategoryPlan(string Id, IReadOnlyList<FieldPlan> Fields);

    private sealed record FieldPlan(
        string Path,
        string Type,
        bool Writable,
        double? Min,
        double? Max,
        IReadOnlyList<string>? Options,
        Func<MainConfigModel, object?> Holder,
        PropertyInfo Property);
}

/// <summary>一个设置分组（协议里的 <c>category</c>）。</summary>
/// <param name="Id">类目 id（如 <c>voice</c>）。</param>
/// <param name="Label">
///     类目名，按**控制台请求的语言**取自这台机器自己的设置页措辞（没请求时就是设备当前的界面语言）。
///     控制台**不再自己维护**设置分类的文案：多一处副本就多一种说法。
/// </param>
/// <param name="Description">
///     类目说明。类目只是分组，控制台已经用字段自己的说明渲染每一行，因此这里固定为 <c>null</c>；
///     保留该键是为了让控制台不必区分"没有说明"和"没下发这个字段"。
/// </param>
/// <param name="Fields">该分组里的字段，按路径字母序。</param>
public sealed record ControlSettingCategory(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("fields")] IReadOnlyList<ControlSettingField> Fields);

/// <summary>目录里的一条设置字段。</summary>
/// <param name="Path">协议路径（如 <c>voice.volume</c>），<c>settings.write</c> 就是按它下发。</param>
/// <param name="Category">所属类目 id。</param>
/// <param name="Type">
///     <c>bool</c> / <c>int</c> / <c>double</c> / <c>string</c> / <c>enum</c>。
///     控制台按它画控件；<c>enum</c> 的取值见 <paramref name="Options" />，且 <paramref name="Value" /> 是成员名而非序号。
/// </param>
/// <param name="Value">这台机器上的当前值；枚举是成员名字符串。</param>
/// <param name="Writable">是否允许 <c>settings.write</c> 写。<c>false</c> 的字段照常显示，但控制台应置灰。</param>
/// <param name="Min">数值下限，未知为 <c>null</c>。</param>
/// <param name="Max">数值上限，未知为 <c>null</c>。</param>
/// <param name="Options"><c>enum</c> 的成员名；其他类型为 <c>null</c>。</param>
/// <param name="Label">
///     字段名，按**控制台请求的语言**取自这台机器自己的设置页（如 <c>音量</c>）；
///     没请求语言时就是设备当前的界面语言（旧控制台只认它，这一语义没有变）。
///     永远非空：查不到时退化成属性名的英文短语，也绝不返回空串——空标签在控制台里只是一行没有名字的设置。
///     回退链见 <see cref="ControlSettingsLabels" />。
/// </param>
/// <param name="Description">
///     一句话说明（如 <c>播报时使用的音量大小</c>），同样来自设置页的 <c>_D</c> 文案，语言与 <paramref name="Label" /> 一致；
///     设置页与设备都没有写过说明时为 <c>null</c>，此时控制台只显示标签。
/// </param>
public sealed record ControlSettingField(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] object? Value,
    [property: JsonPropertyName("writable")] bool Writable,
    [property: JsonPropertyName("min")] double? Min = null,
    [property: JsonPropertyName("max")] double? Max = null,
    [property: JsonPropertyName("options")] IReadOnlyList<string>? Options = null,
    [property: JsonPropertyName("label")] string Label = "",
    [property: JsonPropertyName("description")] string? Description = null);
