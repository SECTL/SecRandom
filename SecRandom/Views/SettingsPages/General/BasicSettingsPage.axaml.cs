using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.basic", FluentIcons.WrenchSettingsRegular, "settings.general")]
public partial class BasicSettingsPage : UserControl
{
    public BasicSettingsPage()
    {
        Settings = ViewModel.Config.Basic;
        DataContext = this;
        InitializeComponent();

        Settings.PropertyChanged += SettingsOnPropertyChanged;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public BasicSettingsConfig Settings { get; }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.Language))
        {
            // 先设置语言，省的 Needs Restarting 显示中文等情况发生
            var culture = Settings.Language switch
            {
                LanguageMode.ChineseSimplified => @"zh-Hans",
                LanguageMode.English => @"en-US",
                LanguageMode.Japanese => @"ja-JP",
                _ => @"zh-Hans"
            };
            App.InitializeLanguages(new CultureInfo(culture));
            
            SettingsView.Current?.RequestRestartApp();
        }
    }

    private void HideVersionNoticeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Settings.ShowVersionNotice = false;
    }
}
