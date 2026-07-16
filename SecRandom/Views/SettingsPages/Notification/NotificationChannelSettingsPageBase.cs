using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.SettingsPages.Notification.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

public abstract class NotificationChannelSettingsPageBase : UserControl, INotifyPropertyChanged
{
    protected NotificationChannelSettingsPageBase()
    {
        ChannelSettings = SelectChannelSettings(ViewModel.Config.NotificationSettings);
        MonitorOptions = [new MonitorOption("", Text("Not specified", "O_Monitor_Unspecified"))];
        if (!string.IsNullOrWhiteSpace(ChannelSettings.EnabledMonitor))
            MonitorOptions.Add(new MonitorOption(ChannelSettings.EnabledMonitor, ChannelSettings.EnabledMonitor));
        DataContext = this;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public NotificationChannelSettings ChannelSettings { get; }
    public ObservableCollection<MonitorOption> MonitorOptions { get; }
    public MonitorOption? SelectedMonitor
    {
        get => MonitorOptions.FirstOrDefault(option => option.Value == ChannelSettings.EnabledMonitor);
        set
        {
            if (value is null || value.Value == ChannelSettings.EnabledMonitor)
                return;

            ChannelSettings.EnabledMonitor = value.Value;
        }
    }
    public OverridableNotificationChannelSettings? OverrideSettings => ChannelSettings as OverridableNotificationChannelSettings;
    public bool CanOverride => OverrideSettings is not null;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }
    public bool OverrideBasicSettings
    {
        get => OverrideSettings?.OverrideBasicSettings ?? true;
        set
        {
            if (OverrideSettings is null || OverrideSettings.OverrideBasicSettings == value)
                return;

            OverrideSettings.OverrideBasicSettings = value;
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OverrideBasicSettings)));
        }
    }
    public bool OverrideNotificationWindowSettings
    {
        get => OverrideSettings?.OverrideNotificationWindowSettings ?? true;
        set
        {
            if (OverrideSettings is null || OverrideSettings.OverrideNotificationWindowSettings == value)
                return;

            OverrideSettings.OverrideNotificationWindowSettings = value;
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OverrideNotificationWindowSettings)));
        }
    }
    public bool OverrideServiceSettings
    {
        get => OverrideSettings?.OverrideServiceSettings ?? true;
        set
        {
            if (OverrideSettings is null || OverrideSettings.OverrideServiceSettings == value)
                return;

            OverrideSettings.OverrideServiceSettings = value;
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OverrideServiceSettings)));
        }
    }
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
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Screens is not null)
        {
            foreach (var screen in topLevel.Screens.All)
            {
                if (!string.IsNullOrWhiteSpace(screen.DisplayName)
                    && MonitorOptions.All(option => option.Value != screen.DisplayName))
                    MonitorOptions.Add(new MonitorOption(screen.DisplayName, screen.DisplayName));
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedMonitor)
            && MonitorOptions.All(option => option.Value != selectedMonitor))
            MonitorOptions.Add(new MonitorOption(selectedMonitor, selectedMonitor));

    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationChannelSettings.EnabledMonitor))
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMonitor)));
        ConfigHandler.Save();
    }
}

public sealed record MonitorOption(string Value, string Label);
