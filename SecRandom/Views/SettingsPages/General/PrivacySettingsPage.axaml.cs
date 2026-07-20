using Avalonia.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.privacy", FluentIcons.EyeFilled, "settings.general")]
public partial class PrivacySettingsPage : UserControl
{
    public PrivacySettingsPage()
    {
        Settings = ViewModel.Config.General.PrivacySettings;
        DataContext = this;
        InitializeComponent();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public PrivacySettingsConfig Settings { get; }
}
