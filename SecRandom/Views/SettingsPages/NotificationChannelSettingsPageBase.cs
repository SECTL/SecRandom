using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.SettingsPages.Notification.Resources;

namespace SecRandom.Views.SettingsPages;

public abstract class NotificationChannelSettingsPageBase : UserControl
{
    protected NotificationChannelSettingsPageBase()
    {
        ChannelSettings = SelectChannelSettings(ViewModel.Config.NotificationSettings);
        DataContext = this;
        ChannelSettings.PropertyChanged += SettingsOnPropertyChanged;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public NotificationChannelSettings ChannelSettings { get; }
    public abstract string ChannelTitle { get; }
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

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
    }
}
