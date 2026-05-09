using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class BasicSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _autoSaveWindowSize = true;
    [ObservableProperty] private bool _autostart;
    [ObservableProperty] private bool _backgroundResident = true;
    [ObservableProperty] private string _font = GlobalConstants.DefaultFontFamily;
    [ObservableProperty] private FontWeightMode _fontWeight = FontWeightMode.Regular;
    [ObservableProperty] private bool _guideCompleted;
    [ObservableProperty] private LanguageMode _language = LanguageMode.ChineseSimplified;
    [ObservableProperty] private TopmostMode _mainWindowTopmostMode = TopmostMode.None;

    // Hidden Configs
    [ObservableProperty] private Guid _offlineUserId = Guid.NewGuid();
    [ObservableProperty] private bool _showStartupWindow = true;
    [ObservableProperty] private bool _showVersionNotice = true;

    [ObservableProperty] private bool _telemetryEnabled = true;
    [ObservableProperty] private TelemetryMode _telemetryMode = TelemetryMode.Full;

    [ObservableProperty] private ThemeMode _theme = ThemeMode.Auto;
    [ObservableProperty] private Color _themeColor = Color.Parse(GlobalConstants.DefaultThemeColor);
    [ObservableProperty] private bool _urlProtocol;
}