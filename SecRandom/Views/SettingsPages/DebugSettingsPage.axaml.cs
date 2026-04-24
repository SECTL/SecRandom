using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Helpers.UI;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.debug", "\uE2C7", location: PageLocation.Bottom)]
public partial class DebugSettingsPage : UserControl
{
    public DebugSettingsPage()
    {
        InitializeComponent();
    }

    private void SendToast_OnClick(object? sender, RoutedEventArgs e)
    {
        this.ShowToast("测试 Toast~");
    }

    private void OpenDrawer_Test(object? sender, RoutedEventArgs e)
    {
        SettingsView.Current?.OpenDrawer(this.FindResource("DrawerTest")!);
    }
}