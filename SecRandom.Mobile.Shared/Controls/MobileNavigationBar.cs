using Avalonia;
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
    private readonly Dictionary<Views.MobileDestination, ToggleButton> _buttons = [];
    private Views.MobileDestination _selectedDestination = Views.MobileDestination.Draw;

    public MobileNavigationBar()
    {
        var navigation = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 4,
            Margin = new Thickness(8, 6)
        };

        AddButton(navigation, 0, Langs.Mobile.Resources.N_Draw, FluentIcons.PeopleFilled, Views.MobileDestination.Draw);
        AddButton(navigation, 1, Langs.Mobile.Resources.N_History, FluentIcons.HistoryFilled, Views.MobileDestination.History);
        AddButton(navigation, 2, Langs.Mobile.Resources.N_Overview, FluentIcons.HomeFilled, Views.MobileDestination.Overview);
        AddButton(navigation, 3, Langs.Mobile.Resources.N_Settings, FluentIcons.SettingsFilled, Views.MobileDestination.Settings);

        _root = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = navigation
        };
        Content = _root;
        RefreshTheme();
        Select(Views.MobileDestination.Draw);
    }

    internal event EventHandler<Views.MobileDestination>? DestinationSelected;

    internal void Select(Views.MobileDestination destination)
    {
        _selectedDestination = destination;
        foreach (var (buttonDestination, button) in _buttons)
        {
            bool selected = buttonDestination == destination;
            button.IsChecked = selected;
            button.Background = selected ? Views.MobileTheme.PrimaryWash : Brushes.Transparent;
            button.Foreground = selected ? Views.MobileTheme.Primary : Views.MobileTheme.MutedText;
        }
    }

    public void RefreshTheme()
    {
        _root.Background = Views.MobileTheme.Surface;
        _root.BorderBrush = Views.MobileTheme.Border;
        Select(_selectedDestination);
    }

    private void AddButton(
        Grid navigation,
        int column,
        string text,
        string glyph,
        Views.MobileDestination destination)
    {
        var button = new ToggleButton
        {
            Content = new StackPanel
            {
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    MobileUi.CreateIcon(glyph, 20, HorizontalAlignment.Center),
                    new TextBlock
                    {
                        Text = text,
                        FontSize = 12,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.NoWrap
                    }
                }
            },
            MinHeight = 56,
            Padding = new Thickness(4, 5),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) =>
        {
            if (destination == _selectedDestination)
            {
                button.IsChecked = true;
                return;
            }

            Select(destination);
            DestinationSelected?.Invoke(this, destination);
        };
        _buttons.Add(destination, button);
        navigation.Children.Add(button);
        Grid.SetColumn(button, column);
    }
}
