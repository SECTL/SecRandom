using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Views;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Notification;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.SettingsPages.Notification.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

public abstract class NotificationChannelSettingsPageBase : ViewBase, INotifyPropertyChanged
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
    public virtual string OverridableSettingsTitle => Text(nameof(OverridableSettingsTitle), "S_Common_OverridableSettings");
    public virtual string OverrideTitle => Text(nameof(OverrideTitle), "C_EnableOverride");
    public abstract string EnabledTitle { get; }
    public abstract string EnabledDescription { get; }
    public virtual string AnimationTitle => Text(nameof(AnimationTitle), "S_Default_Animation");
    public virtual string AnimationDescription => Text(nameof(AnimationDescription), "S_Default_Animation_D");
    public virtual string WindowPositionTitle => Text(nameof(WindowPositionTitle), "S_Default_WindowPosition");
    public virtual string WindowPositionDescription => Text(nameof(WindowPositionDescription), "S_Default_WindowPosition_D");
    public virtual string EnabledMonitorTitle => Text(nameof(EnabledMonitorTitle), "S_Common_EnabledMonitor");
    public virtual string EnabledMonitorDescription => Text(nameof(EnabledMonitorDescription), "S_Common_EnabledMonitor_D");
    public virtual string OffsetTitle => Text(nameof(OffsetTitle), "S_Default_Offset");
    public virtual string OffsetDescription => Text(nameof(OffsetDescription), "S_Default_Offset_D");
    public virtual string TransparencyTitle => Text(nameof(TransparencyTitle), "S_Default_Transparency");
    public virtual string TransparencyDescription => Text(nameof(TransparencyDescription), "S_Default_Transparency_D");
    public virtual string NotificationServiceTitle => Text(nameof(NotificationServiceTitle), "S_Common_NotificationService");
    public virtual string NotificationServiceTypeTitle => Text(nameof(NotificationServiceTypeTitle), "S_Common_NotificationServiceType");
    public virtual string NotificationServiceTypeDescription => Text(nameof(NotificationServiceTypeDescription), "S_Common_NotificationServiceType_D");
    public virtual string NotificationServiceFailureFallbackTitle => Text(
        nameof(NotificationServiceFailureFallbackTitle),
        "S_Common_NotificationServiceFailureFallback");
    public virtual string NotificationServiceFailureFallbackDescription => Text(
        nameof(NotificationServiceFailureFallbackDescription),
        "S_Common_NotificationServiceFailureFallback_D");
    public bool UsesExternalNotificationServiceOnly => ChannelSettings.UsesExternalNotificationService
        && !ChannelSettings.UsesBuiltInNotificationService;
    public virtual string DisplayDurationTitle => Text(nameof(DisplayDurationTitle), "S_Default_DisplayDuration");
    public virtual string DisplayDurationDescription => Text(nameof(DisplayDurationDescription), "S_Default_DisplayDuration_D");
    public virtual string UseMainWindowWhenExceedThresholdTitle => Text(nameof(UseMainWindowWhenExceedThresholdTitle), "S_Default_UseMainWindowWhenExceedThreshold");
    public virtual string UseMainWindowWhenExceedThresholdDescription => Text(nameof(UseMainWindowWhenExceedThresholdDescription), "S_Default_UseMainWindowWhenExceedThreshold_D");
    public virtual string MainWindowDisplayThresholdTitle => Text(nameof(MainWindowDisplayThresholdTitle), "S_Default_MainWindowDisplayThreshold");
    public virtual string MainWindowDisplayThresholdDescription => Text(nameof(MainWindowDisplayThresholdDescription), "S_Default_MainWindowDisplayThreshold_D");

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
            var nativeNames = WindowsMonitorNameProvider.GetNames();
            for (var index = 0; index < topLevel.Screens.All.Count; index++)
            {
                var screen = topLevel.Screens.All[index];
                nativeNames.TryGetValue(screen.Bounds.Position, out var nativeName);
                var displayName = string.IsNullOrWhiteSpace(screen.DisplayName) ? nativeName : screen.DisplayName;
                var identifier = NotificationMonitorIdentifier.Get(displayName, screen.Bounds);
                if (MonitorOptions.All(option => option.Value != identifier))
                {
                    var label = NotificationMonitorIdentifier.GetLabel(
                        displayName,
                        screen.Bounds,
                        screen.IsPrimary,
                        index,
                        Text("{0}: {1}x{2} @ {3},{4}{5}", "O_Monitor_Label"),
                        Text("（主显示器）", "O_Monitor_Primary"));
                    MonitorOptions.Add(new MonitorOption(identifier, label));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedMonitor)
            && MonitorOptions.All(option => option.Value != selectedMonitor))
            MonitorOptions.Add(new MonitorOption(selectedMonitor, selectedMonitor));

    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotificationChannelSettings.Enabled)
            or nameof(NotificationChannelSettings.Animation))
            OverrideSettings?.OverrideBasicSettings = true;

        if (e.PropertyName == nameof(NotificationChannelSettings.EnabledMonitor))
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMonitor)));
        if (e.PropertyName == nameof(NotificationChannelSettings.NotificationServiceType))
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UsesExternalNotificationServiceOnly)));
        ConfigHandler.Save();
    }
}

public sealed record MonitorOption(string Value, string Label);
