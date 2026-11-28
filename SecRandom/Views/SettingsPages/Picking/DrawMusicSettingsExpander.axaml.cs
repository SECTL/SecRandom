using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.FeatureConfiguration;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Services.Music;

namespace SecRandom.Views.SettingsPages.Picking;

public partial class DrawMusicSettingsExpander : FASettingsExpander
{
    public static readonly StyledProperty<DrawSettingsConfigBase?> SettingsProperty =
        AvaloniaProperty.Register<DrawMusicSettingsExpander, DrawSettingsConfigBase?>(nameof(Settings));

    public static readonly StyledProperty<ObservableCollection<MusicSelection>?> MusicSelectionsProperty =
        AvaloniaProperty.Register<DrawMusicSettingsExpander, ObservableCollection<MusicSelection>?>(nameof(MusicSelections));

    public DrawMusicSettingsExpander()
    {
        if (IAppHost.TryGetService<FeatureRuntimeSnapshot>()?.IsEnabled(StartupFeatureCatalog.Music) == false)
        {
            IsVisible = false;
            return;
        }
        InitializeComponent();
    }

    public DrawSettingsConfigBase? Settings
    {
        get => GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    public ObservableCollection<MusicSelection>? MusicSelections
    {
        get => GetValue(MusicSelectionsProperty);
        set => SetValue(MusicSelectionsProperty, value);
    }
}
