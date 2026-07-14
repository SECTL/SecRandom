using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace SecRandom.Core.Models.SubConfigs;

public partial class NotificationSettingsConfig : ObservableObject, IJsonOnDeserialized
{
    private bool _hasDefault;

    [ObservableProperty] private NotificationChannelSettings _default = NotificationChannelSettings.CreateDisabled();
    [ObservableProperty] private OverridableNotificationChannelSettings _rollCall = new();
    [ObservableProperty] private OverridableNotificationChannelSettings _quickDraw = new()
    {
        Enabled = true,
        OverrideBasicSettings = true
    };
    [ObservableProperty] private OverridableNotificationChannelSettings _lottery = new();

    partial void OnDefaultChanged(NotificationChannelSettings value)
    {
        _hasDefault = true;
    }

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (_hasDefault)
            return;

        RollCall.EnableAllOverrides();
        QuickDraw.EnableAllOverrides();
        Lottery.EnableAllOverrides();
    }
}

public partial class NotificationChannelSettings : ObservableObject
{
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _animation = true;
    [ObservableProperty] private int _autoCloseTime = 5;
    [ObservableProperty] private string _enabledMonitor = "OFF";
    [ObservableProperty] private int _windowPosition = 0;
    [ObservableProperty] private int _horizontalOffset = 0;
    [ObservableProperty] private int _verticalOffset = 0;
    [ObservableProperty] private int _transparency = 80;
    [ObservableProperty] private int _notificationServiceType = 0;
    [ObservableProperty] private int _displayDuration = 5;
    [ObservableProperty] private bool _useMainWindowWhenExceedThreshold = true;
    [ObservableProperty] private int _mainWindowDisplayThreshold = 5;

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
}
