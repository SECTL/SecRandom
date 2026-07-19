using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Controls;
using SecRandom.Core.Icons;

namespace SecRandom.Mobile.Controls;

public sealed class MobileNavigationBar : UserControl
{
    private readonly Border _root;

    public MobileNavigationBar()
    {
        NavigationView = new FANavigationView
        {
            PaneDisplayMode = FANavigationViewPaneDisplayMode.Top,
            IsPaneToggleButtonVisible = false,
            IsBackButtonVisible = false,
            IsSettingsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        NavigationView.MenuItems.Add(CreateItem("抽取", FluentIcons.PeopleFilled, Views.MobileDestination.Draw));
        NavigationView.MenuItems.Add(CreateItem("历史记录", FluentIcons.HistoryFilled, Views.MobileDestination.History));
        NavigationView.MenuItems.Add(CreateItem("概览", FluentIcons.HomeFilled, Views.MobileDestination.Overview));
        NavigationView.MenuItems.Add(CreateItem("设置", FluentIcons.SettingsFilled, Views.MobileDestination.Settings));

        _root = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = NavigationView
        };
        Content = _root;
        RefreshTheme();
    }

    public FANavigationView NavigationView { get; }

    internal void Select(Views.MobileDestination destination)
    {
        NavigationView.SelectedItem = NavigationView.MenuItems
            .OfType<FANavigationViewItem>()
            .FirstOrDefault(item => item.Tag is Views.MobileDestination value && value == destination);
    }

    public void RefreshTheme()
    {
        _root.Background = Views.MobileTheme.Surface;
        _root.BorderBrush = Views.MobileTheme.Border;
    }

    private static FANavigationViewItem CreateItem(string text, string icon, Views.MobileDestination destination) => new()
    {
        Content = text,
        IconSource = new FluentIconSource(icon),
        Tag = destination
    };
}
