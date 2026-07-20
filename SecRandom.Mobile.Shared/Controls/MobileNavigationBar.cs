using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Icons;
using SecRandom.Mobile.Views;

namespace SecRandom.Mobile.Controls;

public sealed class MobileNavigationBar : UserControl
{
    private readonly Border _root;
    private MobileDestination _selectedDestination = MobileDestination.Draw;

    public MobileNavigationBar()
    {
        NavigationView = new FANavigationView
        {
            PaneDisplayMode = FANavigationViewPaneDisplayMode.Top,
            IsPaneToggleButtonVisible = false,
            IsBackButtonVisible = false,
            IsSettingsVisible = false,
            AlwaysShowHeader = false,
            SelectionFollowsFocus = false,
            MinHeight = 64,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        NavigationView.MenuItems.Add(CreateItem(
            Langs.Mobile.Resources.N_Draw, FluentIcons.PeopleFilled, MobileDestination.Draw));
        NavigationView.MenuItems.Add(CreateItem(
            Langs.Mobile.Resources.N_History, FluentIcons.HistoryFilled, MobileDestination.History));
        NavigationView.MenuItems.Add(CreateItem(
            Langs.Mobile.Resources.N_Overview, FluentIcons.HomeFilled, MobileDestination.Overview));
        NavigationView.MenuItems.Add(CreateItem(
            Langs.Mobile.Resources.N_Settings, FluentIcons.SettingsFilled, MobileDestination.Settings));
        NavigationView.ItemInvoked += NavigationViewOnItemInvoked;

        _root = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = NavigationView
        };
        Content = _root;
        RefreshTheme();
        Select(MobileDestination.Draw);
    }

    internal event EventHandler<MobileDestination>? DestinationSelected;

    internal FANavigationView NavigationView { get; }

    internal void Select(MobileDestination destination)
    {
        _selectedDestination = destination;
        NavigationView.SelectedItem = NavigationView.MenuItems
            .OfType<FANavigationViewItem>()
            .FirstOrDefault(item => item.Tag is MobileDestination value && value == destination);
    }

    public void RefreshTheme()
    {
        _root.Background = MobileTheme.Surface;
        _root.BorderBrush = MobileTheme.Border;
    }

    private void NavigationViewOnItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
    {
        if (e.InvokedItemContainer is not FANavigationViewItem { Tag: MobileDestination destination }
            || destination == _selectedDestination)
            return;

        Select(destination);
        DestinationSelected?.Invoke(this, destination);
    }

    private static FANavigationViewItem CreateItem(
        string text,
        string glyph,
        MobileDestination destination) => new()
    {
        Content = text,
        IconSource = MobileUi.CreateIconSource(glyph),
        Tag = destination
    };
}
