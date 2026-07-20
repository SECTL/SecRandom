using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Icons;
using SecRandom.Mobile.Views;

namespace SecRandom.Mobile.Controls;

public sealed partial class MobileNavigationBar : UserControl
{
    private readonly Border _root;
    private readonly Dictionary<MobileDestination,
        (ToggleButton Button, TextBlock Icon, TextBlock Label, string RegularGlyph, string FilledGlyph)> _items = [];
    private MobileDestination _selectedDestination = MobileDestination.Draw;

    public MobileNavigationBar()
    {
        InitializeComponent();
        _root = this.FindControl<Border>("Root")!;
        AddItem(this.FindControl<ToggleButton>("DrawButton")!,
            this.FindControl<TextBlock>("DrawIcon")!, this.FindControl<TextBlock>("DrawLabel")!,
            Langs.Mobile.Resources.N_Draw, FluentIcons.PeopleRegular, FluentIcons.PeopleFilled, MobileDestination.Draw, 1);
        AddItem(this.FindControl<ToggleButton>("HistoryButton")!,
            this.FindControl<TextBlock>("HistoryIcon")!, this.FindControl<TextBlock>("HistoryLabel")!,
            Langs.Mobile.Resources.N_History, FluentIcons.HistoryRegular, FluentIcons.HistoryFilled, MobileDestination.History, 2);
        AddItem(this.FindControl<ToggleButton>("OverviewButton")!,
            this.FindControl<TextBlock>("OverviewIcon")!, this.FindControl<TextBlock>("OverviewLabel")!,
            Langs.Mobile.Resources.N_Overview, FluentIcons.HomeRegular, FluentIcons.HomeFilled, MobileDestination.Overview, 3);
        AddItem(this.FindControl<ToggleButton>("SettingsButton")!,
            this.FindControl<TextBlock>("SettingsIcon")!, this.FindControl<TextBlock>("SettingsLabel")!,
            Langs.Mobile.Resources.N_Settings, FluentIcons.SettingsRegular, FluentIcons.SettingsFilled, MobileDestination.Settings, 4);
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
        ToggleButton button,
        TextBlock icon,
        TextBlock label,
        string text,
        string regularGlyph,
        string filledGlyph,
        MobileDestination destination,
        int position)
    {
        button.Tag = destination;
        icon.Text = regularGlyph;
        label.Text = text;
        AutomationProperties.SetName(button, text);
        AutomationProperties.SetPositionInSet(button, position);
        AutomationProperties.SetSizeOfSet(button, 4);
        button.Click += (_, _) => SelectFromInput(destination);
        _items.Add(destination, (button, icon, label, regularGlyph, filledGlyph));
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

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
