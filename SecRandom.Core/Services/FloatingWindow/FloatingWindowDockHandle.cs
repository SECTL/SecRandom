using System.Globalization;
using System.Text;
using SecRandom.Core.Icons;

namespace SecRandom.Core.Services.FloatingWindow;

/// <summary>
///     收纳手柄（贴边后那个小方块）的样式规则：取哪种内容、文字最多几个字、哪些字符不允许。
/// </summary>
/// <remarks>
///     <!--  -->
///     设置页、悬浮窗渲染和配置归一化都走这里，避免三处各写一套长度/字符判断。
///     <para>
///         中文界面仍然只允许“一个字”，这是用户明确要求的单字效果；其它语言（英文、日文）没有单字可说，
///         按四个字符放宽，既能保留默认的“Go”，也能填一个短词。
///     </para>
///     <para>
///         已保存的文字只按 <see cref="StoredMaxTextLength" /> 兜底，不按当前语言再截断：切换语言不应
///         悄悄改掉（甚至丢掉）用户已经填好的内容，实际显示时的字数限制由设置页输入框负责。
///     </para>
/// </remarks>
public static class FloatingWindowDockHandle
{
    /// <summary>收纳手柄样式：图标（默认）。</summary>
    public const int IconStyle = 0;

    /// <summary>收纳手柄样式：文字。</summary>
    public const int TextStyle = 1;

    /// <summary>收纳手柄样式：方向箭头。</summary>
    public const int ArrowStyle = 2;

    /// <summary>图标样式在未配置字形时使用的默认字形（原“点名”头像图标）。</summary>
    public const string DefaultIconGlyph = FluentIcons.PeopleFilled;

    /// <summary>箭头样式用的字形：箭头本身不再用 “&lt;” / “&gt;” 字符画。</summary>
    public const string LeftArrowGlyph = FluentIcons.ChevronLeftFilled;

    public const string RightArrowGlyph = FluentIcons.ChevronRightFilled;

    /// <summary>中文界面的单字上限。</summary>
    public const int ChineseMaxTextLength = 1;

    /// <summary>其它语言的字数上限：只有中文才讲“单字”，不能让英文被迫只写一个字母。</summary>
    public const int OtherMaxTextLength = 4;

    /// <summary>配置里能存的长度上限，保证切到中文界面时也不会把已存的四个字符砍掉。</summary>
    public const int StoredMaxTextLength = OtherMaxTextLength;

    public static bool IsIconStyle(int style) => style == IconStyle;

    public static bool IsTextStyle(int style) => style == TextStyle;

    public static bool IsChineseUi(CultureInfo? culture) =>
        string.Equals(culture?.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase);

    /// <summary>当前界面语言下允许输入的字数。</summary>
    public static int GetMaxTextLength(CultureInfo? culture) =>
        IsChineseUi(culture) ? ChineseMaxTextLength : OtherMaxTextLength;

    /// <summary>
    ///     按“允许的字符 + 最大长度”整理用户输入：去掉首尾空白、emoji 与控制字符；空白结果等于未填写。
    /// </summary>
    public static string NormalizeText(string? text, int maxLength)
    {
        if (maxLength < 1)
            maxLength = 1;

        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(Math.Min(text.Length, maxLength));
        var count = 0;
        foreach (var rune in text.Trim().EnumerateRunes())
        {
            if (count >= maxLength)
                break;
            if (Rune.IsControl(rune) || IsEmojiRune(rune))
                continue;

            builder.Append(rune.ToString());
            count++;
        }

        return builder.ToString();
    }

    /// <summary>
    ///     实际显示的文字：没填就回退到本地化默认文字；对已存的值只过滤 emoji 并按硬上限截断。
    /// </summary>
    public static string ResolveText(string? text, string fallback)
    {
        var normalized = NormalizeText(text, StoredMaxTextLength);
        return normalized.Length > 0 ? normalized : fallback;
    }

    /// <summary>
    ///     文字越长字号越小：单字保持原来的比例，多字符语言也不会撑出方块。
    /// </summary>
    public static double GetTextFontScale(int textLength) => textLength switch
    {
        <= 1 => 0.42,
        2 => 0.34,
        _ => 0.26
    };

    /// <summary>
    ///     是否是 emoji 字符。字符输入框只挡住 emoji，不挡普通标点，因为“一个字”允许用户填任意可见字符。
    /// </summary>
    private static bool IsEmojiRune(Rune rune)
    {
        var value = rune.Value;
        return value is >= 0x1F000 and <= 0x1FAFF // emoji 主体与区域指示符
            or >= 0x2600 and <= 0x27BF // 杂项符号与 dingbats（☀ ★ ❤ 等 emoji 来源）
            or >= 0x2B00 and <= 0x2BFF // ⬛ ⭐ 等
            or >= 0xFE00 and <= 0xFE0F // 变体选择符：把普通字符变成 emoji 的那个
            or 0x200D // ZWJ 组合符
            or 0x20E3 // 键帽组合符
            or 0x3030 or 0x303D or 0x3297 or 0x3299 // 〰 〽 ㊗ ㊙
            or 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 or 0x24C2; // © ® ‼ ⁉ ™ ℹ Ⓜ
    }
}