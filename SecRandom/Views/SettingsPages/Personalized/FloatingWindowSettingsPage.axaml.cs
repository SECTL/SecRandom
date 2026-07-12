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

namespace SecRandom.Views.SettingsPages.Personalized;

[PageInfo("settings.personalized.floatingWindow", FluentIcons.WindowAppsRegular, "settings.personalized")]
public partial class FloatingWindowSettingsPage : UserControl
{
    private bool _synchronizingSelections;

    public FloatingWindowSettingsPage()
    {
        Settings = ViewModel.Config.FloatingWindowSettings;
        var migratedSize = NormalizeFloatingWindowSize() | NormalizeDockedWindowSize();
        ButtonOptions =
        [
            new(LR.S_Buttons_RollCall, () => Settings.ShowRollCallButton,
                value => Settings.ShowRollCallButton = value),
            new(LR.S_Buttons_QuickDraw, () => Settings.ShowQuickDrawButton,
                value => Settings.ShowQuickDrawButton = value),
            new(LR.S_Buttons_Lottery, () => Settings.ShowLotteryButton,
                value => Settings.ShowLotteryButton = value),
            new(LR.S_Buttons_FaceDraw, () => Settings.ShowFaceDrawButton,
                value => Settings.ShowFaceDrawButton = value)
        ];
        SelectedButtonOptions = BuildSelectedOptions(ButtonOptions);
        DataContext = this;
        InitializeComponent();
        Settings.PropertyChanged += SettingsOnPropertyChanged;
        if (migratedSize)
            ConfigHandler.Save();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public FloatingWindowSettingsConfig Settings { get; }
    public AvaloniaList<MultiSelectSettingOption> ButtonOptions { get; }
    public AvaloniaList<MultiSelectSettingOption> SelectedButtonOptions { get; }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
        SynchronizeSelectedOptions(ButtonOptions, SelectedButtonOptions);
    }

    private static AvaloniaList<MultiSelectSettingOption> BuildSelectedOptions(
        IEnumerable<MultiSelectSettingOption> options)
    {
        return new AvaloniaList<MultiSelectSettingOption>(options.Where(option => option.IsSelected));
    }

    private bool NormalizeFloatingWindowSize()
    {
        var size = Settings.FloatingWindowSize <= 6
            ? Settings.FloatingWindowSize switch
            {
                0 => 28,
                1 => 32,
                2 => 40,
                3 => 48,
                4 => 56,
                5 => 64,
                _ => 72
            }
            : System.Math.Clamp(Settings.FloatingWindowSize, 32, 160);

        if (size == Settings.FloatingWindowSize)
            return false;

        Settings.FloatingWindowSize = size;
        return true;
    }

    private bool NormalizeDockedWindowSize()
    {
        var size = System.Math.Clamp(Settings.DockedWindowSize, 28, 96);
        if (size == Settings.DockedWindowSize)
            return false;

        Settings.DockedWindowSize = size;
        return true;
    }

    private void SynchronizeSelectedOptions(
        IEnumerable<MultiSelectSettingOption> options,
        AvaloniaList<MultiSelectSettingOption> selectedOptions)
    {
        _synchronizingSelections = true;
        try
        {
            selectedOptions.Clear();
            selectedOptions.AddRange(options.Where(option => option.IsSelected));
        }
        finally
        {
            _synchronizingSelections = false;
        }
    }

    private void MultiSelect_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelections)
            return;

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
