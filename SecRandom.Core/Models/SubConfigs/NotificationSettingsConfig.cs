using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs;

public partial class NotificationSettingsConfig : ObservableObject
{
    [ObservableProperty] private NotificationChannelSettings _rollCall = NotificationChannelSettings.CreateDisabled();
    [ObservableProperty] private NotificationChannelSettings _quickDraw = NotificationChannelSettings.CreateEnabled();
    [ObservableProperty] private NotificationChannelSettings _lottery = NotificationChannelSettings.CreateDisabled();
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
    [ObservableProperty] private string _floatingWindowEnabledMonitor = "OFF";
    [ObservableProperty] private int _floatingWindowPosition = 0;
    [ObservableProperty] private int _floatingWindowHorizontalOffset = 0;
    [ObservableProperty] private int _floatingWindowVerticalOffset = 0;
    [ObservableProperty] private int _floatingWindowTransparency = 80;
    [ObservableProperty] private int _floatingWindowAutoCloseTime = 5;
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

    public static NotificationChannelSettings CreateEnabled() => new(true);

    public static NotificationChannelSettings CreateDisabled() => new(false);
}
