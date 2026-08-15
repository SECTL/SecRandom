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
using SecRandom.Services.Music;

namespace SecRandom.Views.SettingsPages.Picking;

[PageInfo("settings.picking.quickDraw", FluentIcons.FlashFilled, "settings.picking")]
public partial class QuickDrawSettingsPage : UserControl
{
    private bool _normalizingSettings;
    private bool _isSubscribed;
    private string _lastKnownDefaultClass = string.Empty;

    public QuickDrawSettingsPage()
    {
        Settings = ViewModel.Config.QuickDrawSettings;
        _lastKnownDefaultClass = Settings.DefaultClass;
        MusicLibrary.Refresh();
        RefreshStudentLists();
        DataContext = this;
        InitializeComponent();
        SubscribeSettings();
        NormalizeDrawSettings();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public QuickDrawSettingsConfig Settings { get; }
    public ObservableCollection<string> StudentListNames { get; } = [];
    public ObservableCollection<MusicSelection> MusicSelections => MusicLibrary.Selections;

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private MusicLibraryService MusicLibrary { get; } = IAppHost.GetService<MusicLibraryService>();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        MusicLibrary.Refresh();
        SubscribeSettings();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        _isSubscribed = false;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickDrawSettingsConfig.DefaultClass))
        {
            if (string.IsNullOrWhiteSpace(Settings.DefaultClass))
            {
                // 下拉框在 ItemsSource 刷新时会先清空选中项并回写空值，恢复用户之前的选择
                // 而不是清空或回退到第一项，避免默认名单被瞬时空掉后又被兜底逻辑覆盖。
                if (!string.IsNullOrWhiteSpace(_lastKnownDefaultClass))
                    Settings.DefaultClass = _lastKnownDefaultClass;
                return;
            }

            _lastKnownDefaultClass = Settings.DefaultClass;
        }

        NormalizeDrawSettings();
        ConfigHandler.Save();
    }

    private void SubscribeSettings()
    {
        if (_isSubscribed)
            return;

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        _isSubscribed = true;
    }

    private void RefreshStudentLists()
    {
        StudentListNames.Clear();
        foreach (var file in Directory.GetFiles(Utils.GetDirectoryPath("list", "roll_call_list"), "*.json")
                     .OrderBy(Path.GetFileName))
            StudentListNames.Add(Path.GetFileNameWithoutExtension(file));

        if (StudentListNames.Count > 0
            && string.IsNullOrWhiteSpace(Settings.DefaultClass)
            && SettingsView.Current?.IsPreviewMode != true)
        {
            Settings.DefaultClass = StudentListNames[0];
            ConfigHandler.Save();
        }
    }

    private void NormalizeDrawSettings()
    {
        if (SettingsView.Current?.IsPreviewMode == true || _normalizingSettings)
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

            Settings.DisableAfterClick = System.Math.Clamp(Settings.DisableAfterClick, 0, 60);
        }
        finally
        {
            _normalizingSettings = false;
        }
    }
}
