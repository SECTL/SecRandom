using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.SettingsPages.HistoryManagement.Resources;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.history.management", FluentIcons.HistoryRegular, "settings.history")]
public partial class HistoryManagementSettingsPage : UserControl
{
    public HistoryManagementSettingsPage()
    {
        Settings = ViewModel.Config.HistoryManagementSettings;
        DataContext = this;
        InitializeComponent();
        Settings.PropertyChanged += SettingsOnPropertyChanged;
        RefreshClearCombos();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public HistoryManagementSettingsConfig Settings { get; }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
    }

    // ── 清除历史记录 ──────────────────────────────────────────

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
            var path = new StudentHistory(name).ConfigFilePath;
            if (File.Exists(path)) File.Delete(path);
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
            var path = new PrizeHistory(name).ConfigFilePath;
            if (File.Exists(path)) File.Delete(path);
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
}

