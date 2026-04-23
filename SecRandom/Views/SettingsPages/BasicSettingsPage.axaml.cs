using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Models.SubConfigs;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.basic", "\uf4c4")]
public partial class BasicSettingsPage : UserControl
{
    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public BasicSettingsConfig Settings { get; }
    
    public BasicSettingsPage()
    {
        Settings = ViewModel.Config.BasicSettings;
        DataContext = this;
        InitializeComponent();
    }

    private void HideVersionNoticeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Settings.ShowVersionNotice = false;
    }
}