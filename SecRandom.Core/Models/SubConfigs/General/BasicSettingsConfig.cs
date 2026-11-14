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

    /// <summary>
    ///     结构化自动化 IPC（v1 JSON 请求、v2 RPC 与事件管道）的总开关。
    ///     默认开启；关闭只影响结构化通道，<c>ShowMainWindow</c>/<c>Restart</c>/<c>Url:</c>
    ///     这类无返回值的旧式生命周期转发仍然保留，因此"第二次启动转到已运行实例"不会被关掉。
    /// </summary>
    [ObservableProperty] private bool _automationIpcEnabled = true;

    // Stored separately from the user-facing switches so a disabled size setting retains its last size.
    [ObservableProperty] private double _mainWindowWidth = 1200;
    [ObservableProperty] private double _mainWindowHeight = 800;
    [ObservableProperty] private bool _mainWindowMaximized;
    [ObservableProperty] private double _settingsWindowWidth = 1000;
    [ObservableProperty] private double _settingsWindowHeight = 720;
    [ObservableProperty] private bool _settingsWindowMaximized;

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

    // Retained only to migrate installations that stored the device identifier in settings.json.
    [JsonIgnore] public Guid LegacyOfflineUserId { get; private set; }

    [JsonPropertyName("offline_user_id")]
    public Guid LegacyOfflineUserIdOnLoad
    {
        set => LegacyOfflineUserId = value;
    }

    // Hidden Configs
    [ObservableProperty] private bool _guideCompleted = false;
    [ObservableProperty] private int _acceptedEulaVersion;
    [ObservableProperty] private int _acceptedPrivacyPolicyVersion;
    [ObservableProperty] private int _acceptedGplVersion;
    [ObservableProperty] private int _acceptedVerificationNoticeVersion;
}
