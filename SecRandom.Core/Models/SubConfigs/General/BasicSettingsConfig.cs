using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs.General;

public partial class BasicSettingsConfig : ObservableObject
{
    [ObservableProperty] private LanguageMode _language = LanguageMode.ChineseSimplified;
    [ObservableProperty] private bool _autostart = false;
    [ObservableProperty] private bool _showStartupWindow = true;
    [ObservableProperty] private bool _autoSaveWindowSize = true;
    [ObservableProperty] private TopmostMode _mainWindowTopmostMode = TopmostMode.None;
    [ObservableProperty] private bool _backgroundResident = true;
    [ObservableProperty] private bool _urlProtocol = false;
    
    [ObservableProperty] private bool _telemetryEnabled = true;
    [ObservableProperty] private TelemetryMode _telemetryMode = TelemetryMode.Full;
    
    // Hidden Configs
    [ObservableProperty] private Guid _offlineUserId = Guid.NewGuid();
    [ObservableProperty] private bool _guideCompleted = false;
    [ObservableProperty] private bool _showVersionNotice = true;
}