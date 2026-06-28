using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Models;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.SettingsPages.FloatingWindow.Resources;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.personalized.floatingWindow", FluentIcons.WindowAppsRegular, "settings.personalized")]
public partial class FloatingWindowSettingsPage : UserControl
{
    public FloatingWindowSettingsPage()
    {
        Settings = ViewModel.Config.FloatingWindowSettings;
        ButtonOptions =
        [
            new(LR.S_Buttons_RollCall, () => Settings.ShowRollCallButton,
                value => Settings.ShowRollCallButton = value),
            new(LR.S_Buttons_QuickDraw, () => Settings.ShowQuickDrawButton,
                value => Settings.ShowQuickDrawButton = value),
            new(LR.S_Buttons_Lottery, () => Settings.ShowLotteryButton,
                value => Settings.ShowLotteryButton = value),
            new(LR.S_Buttons_FaceDraw, () => Settings.ShowFaceDrawButton,
                value => Settings.ShowFaceDrawButton = value),
            new(LR.S_Buttons_Timer, () => Settings.ShowTimerButton,
                value => Settings.ShowTimerButton = value),
            new(LR.S_Buttons_ExtendQuickDraw, () => Settings.ExtendQuickDrawComponent,
                value => Settings.ExtendQuickDrawComponent = value)
        ];
        InteractionOptions =
        [
            new(LR.S_Interaction_StickToEdge, () => Settings.StickToEdge,
                value => Settings.StickToEdge = value),
            new(LR.S_Interaction_Draggable, () => Settings.Draggable,
                value => Settings.Draggable = value),
            new(LR.S_Interaction_DoNotStealFocus, () => Settings.DoNotStealFocus,
                value => Settings.DoNotStealFocus = value),
            new(LR.S_Interaction_HideOnForeground, () => Settings.HideOnForeground,
                value => Settings.HideOnForeground = value)
        ];
        SelectedButtonOptions = BuildSelectedOptions(ButtonOptions);
        SelectedInteractionOptions = BuildSelectedOptions(InteractionOptions);
        DataContext = this;
        InitializeComponent();
        Settings.PropertyChanged += SettingsOnPropertyChanged;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public FloatingWindowSettingsConfig Settings { get; }
    public AvaloniaList<MultiSelectSettingOption> ButtonOptions { get; }
    public AvaloniaList<MultiSelectSettingOption> SelectedButtonOptions { get; }
    public AvaloniaList<MultiSelectSettingOption> InteractionOptions { get; }
    public AvaloniaList<MultiSelectSettingOption> SelectedInteractionOptions { get; }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
    }

    private static AvaloniaList<MultiSelectSettingOption> BuildSelectedOptions(
        IEnumerable<MultiSelectSettingOption> options)
    {
        return new AvaloniaList<MultiSelectSettingOption>(options.Where(option => option.IsSelected));
    }

    private void MultiSelect_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        foreach (var option in e.AddedItems.OfType<MultiSelectSettingOption>())
        {
            option.SetSelected(true);
        }

        foreach (var option in e.RemovedItems.OfType<MultiSelectSettingOption>())
        {
            option.SetSelected(false);
        }
    }
}
