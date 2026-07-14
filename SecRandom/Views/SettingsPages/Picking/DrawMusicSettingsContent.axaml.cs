using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Services.Music;

namespace SecRandom.Views.SettingsPages.Picking;

public partial class DrawMusicSettingsContent : UserControl
{
    public static readonly StyledProperty<DrawSettingsConfigBase?> SettingsProperty =
        AvaloniaProperty.Register<DrawMusicSettingsContent, DrawSettingsConfigBase?>(nameof(Settings));

    public static readonly StyledProperty<ObservableCollection<MusicSelection>?> MusicSelectionsProperty =
        AvaloniaProperty.Register<DrawMusicSettingsContent, ObservableCollection<MusicSelection>?>(nameof(MusicSelections));

    public DrawMusicSettingsContent()
    {
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
