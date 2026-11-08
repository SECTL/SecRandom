using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs;

public partial class NotificationSettingsConfig : ObservableObject, IJsonOnDeserialized
{
    private bool _hasDefault;
    private bool _hasOverrideDefaultsInitialized;
    private bool _overrideDefaultsInitialized = true;
    private bool _hasBasicSettingsSeparatedInitialized;
    private bool _basicSettingsSeparatedInitialized = true;
    private NotificationChannelSettings _default = NotificationChannelSettings.CreateDisabled();
    private OverridableNotificationChannelSettings _rollCall = new();
    private OverridableNotificationChannelSettings _quickDraw = CreateQuickDraw();
    private OverridableNotificationChannelSettings _lottery = new();
    private FullscreenNotificationSettings _fullscreen = new();

    [AllowNull]
    public NotificationChannelSettings Default
    {
        get => _default;
        set
        {
            _hasDefault = value is not null;
            SetProperty(ref _default, value ?? NotificationChannelSettings.CreateDisabled());
        }
    }

    [AllowNull]
    public OverridableNotificationChannelSettings RollCall
    {
        get => _rollCall;
        set => SetProperty(ref _rollCall, value ?? new OverridableNotificationChannelSettings());
    }

    [AllowNull]
    public OverridableNotificationChannelSettings QuickDraw
    {
        get => _quickDraw;
        set => SetProperty(ref _quickDraw, value ?? CreateQuickDraw());
    }

    [AllowNull]
    public OverridableNotificationChannelSettings Lottery
    {
        get => _lottery;
        set => SetProperty(ref _lottery, value ?? new OverridableNotificationChannelSettings());
    }

    /// <summary>
    ///     全屏结果通知渠道的外观、文案、动画与倒计时设置。
    /// </summary>
    /// <remarks>
    ///     全屏渠道是**独立的第四种渠道值**（<c>NotificationServiceType == 3</c>），
    ///     与内置浮窗、ClassIsland 平级且互斥；这里的设置只服务全屏窗口自己，
    ///     不会改变 QuickDraw 浮窗的任何行为。
    /// </remarks>
    [AllowNull]
    public FullscreenNotificationSettings Fullscreen
    {
        get => _fullscreen;
        set => SetProperty(ref _fullscreen, value ?? new FullscreenNotificationSettings());
    }

    [JsonInclude]
    public bool OverrideDefaultsInitialized
    {
        get => _overrideDefaultsInitialized;
        private set
        {
            _hasOverrideDefaultsInitialized = true;
            _overrideDefaultsInitialized = value;
        }
    }

    [JsonInclude]
    public bool BasicSettingsSeparatedInitialized
    {
        get => _basicSettingsSeparatedInitialized;
        private set
        {
            _hasBasicSettingsSeparatedInitialized = true;
            _basicSettingsSeparatedInitialized = value;
        }
    }

    private static OverridableNotificationChannelSettings CreateQuickDraw()
    {
        var settings = OverridableNotificationChannelSettings.CreateEnabled();
        return settings;
    }

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (!_hasOverrideDefaultsInitialized || !_overrideDefaultsInitialized)
        {
            if (_hasDefault)
            {
                ConfigureLegacyGlobalChannel(RollCall);
                ConfigureLegacyGlobalChannel(QuickDraw);
                ConfigureLegacyGlobalChannel(Lottery);
            }
            else
            {
                RollCall.EnableAllOverrides();
                QuickDraw.EnableAllOverrides();
                Lottery.EnableAllOverrides();
            }
            _overrideDefaultsInitialized = true;
        }

        if (_hasBasicSettingsSeparatedInitialized && _basicSettingsSeparatedInitialized)
            return;

        CopyInheritedBasicSettings(RollCall);
        CopyInheritedBasicSettings(QuickDraw);
        CopyInheritedBasicSettings(Lottery);
        _basicSettingsSeparatedInitialized = true;
    }

    private void CopyInheritedBasicSettings(OverridableNotificationChannelSettings channel)
    {
        if (channel.OverrideBasicSettings)
            return;

        channel.Enabled = Default.Enabled;
        channel.Animation = Default.Animation;
        channel.OverrideBasicSettings = true;
    }

    private static void ConfigureLegacyGlobalChannel(OverridableNotificationChannelSettings channel)
    {
        channel.OverrideBasicSettings = channel.HasExplicitEnabled;
        channel.OverrideNotificationWindowSettings = false;
        channel.OverrideServiceSettings = false;
    }
}

public partial class NotificationChannelSettings : ObservableObject, IJsonOnDeserialized
{
    private bool _enabled;
    private bool _hasExplicitEnabled;
    [ObservableProperty] private bool _animation = true;
    private string _enabledMonitor = "";
    [ObservableProperty] private int _windowPosition = 0;
    [ObservableProperty] private int _horizontalOffset = 0;
    [ObservableProperty] private int _verticalOffset = 0;
    [ObservableProperty] private int _transparency = 80;
    private int _notificationServiceType;
    [ObservableProperty] private int _displayDuration = 3;
    private bool _hasDisplayDuration;
    private int? _legacyAutoCloseTime;
    private bool _useBuiltInOnServiceFailure = true;
    private bool _hasUseBuiltInOnServiceFailure;
    private bool? _legacyUseBuiltInOnClassIslandFailure;
    [ObservableProperty] private bool _useMainWindowWhenExceedThreshold = true;
    [ObservableProperty] private int _mainWindowDisplayThreshold = 5;

    public bool UseBuiltInOnServiceFailure
    {
        get => _useBuiltInOnServiceFailure;
        set
        {
            _hasUseBuiltInOnServiceFailure = true;
            SetProperty(ref _useBuiltInOnServiceFailure, value);
        }
    }

    [JsonPropertyName("auto_close_time")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LegacyAutoCloseTime
    {
        get => null;
        set => _legacyAutoCloseTime = value;
    }

    [JsonPropertyName("use_built_in_on_class_island_failure")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyUseBuiltInOnClassIslandFailure
    {
        get => null;
        set => _legacyUseBuiltInOnClassIslandFailure = value;
    }

    [AllowNull]
    public string EnabledMonitor
    {
        get => _enabledMonitor;
        set => SetProperty(
            ref _enabledMonitor,
            string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "OFF", StringComparison.OrdinalIgnoreCase)
                ? ""
                : value);
    }

    public int NotificationServiceType
    {
        get => _notificationServiceType;
        set
        {
            // 0=内置浮窗、1=ClassIsland、2=同时使用（内置+ClassIsland）、3=全屏。
            // 值 3 是追加值，0/1/2 的含义逐字节不变；越界值仍回落 0。
            if (!SetProperty(ref _notificationServiceType, value is >= 0 and <= 3 ? value : 0))
                return;

            OnPropertyChanged(nameof(UsesBuiltInNotificationService));
            OnPropertyChanged(nameof(UsesExternalNotificationService));
            OnPropertyChanged(nameof(UsesFullscreenNotificationService));
        }
    }

    [JsonIgnore]
    public bool UsesBuiltInNotificationService => NotificationServiceType is 0 or 2;

    [JsonIgnore]
    public bool UsesExternalNotificationService => NotificationServiceType is 1 or 2;

    /// <summary>
    ///     是否使用全屏结果通知渠道。与另外两条渠道属性互斥："同时使用"永远只含内置 + ClassIsland。
    /// </summary>
    [JsonIgnore]
    public bool UsesFullscreenNotificationService => NotificationServiceType == 3;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _hasExplicitEnabled = true;
            SetProperty(ref _enabled, value);
        }
    }

    internal bool HasExplicitEnabled => _hasExplicitEnabled;

    public NotificationChannelSettings()
    {
    }

    private NotificationChannelSettings(bool enabledByDefault)
    {
        _enabled = enabledByDefault;
    }

    public static NotificationChannelSettings CreateDisabled() => new(false);

    protected void SetDefaultEnabled(bool value)
    {
        _enabled = value;
    }

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (!_hasDisplayDuration && _legacyAutoCloseTime is { } legacyAutoCloseTime)
            DisplayDuration = Math.Clamp(legacyAutoCloseTime, 1, 60);

        if (!_hasUseBuiltInOnServiceFailure && _legacyUseBuiltInOnClassIslandFailure is { } legacyValue)
            _useBuiltInOnServiceFailure = legacyValue;

        _legacyAutoCloseTime = null;
        _legacyUseBuiltInOnClassIslandFailure = null;
    }

    partial void OnDisplayDurationChanging(int value) => _hasDisplayDuration = true;
}

public partial class OverridableNotificationChannelSettings : NotificationChannelSettings
{
    [ObservableProperty] private bool _overrideBasicSettings;
    [ObservableProperty] private bool _overrideNotificationWindowSettings;
    [ObservableProperty] private bool _overrideServiceSettings;

    public void EnableAllOverrides()
    {
        OverrideBasicSettings = true;
        OverrideNotificationWindowSettings = true;
        OverrideServiceSettings = true;
    }

    public void DisableAllOverrides()
    {
        OverrideBasicSettings = false;
        OverrideNotificationWindowSettings = false;
        OverrideServiceSettings = false;
    }

    public static OverridableNotificationChannelSettings CreateEnabled()
    {
        var settings = new OverridableNotificationChannelSettings();
        settings.SetDefaultEnabled(true);
        settings.OverrideBasicSettings = true;
        return settings;
    }
}
