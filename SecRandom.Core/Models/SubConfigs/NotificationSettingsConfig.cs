using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs;

public partial class NotificationSettingsConfig : ObservableObject, IJsonOnDeserialized
{
    private bool _hasDefault;
    private bool _hasOverrideDefaultsInitialized;
    private bool _overrideDefaultsInitialized = true;
    private NotificationChannelSettings _default = NotificationChannelSettings.CreateDisabled();
    private OverridableNotificationChannelSettings _rollCall = new();
    private OverridableNotificationChannelSettings _quickDraw = CreateQuickDraw();
    private OverridableNotificationChannelSettings _lottery = new();

    [AllowNull]
    public NotificationChannelSettings Default
    {
        get => _default;
        set
        {
            _hasDefault = true;
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

    private static OverridableNotificationChannelSettings CreateQuickDraw() => new()
    {
        Enabled = true
    };

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (_hasOverrideDefaultsInitialized && _overrideDefaultsInitialized)
            return;

        if (_hasDefault)
        {
            RollCall.DisableAllOverrides();
            QuickDraw.DisableAllOverrides();
            Lottery.DisableAllOverrides();
        }
        else
        {
            RollCall.EnableAllOverrides();
            QuickDraw.EnableAllOverrides();
            Lottery.EnableAllOverrides();
        }
        _overrideDefaultsInitialized = true;
    }
}

public partial class NotificationChannelSettings : ObservableObject
{
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _animation = true;
    [ObservableProperty] private int _autoCloseTime = 5;
    private string _enabledMonitor = "";
    [ObservableProperty] private int _windowPosition = 0;
    [ObservableProperty] private int _horizontalOffset = 0;
    [ObservableProperty] private int _verticalOffset = 0;
    [ObservableProperty] private int _transparency = 80;
    private int _notificationServiceType;
    [ObservableProperty] private int _displayDuration = 5;
    [ObservableProperty] private bool _useMainWindowWhenExceedThreshold = true;
    [ObservableProperty] private int _mainWindowDisplayThreshold = 5;

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
        set => SetProperty(ref _notificationServiceType, value is >= 0 and <= 2 ? value : 0);
    }

    public NotificationChannelSettings()
    {
    }

    private NotificationChannelSettings(bool enabledByDefault)
    {
        _enabled = enabledByDefault;
    }

    public static NotificationChannelSettings CreateDisabled() => new(false);
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
}
