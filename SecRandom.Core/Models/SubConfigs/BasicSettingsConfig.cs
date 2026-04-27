using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class BasicSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _autostart = false;
    [ObservableProperty] private bool _showStartupWindow = true;
    [ObservableProperty] private bool _autoSaveWindowSize = true;
    [ObservableProperty] private TopmostMode _mainWindowTopmostMode = TopmostMode.None;
    [ObservableProperty] private bool _backgroundResident = true;
    [ObservableProperty] private bool _urlProtocol = false;

    [ObservableProperty] private ThemeMode _theme = ThemeMode.Auto;
    [ObservableProperty] private LanguageMode _language = LanguageMode.ChineseSimplified;
    [ObservableProperty] private string _font = GlobalConstants.DefaultFontFamily;
    [ObservableProperty] private FontWeightMode _fontWeight = FontWeightMode.Regular;
    [ObservableProperty] private DpiScaleMode _dpiScale = DpiScaleMode.Auto;
    [ObservableProperty] private Color _themeColor = Color.Parse(GlobalConstants.DefaultThemeColor);
    
    // Hidden Configs
    [ObservableProperty] private Guid _offlineUserId = Guid.NewGuid();
    [ObservableProperty] private bool _guideCompleted = false;
    [ObservableProperty] private bool _showVersionNotice = true;
}