using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.FeatureConfiguration;
using SecRandom.Services.Notification;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.SettingsPages.Notification.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

public abstract class NotificationChannelSettingsPageBase : UserControl, INotifyPropertyChanged
{
    // Stored value of the fullscreen channel; must match NotificationChannelSettings.NotificationServiceType's
    // persisted encoding (0 built-in, 1 ClassIsland, 2 both, 3 fullscreen). The dropdown displays them in a
    // different order, which is why it binds a mapped option object instead of SelectedIndex.
    public const int FullscreenNotificationServiceType = 3;

    protected NotificationChannelSettingsPageBase()
    {
        ChannelSettings = SelectChannelSettings(ViewModel.Config.NotificationSettings);
        NotificationServiceOptions = BuildNotificationServiceOptions();
        DataContext = this;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public NotificationChannelSettings ChannelSettings { get; }
    public IReadOnlyList<NotificationServiceOption> NotificationServiceOptions { get; private set; }
    public NotificationServiceOption SelectedNotificationServiceOption
    {
        get => NotificationServiceOptions.FirstOrDefault(option => option.Value == ChannelSettings.NotificationServiceType)
               ?? NotificationServiceOptions[0];
        set
        {
            if (value is null || value.Value == ChannelSettings.NotificationServiceType)
                return;

            ApplyNotificationServiceType(value.Value);
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
    public virtual string OverridableSettingsTitle => Text(nameof(OverridableSettingsTitle), "S_Common_OverridableSettings");
    public virtual string OverrideTitle => Text(nameof(OverrideTitle), "C_EnableOverride");
    public abstract string EnabledTitle { get; }
    public abstract string EnabledDescription { get; }
    public virtual string AnimationTitle => Text(nameof(AnimationTitle), "S_Default_Animation");
    public virtual string AnimationDescription => Text(nameof(AnimationDescription), "S_Default_Animation_D");
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
    public virtual string TestTitle => Text("Test notification", "S_Common_Test");
    public virtual string TestDescription => Text("Show a sample result using this notification channel", "S_Common_Test_D");
    public virtual string TestButtonText => Text("Show test", "C_TestNotification");

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    protected abstract NotificationChannelSettings SelectChannelSettings(NotificationSettingsConfig settings);

    private static IReadOnlyList<NotificationServiceOption> BuildNotificationServiceOptions()
    {
        // Display order is built-in / fullscreen / ClassIsland / both while the persisted values stay 0/3/1/2.
        NotificationServiceOption[] options =
        [
            new NotificationServiceOption(0, Text("SecRandom (floating)", "O_NotificationService_SecRandom")),
            new NotificationServiceOption(
                FullscreenNotificationServiceType,
                Text("Fullscreen", "O_NotificationService_Fullscreen")),
            new NotificationServiceOption(1, Text("ClassIsland", "O_NotificationService_ClassIsland")),
            new NotificationServiceOption(2, Text("Use both", "O_NotificationService_Both"))
        ];
        return IAppHost.TryGetService<FeatureRuntimeSnapshot>()?.IsEnabled(StartupFeatureCatalog.FullscreenNotifications) == false
            ? options.Where(option => option.Value != FullscreenNotificationServiceType).ToArray()
            : options;
    }

    private void ApplyNotificationServiceType(int value)
    {
        if (ChannelSettings.NotificationServiceType == value)
            return;

        // The base page's PropertyChanged handler persists the config, so a single write is all that is needed here.
        ChannelSettings.NotificationServiceType = value;
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNotificationServiceOption)));
    }

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

        // The language can change without the page being rebuilt, so the dropdown labels are re-read on every load.
        var refreshedOptions = BuildNotificationServiceOptions();
        if (!NotificationServiceOptions.SequenceEqual(refreshedOptions))
        {
            NotificationServiceOptions = refreshedOptions;
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NotificationServiceOptions)));
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNotificationServiceOption)));
        }
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotificationChannelSettings.Enabled)
            or nameof(NotificationChannelSettings.Animation))
            OverrideSettings?.OverrideBasicSettings = true;

        if (e.PropertyName == nameof(NotificationChannelSettings.NotificationServiceType))
        {
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UsesExternalNotificationServiceOnly)));
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNotificationServiceOption)));
        }
        ConfigHandler.Save();
    }

    internal async void HandleTestNotificationAsync(object? sender, RoutedEventArgs e)
    {
        try
        {
            var notificationService = IAppHost.GetService<NotificationService>();
            var settings = ViewModel.Config.NotificationSettings;
            if (ReferenceEquals(ChannelSettings, settings.Default))
            {
                var shownDefault = await notificationService.ShowTestNotificationAsync(
                    NotificationSettingsType.QuickDraw,
                    ChannelSettings);
                if (!shownDefault)
                    this.ShowWarningToast(Text("This notification channel is not available", "M_NotificationTestUnavailable"));
                return;
            }

            var type = ReferenceEquals(ChannelSettings, settings.RollCall)
                ? NotificationSettingsType.RollCall
                : ReferenceEquals(ChannelSettings, settings.Lottery)
                    ? NotificationSettingsType.Lottery
                    : NotificationSettingsType.QuickDraw;
            var shown = await notificationService.ShowTestNotificationAsync(type);
            if (!shown)
                this.ShowWarningToast(Text("This notification channel is not available", "M_NotificationTestUnavailable"));
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(Text("The notification test is not available", "M_NotificationTestUnavailable"), ex);
        }
    }
}

public sealed record NotificationServiceOption(int Value, string Label);
