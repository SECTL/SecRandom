using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs.Personalized;

public partial class AppearanceSettingsConfig : ObservableObject
{
    [ObservableProperty] private ThemeMode _theme = ThemeMode.Auto;
    [ObservableProperty] private string _font = GlobalConstants.DefaultFontFamily;
    [ObservableProperty] private FontWeightMode _fontWeight = FontWeightMode.Regular;
    [ObservableProperty] private Color _themeColor = Color.Parse(GlobalConstants.DefaultThemeColor);
}