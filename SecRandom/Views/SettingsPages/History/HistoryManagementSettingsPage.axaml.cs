using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Langs.SettingsPages.HistoryManagement.Resources;

namespace SecRandom.Views.SettingsPages.History;

[PageInfo("settings.history.management", FluentIcons.HistoryFilled, "settings.history")]
public partial class HistoryManagementSettingsPage : UserControl
{
    public HistoryManagementSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        RefreshClearCombos();
    }

    private void RefreshClearCombos()
    {
        PopulateCombo(RollCallClassCombo, "roll_call_history");
        PopulateCombo(LotteryPoolCombo, "lottery_history");
    }

    private static void PopulateCombo(ComboBox combo, string subDir)
    {
        combo.Items.Clear();
        var dir = Utils.GetDirectoryPath("history", subDir);
        foreach (var name in Directory.GetFiles(dir, "*.json")
                     .Select(Path.GetFileNameWithoutExtension)
                     .OrderBy(n => n))
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
            var profileService = IAppHost.GetService<IProfileService>();
            if (string.Equals(profileService.StudentHistoryConfig?.Name, name, StringComparison.OrdinalIgnoreCase))
                profileService.ClearCurrentStudentHistory();
            else
            {
                var historyConfig = new StudentHistoryConfig(name);
                Clear(historyConfig.Data);
                historyConfig.Save();
            }
            PopulateCombo(RollCallClassCombo, "roll_call_history");
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
            var profileService = IAppHost.GetService<IProfileService>();
            if (string.Equals(profileService.PrizeHistoryConfig?.Name, name, StringComparison.OrdinalIgnoreCase))
                profileService.ClearCurrentPrizeHistory();
            else
            {
                var historyConfig = new PrizeHistoryConfig(name);
                Clear(historyConfig.Data);
                historyConfig.Save();
            }
            PopulateCombo(LotteryPoolCombo, "lottery_history");
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

    private static void Clear(StudentHistory history)
    {
        history.TotalRounds = 0;
        history.TotalStats = 0;
        history.Students.Clear();
        history.GroupStats.Clear();
        history.GenderStatus.Clear();
    }

    private static void Clear(PrizeHistory history)
    {
        history.TotalRounds = 0;
        history.TotalStats = 0;
        history.Prizes.Clear();
        history.GroupStats.Clear();
        history.GenderStatus.Clear();
    }
}
