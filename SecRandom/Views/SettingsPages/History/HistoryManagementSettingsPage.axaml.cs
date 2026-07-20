using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Views;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Abstraction.Services;
using LR = SecRandom.Langs.SettingsPages.HistoryManagement.Resources;

namespace SecRandom.Views.SettingsPages.History;

[PageInfo("settings.history.management", FluentIcons.HistoryFilled, "settings.history")]
public partial class HistoryManagementSettingsPage : ViewBase
{
    public HistoryManagementSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        RefreshClearCombos();
    }

    private void RefreshClearCombos()
    {
        var historyQueryService = IAppHost.GetService<IHistoryQueryService>();
        PopulateCombo(RollCallClassCombo, historyQueryService.GetStudentHistoryNames());
        PopulateCombo(LotteryPoolCombo, historyQueryService.GetPrizeHistoryNames());
    }

    private static void PopulateCombo(ComboBox combo, IReadOnlyList<string> names)
    {
        combo.Items.Clear();
        foreach (var name in names)
            combo.Items.Add(name);

        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private async void ClearRollCallHistory_OnClick(object? sender, RoutedEventArgs e)
    {
        var name = RollCallClassCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(name))
        {
            this.ShowWarningToast(LR.M_SelectFirst);
            return;
        }

        if (!await ConfirmClearAsync(name)) return;

        try
        {
            IAppHost.GetService<IProfileCatalogManager>().ClearStudentHistory(name);
            PopulateCombo(RollCallClassCombo, IAppHost.GetService<IHistoryQueryService>().GetStudentHistoryNames());
            this.ShowSuccessToast(string.Format(LR.M_ClearSuccess, name));
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(string.Format(LR.M_ClearFailed, ex.Message));
        }
    }

    private async void ClearLotteryHistory_OnClick(object? sender, RoutedEventArgs e)
    {
        var name = LotteryPoolCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(name))
        {
            this.ShowWarningToast(LR.M_SelectFirst);
            return;
        }

        if (!await ConfirmClearAsync(name)) return;

        try
        {
            IAppHost.GetService<IProfileCatalogManager>().ClearPrizeHistory(name);
            PopulateCombo(LotteryPoolCombo, IAppHost.GetService<IHistoryQueryService>().GetPrizeHistoryNames());
            this.ShowSuccessToast(string.Format(LR.M_ClearSuccess, name));
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(string.Format(LR.M_ClearFailed, ex.Message));
        }
    }

    private async System.Threading.Tasks.Task<bool> ConfirmClearAsync(string name)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_ClearConfirm_Title,
            Content = string.Format(LR.M_ClearConfirm_Content, name),
            PrimaryButtonText = LR.C_Clear,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result == FAContentDialogResult.Primary;
    }

}
