using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;
using System.Text.Json.Serialization;

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

    [JsonIgnore] public bool? LegacyTelemetryEnabled { get; private set; }
    [JsonIgnore] public TelemetryMode? LegacyTelemetryMode { get; private set; }

    [JsonPropertyName("telemetry_enabled")]
    public bool LegacyTelemetryEnabledOnLoad
    {
        set => LegacyTelemetryEnabled = value;
    }

    [JsonPropertyName("telemetry_mode")]
    public TelemetryMode LegacyTelemetryModeOnLoad
    {
        set => LegacyTelemetryMode = value;
    }

    // Hidden Configs
    [ObservableProperty] private Guid _offlineUserId = Guid.NewGuid();
    [ObservableProperty] private bool _guideCompleted = false;
    [ObservableProperty] private bool _showVersionNotice = true;
}
