using SecRandom.Core.Enums;

namespace SecRandom.Core.Models.Notification;

/// <summary>
///     全屏窗口里的一条结果（学生或奖品）。
/// </summary>
/// <remarks>
///     刻意做成不可变 DTO：窗口只吃快照，绝不回读页面 ViewModel 或重新查询候选池。
///     提交之后临时记录已经变了，二次查询会得到和结果不一致的名单。
/// </remarks>
/// <param name="DisplayText">主展示文本（学生名或奖品名）。</param>
/// <param name="Subtitle">副标题（中奖学生、学号等）。</param>
/// <param name="ImagePath">头像/奖品图路径（可为空）。</param>
public sealed record FullscreenResultItem(
    string DisplayText,
    string? Subtitle = null,
    string? ImagePath = null);

/// <summary>
///     开窗时（抽取开始前）冻结的展示素材。
/// </summary>
/// <param name="Kind">抽取类型，决定文案与呈现措辞。</param>
/// <param name="FrozenPool">抽取前冻结的候选池，供学号网格排布。</param>
/// <param name="TotalCount">本轮候选总数。</param>
/// <param name="RemainingCount">抽取前剩余人数。</param>
/// <param name="ScopeName">名单/奖池名称，可为空。</param>
public sealed record FullscreenPresentationSeed(
    NotificationSettingsType Kind,
    IReadOnlyList<FullscreenResultItem> FrozenPool,
    int TotalCount,
    int RemainingCount,
    string? ScopeName = null);

/// <summary>
///     提交成功后交给全屏呈现的完整快照。
/// </summary>
/// <remarks>
///     文案（<paramref name="TemplateText" />）在快照构造前已经选定并计数，
///     窗口只做 <c>{result}</c> 替换与渲染，避免"显示一条、计数另一条"。
///     <paramref name="ShowText" /> 只在打字机样式下有意义：学号网格与直接显示
///     落定后停在自己的画面上，不再展示模板前后段。
/// </remarks>
public sealed record FullscreenResultSnapshot(
    NotificationSettingsType Kind,
    IReadOnlyList<FullscreenResultItem> Results,
    IReadOnlyList<FullscreenResultItem> FrozenPool,
    int TotalCount,
    int RemainingCount,
    string? CourseName,
    string? HistoryGroup,
    string? ScopeName,
    DateTimeOffset DrawnAt,
    string TemplateText,
    bool ShowText,
    string? VoiceText,
    bool ShowResultAvatar = false);

/// <summary>
///     全屏文案模板的渲染规则：<c>{result}</c> 是唯一的占位符，
///     渲染时替换成纯结果文本（大括号只是模板语法，不是展示内容）。
/// </summary>
public static class FullscreenTextTemplate
{
    /// <summary>占位符字面量。</summary>
    public const string ResultPlaceholder = "{result}";

    /// <summary>把模板渲染成纯文本（用于语音播报与无障碍文本）。</summary>
    public static string RenderPlain(string template, string result)
        => (template ?? string.Empty).Replace(ResultPlaceholder, result, StringComparison.Ordinal);

    /// <summary>模板是否恰好包含一个占位符。</summary>
    public static bool HasSinglePlaceholder(string template)
    {
        if (string.IsNullOrEmpty(template))
            return false;

        var first = template.IndexOf(ResultPlaceholder, StringComparison.Ordinal);
        return first >= 0 && template.IndexOf(ResultPlaceholder, first + 1, StringComparison.Ordinal) < 0;
    }

    /// <summary>把模板拆成"占位符之前的文本"与"之后的文本"。</summary>
    public static (string Prefix, string Suffix) Split(string template)
    {
        var text = template ?? string.Empty;
        var index = text.IndexOf(ResultPlaceholder, StringComparison.Ordinal);
        if (index < 0)
            return (text, string.Empty);

        return (
            text[..index],
            text[(index + ResultPlaceholder.Length)..]);
    }
}
