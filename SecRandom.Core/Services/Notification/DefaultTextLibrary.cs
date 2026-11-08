namespace SecRandom.Core.Services.Notification;

/// <summary>
///     全屏通知渠道的内置文案库（30 条）。
/// </summary>
/// <remarks>
///     <para>
///         文案是产品内容而不是界面文案：用户可以整库替换、逐条编辑，三语翻译反而会覆盖
///         用户自己的文字，因此**刻意写死在 Core 常量里，不进 resx**。
///     </para>
///     <para>
///         每条模板恰好含一个 <c>{result}</c>，前后段即占位符左右两侧的文本；长度 6–40 字符，
///         不涉及性别与成绩，语气轻但不能堆砌感叹号，避免同一句频繁撞车。
///     </para>
/// </remarks>
public static class DefaultTextLibrary
{
    private static readonly string[] DefaultTemplates =
    [
        "恭喜 {result}，今天的好运被你抽中了",
        "就是 {result}，掌声在哪里",
        "{result}，老师想听你说说",
        "有请 {result} 登场",
        "{result}，这道题非你莫属",
        "天命所归，{result}，请起立",
        "{result} 被幸运女神点了个名",
        "灯光给到 {result}",
        "{result}，你与这道题有缘",
        "系统强烈推荐 {result}",
        "恭喜 {result} 喜提本题一票",
        "让我们把目光送给 {result}",
        "{result}，请开始你的表演",
        "抽中的是你，{result}，别躲了",
        "{result}，机会来了",
        "这道题的答案，交给 {result}",
        "{result}，本轮 MVP 就是你",
        "幸运签落在 {result} 头上",
        "{result}，老师的小本本记下你了",
        "有请 {result} 展现真正的技术",
        "{result}，全班的目光都在你身上",
        "掌声送给被选中的 {result}",
        "{result}，好运正在向你招手",
        "这一次，{result} 站到台前",
        "{result}，请接受大家的注视",
        "恭喜 {result} 成为本轮幸运儿",
        "{result}，请说出你的答案",
        "就是你了，{result}，跑不掉",
        "{result}，轮到你来发光了",
        "命中注定，{result}，请上场"
    ];

    /// <summary>
    ///     内置文案模板。恢复默认与损坏重建都只认这一份清单，因此它是"默认库"的唯一来源。
    /// </summary>
    public static IReadOnlyList<string> Templates { get; } = Array.AsReadOnly(DefaultTemplates);
}
