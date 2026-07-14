using System.ComponentModel;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.SettingsPages.Notification.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

public abstract class NotificationChannelSettingsPageBase : UserControl
{
    protected NotificationChannelSettingsPageBase()
    {
        ChannelSettings = SelectChannelSettings(ViewModel.Config.NotificationSettings);
        DataContext = this;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public NotificationChannelSettings ChannelSettings { get; }
    public ObservableCollection<string> MonitorOptions { get; } = ["OFF"];
    public OverridableNotificationChannelSettings? OverrideSettings => ChannelSettings as OverridableNotificationChannelSettings;
    public bool CanOverride => OverrideSettings is not null;
    public bool OverrideBasicSettings
    {
        get => OverrideSettings?.OverrideBasicSettings ?? true;
        set
        {
            if (OverrideSettings is not null)
                OverrideSettings.OverrideBasicSettings = value;
        }
    }
    public bool OverrideNotificationWindowSettings
    {
        get => OverrideSettings?.OverrideNotificationWindowSettings ?? true;
        set
        {
            if (OverrideSettings is not null)
                OverrideSettings.OverrideNotificationWindowSettings = value;
        }
    }
    public bool OverrideServiceSettings
    {
        get => OverrideSettings?.OverrideServiceSettings ?? true;
        set
        {
            if (OverrideSettings is not null)
                OverrideSettings.OverrideServiceSettings = value;
        }
    }
    public abstract string ChannelTitle { get; }
    public virtual string BasicSettingsTitle => Text(nameof(BasicSettingsTitle), "S_Common_BasicSettings");
    public virtual string NotificationWindowSettingsTitle => Text(nameof(NotificationWindowSettingsTitle), "S_Common_NotificationWindowSettings");
    public virtual string OverrideTitle => Text(nameof(OverrideTitle), "C_EnableOverride");
    public abstract string EnabledTitle { get; }
    public abstract string EnabledDescription { get; }
    public abstract string AnimationTitle { get; }
    public abstract string AnimationDescription { get; }
    public abstract string AutoCloseTimeTitle { get; }
    public abstract string AutoCloseTimeDescription { get; }
    public abstract string WindowPositionTitle { get; }
    public abstract string WindowPositionDescription { get; }
    public virtual string EnabledMonitorTitle => Text(nameof(EnabledMonitorTitle), "S_Common_EnabledMonitor");
    public virtual string EnabledMonitorDescription => Text(nameof(EnabledMonitorDescription), "S_Common_EnabledMonitor_D");
    public abstract string OffsetTitle { get; }
    public abstract string OffsetDescription { get; }
    public abstract string TransparencyTitle { get; }
    public abstract string TransparencyDescription { get; }
    public virtual string FloatingWindowTitle => Text(nameof(FloatingWindowTitle), "S_Common_FloatingWindow");
    public virtual string FloatingWindowEnabledMonitorTitle => Text(nameof(FloatingWindowEnabledMonitorTitle), "S_Common_FloatingWindowEnabledMonitor");
    public virtual string FloatingWindowEnabledMonitorDescription => Text(nameof(FloatingWindowEnabledMonitorDescription), "S_Common_FloatingWindowEnabledMonitor_D");
    public virtual string FloatingWindowPositionTitle => Text(nameof(FloatingWindowPositionTitle), "S_Common_FloatingWindowPosition");
    public virtual string FloatingWindowPositionDescription => Text(nameof(FloatingWindowPositionDescription), "S_Common_FloatingWindowPosition_D");
    public virtual string FloatingWindowOffsetTitle => Text(nameof(FloatingWindowOffsetTitle), "S_Common_FloatingWindowOffset");
    public virtual string FloatingWindowOffsetDescription => Text(nameof(FloatingWindowOffsetDescription), "S_Common_FloatingWindowOffset_D");
    public virtual string FloatingWindowTransparencyTitle => Text(nameof(FloatingWindowTransparencyTitle), "S_Common_FloatingWindowTransparency");
    public virtual string FloatingWindowTransparencyDescription => Text(nameof(FloatingWindowTransparencyDescription), "S_Common_FloatingWindowTransparency_D");
    public virtual string FloatingWindowAutoCloseTimeTitle => Text(nameof(FloatingWindowAutoCloseTimeTitle), "S_Common_FloatingWindowAutoCloseTime");
    public virtual string FloatingWindowAutoCloseTimeDescription => Text(nameof(FloatingWindowAutoCloseTimeDescription), "S_Common_FloatingWindowAutoCloseTime_D");
    public virtual string NotificationServiceTitle => Text(nameof(NotificationServiceTitle), "S_Common_NotificationService");
    public virtual string NotificationServiceTypeTitle => Text(nameof(NotificationServiceTypeTitle), "S_Common_NotificationServiceType");
    public virtual string NotificationServiceTypeDescription => Text(nameof(NotificationServiceTypeDescription), "S_Common_NotificationServiceType_D");
    public abstract string DisplayDurationTitle { get; }
    public abstract string DisplayDurationDescription { get; }
    public abstract string UseMainWindowWhenExceedThresholdTitle { get; }
    public abstract string UseMainWindowWhenExceedThresholdDescription { get; }
    public abstract string MainWindowDisplayThresholdTitle { get; }
    public abstract string MainWindowDisplayThresholdDescription { get; }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    protected abstract NotificationChannelSettings SelectChannelSettings(NotificationSettingsConfig settings);

    private static string Text(string fallback, string key)
    {
        return LR.ResourceManager.GetString(key, LR.Culture) ?? fallback;
    }

    protected void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        ChannelSettings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ChannelSettings.PropertyChanged -= SettingsOnPropertyChanged;
        ChannelSettings.PropertyChanged += SettingsOnPropertyChanged;

        var selectedMonitor = ChannelSettings.EnabledMonitor;
        MonitorOptions.Clear();
        MonitorOptions.Add("OFF");
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Screens is not null)
        {
            foreach (var screen in topLevel.Screens.All)
            {
                if (!string.IsNullOrWhiteSpace(screen.DisplayName))
                    MonitorOptions.Add(screen.DisplayName);
            }
        }

        if (!MonitorOptions.Contains(selectedMonitor))
            MonitorOptions.Add(selectedMonitor);
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
    }
}
