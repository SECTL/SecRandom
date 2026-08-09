using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.ViewModels;
using SecRandom.Services.Music;

namespace SecRandom.Views.SettingsPages.Picking;

[PageInfo("settings.picking.lottery", FluentIcons.LotteryFilled, "settings.picking")]
public partial class LotteryDrawSettingsPage : UserControl
{
    private bool _normalizingSettings;
    private bool _isSubscribed;

    public LotteryDrawSettingsPage()
    {
        Settings = ViewModel.Config.LotterySettings;
        MusicLibrary.Refresh();
        RefreshPrizeLists();
        DataContext = this;
        InitializeComponent();
        SubscribeSettings();
        NormalizeDrawSettings();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public LotterySettingsConfig Settings { get; }
    public ObservableCollection<string> PrizeListNames { get; } = [];
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

    private void RefreshPrizeLists()
    {
        PrizeListNames.Clear();
        foreach (var file in Directory.GetFiles(Utils.GetDirectoryPath("list", "lottery_list"), "*.json")
                     .OrderBy(Path.GetFileName))
            PrizeListNames.Add(Path.GetFileNameWithoutExtension(file));
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
            Settings.CustomLotteryShowRandomFormat = LotteryProcessDisplayFormatter.NormalizeTemplate(
                Settings.CustomLotteryShowRandomFormat);
        }
        finally
        {
            _normalizingSettings = false;
        }
    }

    private void InsertCustomLotteryShowRandomFormatTokenOnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string token })
            return;

        var textBox = CustomLotteryShowRandomFormatTextBox;
        var template = Settings.CustomLotteryShowRandomFormat;
        var selectionStart = System.Math.Clamp(textBox.SelectionStart, 0, template.Length);
        var selectionEnd = System.Math.Clamp(textBox.SelectionEnd, selectionStart, template.Length);
        var updated = template[..selectionStart] + token + template[selectionEnd..];

        Settings.CustomLotteryShowRandomFormat = LotteryProcessDisplayFormatter.NormalizeTemplate(updated);
        var caret = System.Math.Min(selectionStart + token.Length, Settings.CustomLotteryShowRandomFormat.Length);
        textBox.SelectionStart = caret;
        textBox.SelectionEnd = caret;
        textBox.Focus();
    }
}
