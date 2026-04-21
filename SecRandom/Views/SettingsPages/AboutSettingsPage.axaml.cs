using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.about", "\uE9E3", location: PageLocation.Bottom)]
public partial class AboutSettingsPage : UserControl
{
    public AboutSettingsPage()
    {
        InitializeComponent();
    }
}