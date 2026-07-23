using Avalonia.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.Config;
using SecRandom.Mobile;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.Views.Mobile.Settings;

/// <summary>
/// 通用设置页：在线状态上报三档、Sentry 遥测开关、语言三档。
/// 语言绑定桌面同一字段 <c>MainConfigModel.General.Basic.Language</c>；
/// 切换后立即应用 culture 并重建 <see cref="MobileViewHost"/> 可视树，无需重启。
/// </summary>
[PageInfo(MobilePageIds.General, FluentIcons.SettingsFilled, "settings.mobile.preferences")]
public sealed partial class MobileGeneralSettingsPage : MobileSettingsPageBase
{
    private readonly MainConfigHandler _configHandler;
    private readonly IMobileRootViewReloader _rootViewReloader;

    public MobileGeneralSettingsPage(
        MainConfigHandler configHandler,
        IMobileRootViewReloader rootViewReloader,
        IMobileCapabilities capabilities)
        : base(capabilities)
    {
        _configHandler = configHandler;
        _rootViewReloader = rootViewReloader;
        InitializeComponent();
        SelectItem(OnlineStatusSelector, PrivacySettings.OnlineStatusMode);
        SentryTelemetryToggle.IsChecked = PrivacySettings.SentryTelemetryEnabled;
        SelectItem(LanguageSelector, BasicSettings.Language);
    }

    private global::SecRandom.Core.Models.SubConfigs.General.PrivacySettingsConfig PrivacySettings =>
        _configHandler.Data.General.PrivacySettings;

    private global::SecRandom.Core.Models.SubConfigs.General.BasicSettingsConfig BasicSettings =>
        _configHandler.Data.General.Basic;

    private void OnlineStatusSelector_OnSelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (GetSelectedTag<OnlineStatusMode>(OnlineStatusSelector) is { } mode && mode != PrivacySettings.OnlineStatusMode)
        {
            PrivacySettings.OnlineStatusMode = mode;
            _configHandler.Save();
        }
    }

    private void SentryTelemetryToggle_OnIsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SentryTelemetryToggle.IsChecked is bool enabled && enabled != PrivacySettings.SentryTelemetryEnabled)
        {
            PrivacySettings.SentryTelemetryEnabled = enabled;
            _configHandler.Save();
        }
    }

    private async void LanguageSelector_OnSelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (GetSelectedTag<LanguageMode>(LanguageSelector) is not { } mode || mode == BasicSettings.Language)
            return;

        BasicSettings.Language = mode;
        _configHandler.Save();
        global::SecRandom.App.ApplyMobileCulture(new System.Globalization.CultureInfo(mode switch
        {
            LanguageMode.ChineseSimplified => "zh-Hans",
            LanguageMode.English => "en-US",
            LanguageMode.Japanese => "ja-JP",
            _ => "zh-Hans"
        }));
        await _rootViewReloader.ReloadAsync();
    }

    private static void SelectItem<T>(ComboBox selector, T value) where T : struct, Enum
    {
        selector.SelectedItem = selector.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is T tag && EqualityComparer<T>.Default.Equals(tag, value));
    }

    private static T? GetSelectedTag<T>(ComboBox selector) where T : struct, Enum =>
        (selector.SelectedItem as ComboBoxItem)?.Tag as T?;
}
