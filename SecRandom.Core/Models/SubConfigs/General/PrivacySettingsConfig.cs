using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;
using System.Text.Json.Serialization;

namespace SecRandom.Core.Models.SubConfigs.General;

public partial class PrivacySettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _sentryTelemetryEnabled = true;
    [ObservableProperty] private OnlineStatusMode _onlineStatusMode = OnlineStatusMode.Full;

    private bool? _legacyTelemetryEnabled;
    private TelemetryMode? _legacyTelemetryMode;

    public bool ShouldInitializeSentryTelemetry => SentryTelemetryEnabled;

    [JsonPropertyName("telemetry_enabled")]
    public bool LegacyTelemetryEnabledOnLoad
    {
        set
        {
            _legacyTelemetryEnabled = value;
            ApplyLegacyTelemetry(_legacyTelemetryEnabled, _legacyTelemetryMode);
        }
    }

    [JsonPropertyName("telemetry_mode")]
    public TelemetryMode LegacyTelemetryModeOnLoad
    {
        set
        {
            _legacyTelemetryMode = value;
            ApplyLegacyTelemetry(_legacyTelemetryEnabled, _legacyTelemetryMode);
        }
    }

    public void ApplyLegacyTelemetry(bool? telemetryEnabled, TelemetryMode? telemetryMode)
    {
        if (telemetryEnabled is null && telemetryMode is null)
            return;

        SentryTelemetryEnabled = telemetryEnabled ?? SentryTelemetryEnabled;
        OnlineStatusMode = ResolveLegacyOnlineStatusMode(telemetryEnabled, telemetryMode);
    }

    public static OnlineStatusMode ResolveLegacyOnlineStatusMode(bool? telemetryEnabled, TelemetryMode? telemetryMode)
    {
        if (telemetryEnabled == false || telemetryMode == TelemetryMode.Off)
            return OnlineStatusMode.Off;

        return telemetryMode switch
        {
            TelemetryMode.Anonymous => OnlineStatusMode.Anonymous,
            TelemetryMode.Full => OnlineStatusMode.Full,
            _ => OnlineStatusMode.Full
        };
    }
}
