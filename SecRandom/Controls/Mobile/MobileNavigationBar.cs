using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Icons;
using SecRandom.Views.Mobile;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.Controls.Mobile;

public sealed partial class MobileNavigationBar : UserControl
{
    private readonly Dictionary<MobileDestination,
        (ToggleButton Button, TextBlock Icon, TextBlock Label, string RegularGlyph, string FilledGlyph)> _items = [];
    private MobileDestination _selectedDestination = MobileDestination.Draw;

    public MobileNavigationBar()
    {
        InitializeComponent();
        AddItem(this.FindControl<ToggleButton>("DrawButton")!,
            this.FindControl<TextBlock>("DrawIcon")!, this.FindControl<TextBlock>("DrawLabel")!,
            LR.N_Draw, FluentIcons.PeopleRegular, FluentIcons.PeopleFilled, MobileDestination.Draw, 1);
        AddItem(this.FindControl<ToggleButton>("HistoryButton")!,
            this.FindControl<TextBlock>("HistoryIcon")!, this.FindControl<TextBlock>("HistoryLabel")!,
            LR.N_History, FluentIcons.HistoryRegular, FluentIcons.HistoryFilled, MobileDestination.History, 2);
        AddItem(this.FindControl<ToggleButton>("OverviewButton")!,
            this.FindControl<TextBlock>("OverviewIcon")!, this.FindControl<TextBlock>("OverviewLabel")!,
            LR.N_Overview, FluentIcons.HomeRegular, FluentIcons.HomeFilled, MobileDestination.Overview, 3);
        AddItem(this.FindControl<ToggleButton>("SettingsButton")!,
            this.FindControl<TextBlock>("SettingsIcon")!, this.FindControl<TextBlock>("SettingsLabel")!,
            LR.N_Settings, FluentIcons.SettingsRegular, FluentIcons.SettingsFilled, MobileDestination.Settings, 4);
        Select(MobileDestination.Draw);
    }

    internal event EventHandler<MobileDestination>? DestinationSelected;

    internal void Select(MobileDestination destination)
    {
        _selectedDestination = destination;
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
        }
        else
        {
            Select(destination);
        }

        DestinationSelected?.Invoke(this, destination);
    }

    private void RefreshSelection()
    {
        foreach (var (destination, item) in _items)
        {
            var isSelected = destination == _selectedDestination;
            item.Button.IsChecked = isSelected;
            item.Icon.Text = isSelected ? item.FilledGlyph : item.RegularGlyph;
            item.Label.FontWeight = isSelected ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
