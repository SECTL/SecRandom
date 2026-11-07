using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Services.Profiles;

namespace SecRandom.Views.SettingsPages.ListManagement;

/// <summary>编号冲突弹窗的用户选择。</summary>
internal enum SequenceNumberConflictChoice
{
    /// <summary>把该编号及之后的数字编号整体 +1，新成员插到被撞成员的位置。</summary>
    ShiftAndInsert,

    /// <summary>其他成员编号不变，新成员取现有最大数字 +1 并追加到末尾。</summary>
    AppendAtEnd,

    /// <summary>保留重复编号直接添加。</summary>
    KeepDuplicate,

    /// <summary>不写入，回到表单继续修改。</summary>
    EditAgain
}

/// <summary>
///     编号冲突弹窗的全部文案。由页面按自己的资源类提供，弹窗只负责拼装，
///     以便点名名单与奖品池各自使用本页的措辞。
/// </summary>
/// <param name="Title">弹窗标题。</param>
/// <param name="ShiftContent">可顺延时的正文，参数依次为新编号、被撞成员、顺延人数、追加编号、预览块。</param>
/// <param name="NotNumericContent">新编号不是纯数字时的正文，参数为新编号、被撞成员。</param>
/// <param name="DuplicatedContent">已有多个同号成员时的正文，参数为新编号、同号人数。</param>
/// <param name="PreviewHeader">预览块标题。</param>
/// <param name="PreviewLine">预览行格式，参数为旧编号、成员名、新编号。</param>
/// <param name="PreviewEllipsis">预览被截断时的省略行。</param>
/// <param name="ShiftPrimary">“顺延并插入”按钮。</param>
/// <param name="AppendSecondary">“追加到末尾”按钮。</param>
/// <param name="KeepDuplicatePrimary">“保留重复并添加”按钮。</param>
/// <param name="Close">“返回修改”按钮。</param>
/// <param name="UnnamedMember">没有名字时的占位。</param>
internal sealed record SequenceNumberConflictText(
    string Title,
    string ShiftContent,
    string NotNumericContent,
    string DuplicatedContent,
    string PreviewHeader,
    string PreviewLine,
    string PreviewEllipsis,
    string ShiftPrimary,
    string AppendSecondary,
    string KeepDuplicatePrimary,
    string Close,
    string UnnamedMember);

/// <summary>新增成员的编号冲突确认弹窗，点名名单与奖品池共用。</summary>
internal static class SequenceNumberConflictPrompt
{
    private const int PreviewHeadCount = 3;
    private const int PreviewMaxCount = 4;

    /// <summary>展示冲突弹窗并返回用户的选择；关闭或按 Esc 视为“返回修改”。</summary>
    public static async Task<SequenceNumberConflictChoice> ShowAsync(
        Control owner,
        SequenceNumberConflict conflict,
        IReadOnlyList<string> displayNames,
        SequenceNumberConflictText text)
    {
        // Shiftable / NotNumeric / AlreadyDuplicated 都带着被撞成员下标；InsertIndex 只在可顺延时有效，
        // 因此统一优先使用 DuplicateIndices，避免非数字编号把正文里的“{1}”显示成名单第一名成员。
        var collidingIndex = conflict.DuplicateIndices.Count > 0
            ? conflict.DuplicateIndices[0]
            : conflict.InsertIndex;
        var collidingName = ResolveName(displayNames, collidingIndex, text.UnnamedMember);

        var dialog = new FAContentDialog
        {
            Title = text.Title,
            DefaultButton = FAContentDialogButton.Close
        };

        switch (conflict.Kind)
        {
            case SequenceNumberConflictKind.Shiftable:
                dialog.Content = BuildContent(string.Format(CultureInfo.CurrentCulture, text.ShiftContent,
                    conflict.NewId,
                    collidingName,
                    conflict.ShiftEntries.Count,
                    conflict.AppendNumber?.ToString(CultureInfo.InvariantCulture) ?? conflict.NewId,
                    BuildPreview(conflict, displayNames, text)));
                dialog.PrimaryButtonText = text.ShiftPrimary;
                dialog.SecondaryButtonText = text.AppendSecondary;
                dialog.CloseButtonText = text.Close;
                break;

            case SequenceNumberConflictKind.AlreadyDuplicated:
                dialog.Content = BuildContent(string.Format(CultureInfo.CurrentCulture, text.DuplicatedContent,
                    conflict.NewId, conflict.DuplicateIndices.Count));
                dialog.PrimaryButtonText = text.KeepDuplicatePrimary;
                dialog.CloseButtonText = text.Close;
                break;

            default:
                dialog.Content = BuildContent(string.Format(CultureInfo.CurrentCulture, text.NotNumericContent,
                    conflict.NewId, collidingName));
                dialog.PrimaryButtonText = text.KeepDuplicatePrimary;
                dialog.CloseButtonText = text.Close;
                break;
        }

        var result = await dialog.ShowAsync(TopLevel.GetTopLevel(owner));
        if (conflict.Kind != SequenceNumberConflictKind.Shiftable)
            return result == FAContentDialogResult.Primary
                ? SequenceNumberConflictChoice.KeepDuplicate
                : SequenceNumberConflictChoice.EditAgain;

        return result switch
        {
            FAContentDialogResult.Primary => SequenceNumberConflictChoice.ShiftAndInsert,
            FAContentDialogResult.Secondary => SequenceNumberConflictChoice.AppendAtEnd,
            _ => SequenceNumberConflictChoice.EditAgain
        };
    }

    private static TextBlock BuildContent(string content) => new()
    {
        Text = content,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 460
    };

    private static string BuildPreview(
        SequenceNumberConflict conflict,
        IReadOnlyList<string> displayNames,
        SequenceNumberConflictText text)
    {
        var ordered = conflict.ShiftEntries.OrderBy(entry => entry.OldNumber).ToList();
        var lines = new List<string> { text.PreviewHeader };

        if (ordered.Count <= PreviewMaxCount)
        {
            lines.AddRange(ordered.Select(entry => FormatPreviewLine(entry, displayNames, text)));
        }
        else
        {
            lines.AddRange(ordered.Take(PreviewHeadCount).Select(entry => FormatPreviewLine(entry, displayNames, text)));
            lines.Add(text.PreviewEllipsis);
            lines.Add(FormatPreviewLine(ordered[^1], displayNames, text));
        }

        return string.Join('\n', lines);
    }

    private static string FormatPreviewLine(
        SequenceNumberShiftEntry entry,
        IReadOnlyList<string> displayNames,
        SequenceNumberConflictText text) =>
        string.Format(CultureInfo.CurrentCulture, text.PreviewLine,
            entry.OldId,
            ResolveName(displayNames, entry.Index, text.UnnamedMember),
            entry.NewNumber.ToString(CultureInfo.InvariantCulture));

    private static string ResolveName(IReadOnlyList<string> displayNames, int index, string unnamedMember)
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
