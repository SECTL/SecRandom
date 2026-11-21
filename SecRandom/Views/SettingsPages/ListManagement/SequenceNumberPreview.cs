using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SecRandom.Core.Services.Profiles;

namespace SecRandom.Views.SettingsPages.ListManagement;

/// <summary>编号变化预览块的拼装，新增冲突与删除前移共用。</summary>
internal static class SequenceNumberPreview
{
    private const int HeadCount = 3;
    private const int MaxCount = 4;

    /// <summary>拼装预览块；条目按旧编号升序，过长时只保留开头几条与最后一条。</summary>
    public static string Build(
        IReadOnlyList<SequenceNumberShiftEntry> entries,
        IReadOnlyList<string> displayNames,
        string header,
        string lineFormat,
        string ellipsis,
        string unnamedMember)
    {
        var ordered = entries.OrderBy(entry => entry.OldNumber).ToList();
        var lines = new List<string> { header };

        if (ordered.Count <= MaxCount)
        {
            lines.AddRange(ordered.Select(entry => FormatLine(entry, displayNames, lineFormat, unnamedMember)));
        }
        else
        {
            lines.AddRange(ordered.Take(HeadCount)
                .Select(entry => FormatLine(entry, displayNames, lineFormat, unnamedMember)));
            lines.Add(ellipsis);
            lines.Add(FormatLine(ordered[^1], displayNames, lineFormat, unnamedMember));
        }

        return string.Join('\n', lines);
    }

    private static string FormatLine(
        SequenceNumberShiftEntry entry,
        IReadOnlyList<string> displayNames,
        string lineFormat,
        string unnamedMember) =>
        string.Format(CultureInfo.CurrentCulture, lineFormat,
            entry.OldId,
            ResolveName(displayNames, entry.Index, unnamedMember),
            entry.NewNumber.ToString(CultureInfo.InvariantCulture));

    /// <summary>按集合下标取成员显示名；没有名字时返回占位文案。</summary>
    public static string ResolveName(IReadOnlyList<string> displayNames, int index, string unnamedMember)
    {
        if (index >= 0 && index < displayNames.Count)
        {
            var name = displayNames[index];
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
        }

        return unnamedMember;
    }
}