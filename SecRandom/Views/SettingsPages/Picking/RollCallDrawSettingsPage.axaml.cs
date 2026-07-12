using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages.Picking;

[PageInfo("settings.picking.rollCall", FluentIcons.PersonFilled, "settings.picking")]
public partial class RollCallDrawSettingsPage : UserControl
{
    private bool _normalizingSettings;

    public RollCallDrawSettingsPage()
    {
        Settings = ViewModel.Config.RollCallSettings;
        RefreshStudentLists();
        DataContext = this;
        InitializeComponent();
        NormalizeDrawSettings();
        Settings.PropertyChanged += SettingsOnPropertyChanged;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public RollCallSettingsConfig Settings { get; }
    public ObservableCollection<string> StudentListNames { get; } = [];

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        NormalizeDrawSettings();
        ConfigHandler.Save();
    }

    private void RefreshStudentLists()
    {
        StudentListNames.Clear();
        foreach (var file in Directory.GetFiles(Utils.GetDirectoryPath("list", "roll_call_list"), "*.json")
                     .OrderBy(Path.GetFileName))
            StudentListNames.Add(Path.GetFileNameWithoutExtension(file));
    }

    private void NormalizeDrawSettings()
    {
        if (_normalizingSettings)
            return;

        _normalizingSettings = true;
        try
        {
            Settings.HalfRepeat = Settings.DrawMode switch
            {
                DrawMode.Repeat => 0,
                DrawMode.NoRepeat => 1,
                DrawMode.HalfRepeat => System.Math.Clamp(Settings.HalfRepeat, 2, 100),
                _ => Settings.HalfRepeat
            };
        }
        finally
        {
            _normalizingSettings = false;
        }
    }
}
