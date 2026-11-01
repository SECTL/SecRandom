using System;
using System.Globalization;

namespace SecRandom.Models;

/// <summary>
///     历史记录页面查看模式常量。
///     用字符串标识而非数字，便于绑定与调试。
///     个人统计模式下 SelectedMode 直接是学生/奖品名称，不使用此常量。
/// </summary>
public static class HistoryMode
{
    public const string Overview = "overview"; // 总览：按人/奖品汇总被抽次数
    public const string Records = "records";    // 抽取记录：逐条抽取事件
}

/// <summary>
///     历史记录 DataGrid 的平铺行对象。
///     不同查看模式复用同一模型，由页面按模式控制列可见性。
/// </summary>
public sealed class HistoryDisplayRow
{
    /// <summary>历史记录自身的稳定标识；总览模式的行是汇总行，没有对应记录，因此为空。</summary>
    public string RecordId { get; init; } = string.Empty;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string DrawGender { get; init; } = string.Empty;
    public string DrawGroup { get; init; } = string.Empty;

    /// <summary>历史记录组（用户自定义的用途分组，空串=默认组）。与 Group（学生自身分组）、
    /// DrawGroup（抽取时的分组范围）、Subject（联动课程）四者互不相同，不要互相赋值。</summary>
    public string HistoryGroup { get; init; } = string.Empty;

    public int TotalCount { get; init; }
    public string DrawTime { get; init; } = string.Empty;
    public string DrawMethod { get; init; } = string.Empty;
    public int DrawNumbers { get; init; }
    public string Weight { get; init; } = string.Empty;
    public double IdSortValue => double.TryParse(Id, out var value) ? value : double.MaxValue;
    public string GroupSortValue => BuildGroupSortValue(Group);
    public string DrawGroupSortValue => BuildGroupSortValue(DrawGroup);
    public double WeightSortValue => double.TryParse(Weight, out var value) ? value : 0;
    public DateTime SortTime { get; init; } = DateTime.MinValue;

    private static string BuildGroupSortValue(string group)
    {
        var text = group.Trim();
        if (TryExtractArabicNumber(text, out var arabicNumber))
            return FormatNumericSortValue(arabicNumber, text);

        if (TryExtractChineseNumber(text, out var chineseNumber))
            return FormatNumericSortValue(chineseNumber, text);

        return $"1:{text}";
    }

    private static string FormatNumericSortValue(int number, string fallback)
    {
        return $"0:{number.ToString("D10", CultureInfo.InvariantCulture)}:{fallback}";
    }

    private static bool TryExtractArabicNumber(string text, out int number)
    {
        number = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i]))
                continue;

            var start = i;
            while (i < text.Length && char.IsDigit(text[i]))
                i++;

            return int.TryParse(text[start..i], NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }

        return false;
    }

    private static bool TryExtractChineseNumber(string text, out int number)
    {
        number = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!IsChineseNumberChar(text[i]))
                continue;

            var start = i;
            while (i < text.Length && IsChineseNumberChar(text[i]))
                i++;

            return TryParseChineseNumber(text[start..i], out number);
        }

        return false;
    }

    private static bool IsChineseNumberChar(char value)
    {
        return GetChineseDigit(value) >= 0 || GetChineseUnit(value) > 0;
    }

    private static bool TryParseChineseNumber(string text, out int number)
    {
        number = 0;
        var currentDigit = 0;
        var hasValue = false;

        foreach (var ch in text)
        {
            var digit = GetChineseDigit(ch);
            if (digit >= 0)
            {
                currentDigit = digit;
                hasValue = true;
                continue;
            }

            var unit = GetChineseUnit(ch);
            if (unit <= 0)
                return false;

            if (currentDigit == 0 && unit == 10)
                currentDigit = 1;

            number += currentDigit * unit;
            currentDigit = 0;
            hasValue = true;
        }

        number += currentDigit;
        return hasValue;
    }

    private static int GetChineseDigit(char value)
    {
        return value switch
        {
            '零' or '〇' => 0,
            '一' or '壹' => 1,
            '二' or '贰' or '貳' or '两' or '兩' => 2,
            '三' or '叁' or '參' => 3,
            '四' or '肆' => 4,
            '五' or '伍' => 5,
            '六' or '陆' or '陸' => 6,
            '七' or '柒' => 7,
            '八' or '捌' => 8,
            '九' or '玖' => 9,
            _ => -1
        };
    }

    private static int GetChineseUnit(char value)
    {
        return value switch
        {
            '十' or '拾' => 10,
            '百' or '佰' => 100,
            '千' or '仟' => 1000,
            _ => 0
        };
    }
}

/// <summary>
///     历史记录页「记录组」筛选/移动下拉的一项。
///     <see cref="Value" /> 才是记录上的取值：<c>null</c>=不做记录组筛选（「全部记录组」）、
///     空串=默认组、其余为具体组名；<see cref="DisplayName" /> 只用于显示。
///     两者必须分开，因为默认组的取值就是空串，不能拿它当文本用。
/// </summary>
public sealed class HistoryGroupOption
{
    public string? Value { get; init; }
    public string DisplayName { get; init; } = string.Empty;
}
