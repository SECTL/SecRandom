using Avalonia.Controls;
using Avalonia.Media;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.Config;
using SecRandom.Mobile.Controls;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

/// <summary>
/// 通用设置页：在线状态上报三档、Sentry 遥测开关、语言三档。
/// 语言绑定桌面同一字段 <c>MainConfigModel.General.Basic.Language</c>；
/// 切换后立即应用 culture 并重建 <see cref="MobileRootView"/> 可视树，无需重启。
/// </summary>
public sealed partial class MobileGeneralSettingsPage : MobileSettingsPageBase
{
    private readonly MainConfigHandler _configHandler;
    private readonly IMobileRootViewReloader _rootViewReloader;

    public MobileGeneralSettingsPage(
        MainConfigHandler configHandler,
        IMobileRootViewReloader rootViewReloader,
        IMobileNavigator navigator,
        IMobileCapabilities capabilities)
        : base(navigator, capabilities)
    {
        _configHandler = configHandler;
        _rootViewReloader = rootViewReloader;
        InitializeComponent();
        Render();
    }

    private void Render()
    {
        var privacy = _configHandler.Data.General.PrivacySettings;
        var basic = _configHandler.Data.General.Basic;
        void Save(Action mutate)
        {
            mutate();
            _configHandler.Save();
            Render();
        }

        // 语言切换：写配置后立即应用 culture，并重建根视图可视树（回到默认抽取路由），
        // 不再走 Render() —— 本页会随旧根视图一起被销毁。
        void SwitchLanguage(LanguageMode mode)
        {
            basic.Language = mode;
            _configHandler.Save();
            MobileApplicationServices.ApplyCulture(new System.Globalization.CultureInfo(mode switch
            {
                LanguageMode.ChineseSimplified => "zh-Hans",
                LanguageMode.English => "en-US",
                LanguageMode.Japanese => "ja-JP",
                _ => "zh-Hans"
            }));
            _ = _rootViewReloader.ReloadAsync();
        }

        RenderPage([
            CreateCaption(LR.M_GeneralMobileOnly),
            new MobileSectionHeader(LR.S_OnlineStatus, FluentIcons.CloudArrowUpFilled),
            MobileSettingRow.Choice(LR.O_Off, privacy.OnlineStatusMode == OnlineStatusMode.Off,
                () => Save(() => privacy.OnlineStatusMode = OnlineStatusMode.Off)),
            MobileSettingRow.Choice(LR.O_Anonymous, privacy.OnlineStatusMode == OnlineStatusMode.Anonymous,
                () => Save(() => privacy.OnlineStatusMode = OnlineStatusMode.Anonymous)),
            MobileSettingRow.Choice(LR.O_Full, privacy.OnlineStatusMode == OnlineStatusMode.Full,
                () => Save(() => privacy.OnlineStatusMode = OnlineStatusMode.Full)),
            new MobileSectionHeader(LR.S_Telemetry, FluentIcons.ShieldCheckmarkFilled),
            // Sentry 开关即开即关：TelemetryRuntimeService 已订阅 PrivacySettings.PropertyChanged。
            MobileSettingRow.Toggle(LR.S_SentryTelemetry, LR.S_SentryTelemetry_D,
                privacy.SentryTelemetryEnabled,
                v => Save(() => privacy.SentryTelemetryEnabled = v)),
            new MobileSectionHeader(LR.S_Language, FluentIcons.LocalLanguageFilled),
            // 语言名刻意保留各自语言的自称（endonym），不随界面语言翻译。
            MobileSettingRow.Choice("中文", basic.Language == LanguageMode.ChineseSimplified,
                () => SwitchLanguage(LanguageMode.ChineseSimplified)),
            MobileSettingRow.Choice("English", basic.Language == LanguageMode.English,
                () => SwitchLanguage(LanguageMode.English)),
            MobileSettingRow.Choice("日本語", basic.Language == LanguageMode.Japanese,
                () => SwitchLanguage(LanguageMode.Japanese)),
            CreateCaption(LR.S_Language_D)
        ]);
    }

    private static TextBlock CreateCaption(string text)
    {
        var caption = new TextBlock
        {
            Text = text,
            FontSize = MobileResources.FindDouble("MobileFontSizeCaption", 12),
            TextWrapping = TextWrapping.Wrap
        };
        MobileResources.BindBrush(caption, TextBlock.ForegroundProperty, MobileResources.Keys.MutedText);
        return caption;
    }
}
