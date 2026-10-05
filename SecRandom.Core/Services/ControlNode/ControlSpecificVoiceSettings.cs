using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Interfaces;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     一名成员（学生或奖品）身上的「特殊语音」附加设置，正是列表页那个「附加设置」浮出控件里的三个文本框。
/// </summary>
/// <remarks>
///     <para>
///         三个值都存在成员自己的 <see cref="IAttachableSettingsObject.AttachedObjects" /> 里，
///         键是 <see cref="SettingsId" />（与 <c>AttachedSettingsControlInfo</c> 的 Guid 是同一个），
///         界面上的 <c>SpecificAnnouncementAttachedSettingsControl</c> 就是绑在它上面的
///         <c>TtsAlias</c> / <c>Prefix</c> / <c>Suffix</c> 三个属性。
///     </para>
///     <para>
///         <b>三态</b>：<c>null</c> = 本次不下发（写通道）/ 没有值（读通道），空串 = 明确清空。
///         与 <c>tags</c> 同一条规则，理由也一样——"没提这一项"绝不能退化成"清空这一项"，
///         否则控制台改一个学生姓名的整份回写就会抹掉别人的专属语音。
///     </para>
///     <para>
///         字段名常量只写在这里一处：读载荷的 <c>JsonPropertyName</c> 与写载荷的取值共用它，
///         两边各抄一份字面量迟早会分叉，而分叉的表现是"控制台写进去、设备当成没传"——一个安静的 bug。
///     </para>
/// </remarks>
/// <param name="Alias">读出来的 TTS 别名 / 写进去的 TTS 别名（协议字段 <see cref="AliasField" />）。</param>
/// <param name="Prefix">播报前缀（协议字段 <see cref="PrefixField" />）。</param>
/// <param name="Suffix">播报后缀（协议字段 <see cref="SuffixField" />）。</param>
public readonly record struct ControlSpecificVoiceValues(string? Alias, string? Prefix, string? Suffix)
{
    /// <summary>读通道成员对象里的 TTS 别名字段。</summary>
    public const string AliasField = "specific_voice_alias";

    /// <summary>读通道成员对象里的播报前缀字段。</summary>
    public const string PrefixField = "specific_voice_prefix";

    /// <summary>读通道成员对象里的播报后缀字段。</summary>
    public const string SuffixField = "specific_voice_suffix";

    /// <summary>特殊语音在 <c>AttachedObjects</c> 里的键。</summary>
    /// <remarks>
    ///     与控件注册时用的 Guid 逐字相同（<c>SpecificAnnouncementAttachedSettingsControl</c> 上的
    ///     <c>AttachedSettingsControlInfo</c>）：键一旦对不上，读出来永远是"这个人没设置过"。
    /// </remarks>
    public static Guid SettingsId { get; } = Guid.Parse(GlobalConstants.SpecificAnnouncementAttachedSettings);

    /// <summary>三个字段一个都没下发：写通道下表示"这次一个字都不动"。</summary>
    public bool HasAny => Alias is not null || Prefix is not null || Suffix is not null;

    /// <summary>读出目标身上**正在生效**的特殊语音；没有、没开附加设置、或值全空白时三项都是 <c>null</c>。</summary>
    /// <remarks>
    ///     <para>
    ///         为什么按"有没有生效"报，而不是"存没存过值"：语音服务只在
    ///         <c>IsAttachSettingsEnabled</c> 打开时才读这三个值（见 <c>VoiceAnnouncementService</c>
    ///         的 <c>BuildAnnouncementText</c>），关着的时候它们对教室没有任何影响。
    ///     </para>
    ///     <para>
    ///         这一条同时挡住了一个真实的坑：控制台**只回写用户改过的行**，如果关着开关的值也被报出去，
    ///         控制台会把它们当成"这台设备上的值"照原样写回来，写通道又是"下发即打开"，
    ///         于是一次改名字就悄悄把这台机器的专属语音打开了。
    ///         关着 = 读通道什么也不报 = 控制台回写时不会带上它们 = 开关保持原样。
    ///     </para>
    ///     <para>
    ///         空串与纯空白一律归一成 <c>null</c>：协议里"空串等于没有"，成员是这条命令里数量最多的对象，
    ///         没有必要为每个没设置过的人多写出三个空串。
    ///     </para>
    /// </remarks>
    public static ControlSpecificVoiceValues Read(IAttachableSettingsObject? target)
    {
        if (target?.GetAttachedObject<SpecificAnnouncementAttachedSettings>(SettingsId)
            is not { IsAttachSettingsEnabled: true } settings)
            return default;

        return new ControlSpecificVoiceValues(
            NullIfBlank(settings.TtsAlias),
            NullIfBlank(settings.Prefix),
            NullIfBlank(settings.Suffix));
    }

    /// <summary>把本次下发的字段落到目标上；没下发的字段保持设备上的原值。</summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="HasAny" /> 为假时**连附加设置对象都不建**：<c>replace</c> 是按载荷重建整份名单，
    ///         新建的成员上本来就没有这个键，"没给就是没有"这一态因此是自然成立的，不需要额外清空动作。
    ///     </para>
    ///     <para>
    ///         只要还有一项非空，就把「附加设置」这个开关打开：语音服务只在开关打开时读这三个值，
    ///         不开就等于"写进去了但永远不生效"——控制台写进来却看不到任何变化，
    ///         那是最难查的一类"成功"。三项全空是"明确清空"，这时顺手把开关关掉，
    ///         读通道才不会回一组空值出来。
    ///     </para>
    /// </remarks>
    public void WriteTo(IAttachableSettingsObject target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!HasAny)
            return;

        // 用带默认值的那条重载：成员身上还没这个键时，它会建一个并挂上去，
        // 否则新填的值只是内存里一个没人引用的对象，落盘时什么都不会出现。
        var settings = target.GetAttachedObject(SettingsId, new SpecificAnnouncementAttachedSettings());

        if (Alias is { } alias)
            settings.TtsAlias = alias;

        if (Prefix is { } prefix)
            settings.Prefix = prefix;

        if (Suffix is { } suffix)
            settings.Suffix = suffix;

        settings.IsAttachSettingsEnabled =
            !string.IsNullOrWhiteSpace(settings.TtsAlias)
            || !string.IsNullOrWhiteSpace(settings.Prefix)
            || !string.IsNullOrWhiteSpace(settings.Suffix);
    }

    /// <summary>空字符串与缺失在协议里是同一件事：都没有值，不要用 <c>""</c> 冒充有值。</summary>
    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
