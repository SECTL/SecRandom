using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        MobileResources.BindBrush(_root, Border.BackgroundProperty, MobileResources.Keys.Surface);
        MobileResources.BindBrush(_root, Border.BorderBrushProperty, MobileResources.Keys.Border);
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
            if (isSelected)
                MobileResources.BindBrush(item.Button, ToggleButton.BackgroundProperty, MobileResources.Keys.PrimaryWash);
            else
                item.Button.ClearValue(ToggleButton.BackgroundProperty);
            item.Icon.Text = isSelected ? item.FilledGlyph : item.RegularGlyph;
            MobileResources.BindBrush(item.Icon, TextBlock.ForegroundProperty,
                isSelected ? MobileResources.Keys.Primary : MobileResources.Keys.MutedText);
            MobileResources.BindBrush(item.Label, TextBlock.ForegroundProperty,
                isSelected ? MobileResources.Keys.Primary : MobileResources.Keys.MutedText);
            item.Label.FontWeight = isSelected ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
