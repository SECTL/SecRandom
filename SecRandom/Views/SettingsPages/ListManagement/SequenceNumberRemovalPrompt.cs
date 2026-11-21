using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Services.Profiles;

namespace SecRandom.Views.SettingsPages.ListManagement;

/// <summary>删除成员/奖品时的编号处理方式。</summary>
internal enum SequenceNumberRemovalChoice
{
    /// <summary>取消删除。</summary>
    Cancel,

    /// <summary>只删除，后面的编号保持不变。</summary>
    DeleteOnly,

    /// <summary>删除并把后面的数字编号整体向前移动一位。</summary>
    DeleteAndShift
}

/// <summary>
///     删除单个成员确认弹窗的全部文案。由页面按自己的资源类提供，弹窗只负责拼装，
///     以便点名名单与奖品池各自使用本页的措辞。
/// </summary>
/// <param name="Title">弹窗标题。</param>
/// <param name="Content">删除确认正文，参数依次为被删成员、当前名单/奖池。</param>
/// <param name="ShiftHint">可前移时的补充正文，参数依次为被删编号、前移人数、预览块。</param>
/// <param name="PreviewHeader">预览块标题。</param>
/// <param name="PreviewLine">预览行格式，参数为旧编号、成员名、新编号。</param>
/// <param name="PreviewEllipsis">预览被截断时的省略行。</param>
/// <param name="DeletePrimary">不可前移时的“删除”按钮。</param>
/// <param name="DeleteAndShiftPrimary">可前移时的“删除并前移编号”按钮。</param>
/// <param name="DeleteOnlySecondary">可前移时的“仅删除”按钮。</param>
/// <param name="Close">“取消”按钮。</param>
/// <param name="UnnamedMember">没有名字时的占位。</param>
internal sealed record SequenceNumberRemovalText(
    string Title,
    string Content,
    string ShiftHint,
    string PreviewHeader,
    string PreviewLine,
    string PreviewEllipsis,
    string DeletePrimary,
    string DeleteAndShiftPrimary,
    string DeleteOnlySecondary,
    string Close,
    string UnnamedMember);

/// <summary>删除单个成员/奖品的确认弹窗，点名名单与奖品池共用。</summary>
internal static class SequenceNumberRemovalPrompt
{
    /// <summary>展示删除确认并按需附带编号前移选项；关闭或按 Esc 视为取消。</summary>
    public static async Task<SequenceNumberRemovalChoice> ShowAsync(
        Control owner,
        SequenceNumberRemoval removal,
        string displayName,
        string listName,
        IReadOnlyList<string> displayNames,
        SequenceNumberRemovalText text)
    {
        var content = string.Format(CultureInfo.CurrentCulture, text.Content, displayName, listName);
        if (removal.CanShift)
        {
            content += Environment.NewLine + Environment.NewLine + string.Format(CultureInfo.CurrentCulture,
                text.ShiftHint,
                removal.RemovedNumber?.ToString(CultureInfo.InvariantCulture) ?? removal.RemovedId,
                removal.ShiftEntries.Count,
                SequenceNumberPreview.Build(removal.ShiftEntries, displayNames, text.PreviewHeader,
                    text.PreviewLine, text.PreviewEllipsis, text.UnnamedMember));
        }

        var dialog = new FAContentDialog
        {
            Title = text.Title,
            Content = new TextBlock
            {
                Text = content,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460
            },
            CloseButtonText = text.Close,
            DefaultButton = FAContentDialogButton.Close
        };

        if (removal.CanShift)
        {
            dialog.PrimaryButtonText = text.DeleteAndShiftPrimary;
            dialog.SecondaryButtonText = text.DeleteOnlySecondary;
        }
        else
        {
            dialog.PrimaryButtonText = text.DeletePrimary;
        }

        var result = await dialog.ShowAsync(TopLevel.GetTopLevel(owner));
        if (!removal.CanShift)
            return result == FAContentDialogResult.Primary
                ? SequenceNumberRemovalChoice.DeleteOnly
                : SequenceNumberRemovalChoice.Cancel;

        return result switch
        {
            FAContentDialogResult.Primary => SequenceNumberRemovalChoice.DeleteAndShift,
            FAContentDialogResult.Secondary => SequenceNumberRemovalChoice.DeleteOnly,
            _ => SequenceNumberRemovalChoice.Cancel
        };
    }
}