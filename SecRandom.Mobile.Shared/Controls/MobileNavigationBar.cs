using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Core.Icons;
using SecRandom.Mobile.Views;

namespace SecRandom.Mobile.Controls;

public sealed class MobileNavigationBar : UserControl
{
    private readonly Border _root;
    private readonly Dictionary<MobileDestination,
        (ToggleButton Button, TextBlock Icon, TextBlock Label, string RegularGlyph, string FilledGlyph)> _items = [];
    private MobileDestination _selectedDestination = MobileDestination.Draw;

    public MobileNavigationBar()
    {
        var destinations = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AddItem(destinations, 0, Langs.Mobile.Resources.N_Draw,
            FluentIcons.PeopleRegular, FluentIcons.PeopleFilled, MobileDestination.Draw);
        AddItem(destinations, 1, Langs.Mobile.Resources.N_History,
            FluentIcons.HistoryRegular, FluentIcons.HistoryFilled, MobileDestination.History);
        AddItem(destinations, 2, Langs.Mobile.Resources.N_Overview,
            FluentIcons.HomeRegular, FluentIcons.HomeFilled, MobileDestination.Overview);
        AddItem(destinations, 3, Langs.Mobile.Resources.N_Settings,
            FluentIcons.SettingsRegular, FluentIcons.SettingsFilled, MobileDestination.Settings);

        _root = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(6, 4),
            MinHeight = 64,
            Child = destinations
        };
        Content = _root;
        RefreshTheme();
        Select(MobileDestination.Draw);
    }

    internal event EventHandler<MobileDestination>? DestinationSelected;

    internal void Select(MobileDestination destination)
    {
        _selectedDestination = destination;
        RefreshSelection();
    }

    public void RefreshTheme()
    {
        _root.Background = MobileTheme.Surface;
        _root.BorderBrush = MobileTheme.Border;
        RefreshSelection();
    }

    private void AddItem(
        Grid destinations,
        int column,
        string text,
        string regularGlyph,
        string filledGlyph,
        MobileDestination destination)
    {
        var icon = MobileUi.CreateIcon(regularGlyph, 22, HorizontalAlignment.Center);
        var label = new TextBlock
        {
            Text = text,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var button = new ToggleButton
        {
            Tag = destination,
            MinHeight = 56,
            Margin = new Thickness(2, 0),
            Padding = new Thickness(4, 5),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new StackPanel
            {
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { icon, label }
            }
        };
        AutomationProperties.SetName(button, text);
        AutomationProperties.SetPositionInSet(button, column + 1);
        AutomationProperties.SetSizeOfSet(button, 4);
        button.Click += (_, _) => SelectFromInput(destination);
        _items.Add(destination, (button, icon, label, regularGlyph, filledGlyph));
        destinations.Children.Add(button);
        Grid.SetColumn(button, column);
    }

    private void SelectFromInput(MobileDestination destination)
    {
        if (destination == _selectedDestination)
        {
            RefreshSelection();
            return;
        }

        Select(destination);
        DestinationSelected?.Invoke(this, destination);
    }

    private void RefreshSelection()
    {
        foreach (var (destination, item) in _items)
        {
            var isSelected = destination == _selectedDestination;
            item.Button.IsChecked = isSelected;
            item.Button.Background = isSelected ? MobileTheme.PrimaryWash : Brushes.Transparent;
            item.Icon.Text = isSelected ? item.FilledGlyph : item.RegularGlyph;
            item.Icon.Foreground = isSelected ? MobileTheme.Primary : MobileTheme.MutedText;
            item.Label.Foreground = isSelected ? MobileTheme.Primary : MobileTheme.MutedText;
            item.Label.FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal;
        }
    }
}
