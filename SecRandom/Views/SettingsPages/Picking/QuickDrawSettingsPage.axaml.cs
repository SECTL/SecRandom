using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages.Picking;

[PageInfo("settings.picking.quickDraw", FluentIcons.FlashRegular, "settings.picking")]
public partial class QuickDrawSettingsPage : UserControl
{
    public QuickDrawSettingsPage()
    {
        Settings = ViewModel.Config.QuickDrawSettings;
        RefreshStudentLists();
        DataContext = this;
        InitializeComponent();
        Settings.PropertyChanged += SettingsOnPropertyChanged;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public QuickDrawSettingsConfig Settings { get; }
    public ObservableCollection<string> StudentListNames { get; } = [];
    public List<string> FontFamilyNames { get; } = [..FontManager.Current.SystemFonts.Select(font => font.Name), GlobalConstants.DefaultFontFamily];

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
    }

    private void RefreshStudentLists()
    {
        StudentListNames.Clear();
        foreach (var file in Directory.GetFiles(Utils.GetDirectoryPath("data", "list", "roll_call_list"), "*.json")
                     .OrderBy(Path.GetFileName))
            StudentListNames.Add(Path.GetFileNameWithoutExtension(file));
    }
}
