using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Views;

namespace SecRandom.Mobile.Tests;

/// <summary>
/// Issue #268: the settings sidebar entry opens a separate window/view, so it must not take the
/// navigation highlight away from the main page that stays visible.
/// </summary>
public sealed class MainViewSettingsNavigationTests
{
    [AvaloniaFact]
    public void SettingsEntryKeepsItsPageMetadataWithoutSelectingItself()
    {
        var item = Assert.IsType<FANavigationViewItem>(MainView.CreateSettingsNavigationItem());

        Assert.False(item.SelectsOnInvoked);
        var info = Assert.IsType<PageInfo>(item.Tag);
        Assert.Equal("settings", info.Id);
        Assert.Equal(PageLocation.Bottom, info.Location);
    }

    [AvaloniaFact]
    public void InvokingTheSettingsEntryKeepsTheSelectedMainPage()
    {
        var rollCall = new FANavigationViewItem { Content = "Roll call" };
        var settings = MainView.CreateSettingsNavigationItem();
        var navigation = new FANavigationView
        {
            MenuItemsSource = new object[] { rollCall },
            FooterMenuItemsSource = new object[] { settings },
            SelectedItem = rollCall
        };

        var window = new Window
        {
            Width = 900,
            Height = 600,
            Content = navigation
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        PageInfo? invoked = null;
        navigation.ItemInvoked += (_, args) =>
        {
            if (args.InvokedItemContainer is FANavigationViewItem { Tag: PageInfo info })
                invoked = info;
        };

        var settingsControl = (Control)settings;
        var center = settingsControl.TranslatePoint(
            new Point(settingsControl.Bounds.Width / 2, settingsControl.Bounds.Height / 2), window);
        Assert.NotNull(center);

        window.MouseDown(center.Value, MouseButton.Left);
        window.MouseUp(center.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(invoked);
        Assert.Equal("settings", invoked.Id);
        Assert.Same(rollCall, navigation.SelectedItem);

        window.Close();
    }
}
