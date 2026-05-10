using Avalonia.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.home", FluentIcons.HomeRegular)]
public partial class HomeSettingsPage : UserControl
{
    public HomeSettingsPage()
    {
        InitializeComponent();
    }
}