using System.Threading.Tasks;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Services.SingleInstance;

namespace SecRandom.Dialogs;

/// <summary>用户在多实例对话框中的选择结果。</summary>
public enum DuplicateInstanceAction
{
    /// <summary>打开已有实例主界面</summary>
    OpenExisting,

    /// <summary>重启实例</summary>
    Restart,

    /// <summary>取消（直接退出当前进程）</summary>
    Cancel
}

/// <summary>
///     多实例检测警告对话框帮助类。
///     在重复实例启动时弹出，让用户选择如何处理。
/// </summary>
public static class DuplicateInstanceDialog
{
    /// <summary>
    ///     在指定 TopLevel 上异步显示多实例对话框，返回用户选择的操作。
    /// </summary>
    public static async Task<DuplicateInstanceAction> ShowAsync(TopLevel parent)
    {
        var dialog = new FAContentDialog
        {
            Title = Langs.Common.Resources.MultiInstance_Title,
            Content = Langs.Common.Resources.MultiInstance_Message,
            PrimaryButtonText = Langs.Common.Resources.MultiInstance_OpenExisting,
            SecondaryButtonText = Langs.Common.Resources.MultiInstance_Restart,
            CloseButtonText = Langs.Common.Resources.MultiInstance_Cancel,
            DefaultButton = FAContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync(parent);

        return result switch
        {
            FAContentDialogResult.Primary => DuplicateInstanceAction.OpenExisting,
            FAContentDialogResult.Secondary => DuplicateInstanceAction.Restart,
            _ => DuplicateInstanceAction.Cancel
        };
    }
}
