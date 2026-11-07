using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums;
using SecRandom.Core.Helpers;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.SimpleMode;
using SecRandom.Shared;
using SecRandom.Shared.Abstraction;
using AppearanceSettingsConfig = SecRandom.Core.Models.SubConfigs.Personalized.AppearanceSettingsConfig;
using BackupConfig = SecRandom.Core.Models.SubConfigs.General.BackupConfig;
using BasicSettingsConfig = SecRandom.Core.Models.SubConfigs.General.BasicSettingsConfig;
using GeneralSettingsConfig = SecRandom.Core.Models.SubConfigs.General.GeneralSettingsConfig;
using HistoryGroupSettingsConfig = SecRandom.Core.Models.SubConfigs.General.HistoryGroupSettingsConfig;
using DefaultDrawSettingsConfig = SecRandom.Core.Models.SubConfigs.Picking.DefaultDrawSettingsConfig;
using DrawSettingsConfigBase = SecRandom.Core.Models.SubConfigs.Picking.DrawSettingsConfigBase;
using FairDrawSettingsConfig = SecRandom.Core.Models.SubConfigs.Picking.FairDrawSettingsConfig;
using LotterySettingsConfig = SecRandom.Core.Models.SubConfigs.Picking.LotterySettingsConfig;
using OverridableDrawSettings = SecRandom.Core.Models.SubConfigs.Picking.OverridableDrawSettings;
using QuickDrawSettingsConfig = SecRandom.Core.Models.SubConfigs.Picking.QuickDrawSettingsConfig;
using RollCallSettingsConfig = SecRandom.Core.Models.SubConfigs.Picking.RollCallSettingsConfig;

namespace SecRandom.Core.Models;

public partial class MainConfigModel : ConfigBase, IJsonOnDeserialized
{
    [ObservableProperty] private FloatPositionConfig _floatPosition = new();

    // 通用
    [ObservableProperty] private GeneralSettingsConfig _general = new();

    // 个性化
    [ObservableProperty] private AppearanceSettingsConfig _appearance = new();

    // 抽取设置
    [ObservableProperty] private FairDrawSettingsConfig _fairDrawSettings = new();
    [ObservableProperty] private DefaultDrawSettingsConfig _defaultDrawSettings = new();
    [ObservableProperty] private RollCallSettingsConfig _rollCallSettings = new();
    [ObservableProperty] private QuickDrawSettingsConfig _quickDrawSettings = new();
    [ObservableProperty] private LotterySettingsConfig _lotterySettings = new();

    [ObservableProperty] private FloatingWindowSettingsConfig _floatingWindowSettings = new();
    private NotificationSettingsConfig _notificationSettings = new();

    [AllowNull]
    public NotificationSettingsConfig NotificationSettings
    {
        get => _notificationSettings;
        set => SetProperty(ref _notificationSettings, value ?? new NotificationSettingsConfig());
    }
    [ObservableProperty] private LinkageSettingsConfig _linkageSettings = new();
    [ObservableProperty] private VoiceSettingsConfig _voiceSettings = new();
    [ObservableProperty] private HistoryManagementSettingsConfig _historyManagementSettings = new();
    [ObservableProperty] private UpdateSettingsConfig _updateSettings = new();
    [ObservableProperty] private MoreSettingsConfig _moreSettings = new();

    // 计时器
    [ObservableProperty] private TimerSettingsConfig _timerSettings = new();
    [ObservableProperty] private List<int> _recentTimerPresetSeconds = [];

    [JsonPropertyName("moreSettings")]
    public MoreSettingsConfig LegacyMoreSettingsOnLoad
    {
        set => MoreSettings = value;
    }

    [JsonIgnore] public override string ConfigFilePath => Utils.GetFilePath("config", "settings.json");

    /// <summary>
    ///     简洁模式开关。开启后隐藏掉的功能在运行时按默认配置读取，用户已存设置不会被改写。
    /// </summary>
    [JsonIgnore]
    public bool SimpleModeEnabled => General.SimpleMode.Enabled;

    // 简洁模式：隐藏功能在运行时按默认配置读取。这里只提供读取入口，绝不改写用户存下的真实设置。
    // 需要区分「主流程状态」（例如上次选中的名单/奖池）时，调用方仍然读取真实配置的那一个字段。
    [JsonIgnore]
    public MoreSettingsConfig EffectiveMoreSettings => SimpleModeEnabled ? SimpleModeDefaults.MoreSettings : MoreSettings;

    [JsonIgnore]
    public DefaultDrawSettingsConfig EffectiveDefaultDrawSettings =>
        SimpleModeEnabled ? SimpleModeDefaults.DefaultDrawSettings : DefaultDrawSettings;

    [JsonIgnore]
    public RollCallSettingsConfig EffectiveRollCallSettings =>
        SimpleModeEnabled ? SimpleModeDefaults.RollCallSettings : RollCallSettings;

    [JsonIgnore]
    public QuickDrawSettingsConfig EffectiveQuickDrawSettings =>
        SimpleModeEnabled ? SimpleModeDefaults.QuickDrawSettings : QuickDrawSettings;

    [JsonIgnore]
    public LotterySettingsConfig EffectiveLotterySettings =>
        SimpleModeEnabled ? SimpleModeDefaults.LotterySettings : LotterySettings;

    [JsonIgnore]
    public FairDrawSettingsConfig EffectiveFairDrawSettings =>
        SimpleModeEnabled ? SimpleModeDefaults.FairDrawSettings : FairDrawSettings;

    [JsonIgnore]
    public FloatingWindowSettingsConfig EffectiveFloatingWindowSettings =>
        SimpleModeEnabled ? SimpleModeDefaults.FloatingWindowSettings : FloatingWindowSettings;

    [JsonIgnore]
    public LinkageSettingsConfig EffectiveLinkageSettings =>
        SimpleModeEnabled ? SimpleModeDefaults.LinkageSettings : LinkageSettings;

    [JsonIgnore]
    public VoiceSettingsConfig EffectiveVoiceSettings =>
        SimpleModeEnabled ? SimpleModeDefaults.VoiceSettings : VoiceSettings;

    [JsonIgnore]
    public NotificationSettingsConfig EffectiveNotificationSettings =>
        SimpleModeEnabled ? SimpleModeDefaults.NotificationSettings : NotificationSettings;

    [JsonIgnore]
    public HistoryManagementSettingsConfig EffectiveHistoryManagementSettings =>
        SimpleModeEnabled ? SimpleModeDefaults.HistoryManagementSettings : HistoryManagementSettings;

    [JsonIgnore]
    public TimerSettingsConfig EffectiveTimerSettings =>
        SimpleModeEnabled ? SimpleModeDefaults.TimerSettings : TimerSettings;

    [JsonIgnore]
    public HistoryGroupSettingsConfig EffectiveHistoryGroups =>
        SimpleModeEnabled ? SimpleModeDefaults.HistoryGroups : General.HistoryGroups;

    [JsonIgnore]
    public BasicSettingsConfig Basic
    {
        get => General.Basic;
        set => General.Basic = value;
    }

    [JsonIgnore]
    public BackupConfig Backup
    {
        get => General.Backup;
        set => General.Backup = value;
    }

    [JsonPropertyName("basic")]
    public BasicSettingsConfig LegacyBasicOnLoad
    {
        set => General.ApplyLegacyBasic(value);
    }

    [JsonPropertyName("backup")]
    public BackupConfig LegacyBackupOnLoad
    {
        set => General.ApplyLegacyBackup(value);
    }

    /// <summary>
    ///     旧版把安全设置明文放在 settings.json 里。它现在住在 <c>data/config/security/settings.json</c>
    ///     的加密信封中，这里只把旧文件里的那一段原样收下来，供首次迁移取走一次；
    ///     该属性只写不读，因此下一次保存 settings.json 时明文副本就会被去掉。
    /// </summary>
    [JsonPropertyName("security_settings")]
    public SecuritySettingsConfig LegacySecuritySettingsOnLoad
    {
        set => PendingLegacySecuritySettings = value;
    }

    /// <summary>等待迁移的旧版安全设置；没有旧文件时为 null。</summary>
    [JsonIgnore]
    public SecuritySettingsConfig? PendingLegacySecuritySettings { get; private set; }

    /// <summary>
    ///     取走迁移用的旧版安全设置，只返回一次：迁移只允许发生在加密文件不存在时，
    ///     取走后就不会再被后续的重新加载重新捡起来。
    /// </summary>
    public SecuritySettingsConfig? ConsumeLegacySecuritySettings()
    {
        var pending = PendingLegacySecuritySettings;
        PendingLegacySecuritySettings = null;
        return pending;
    }

    public DrawSettingsConfigBase GetOverrideDrawSettings(
        DrawSettingsType drawSettingsType, OverridableDrawSettingsType settingsType)
    {
        // 简洁模式：抽取页面的显示/动画/音乐/语音等设置按默认值读取，隐藏的设置页不生效。
        if (SimpleModeEnabled)
            return SimpleModeDefaults.GetOverrideDrawSettings(drawSettingsType, settingsType);

        OverridableDrawSettings settings = drawSettingsType switch
        {
            DrawSettingsType.RollCall => RollCallSettings,
            DrawSettingsType.QuickDraw => QuickDrawSettings,
            DrawSettingsType.Lottery => LotterySettings,
            _ => throw new ArgumentOutOfRangeException(nameof(drawSettingsType), drawSettingsType, null)
        };

        return settingsType switch
        {
            OverridableDrawSettingsType.Display => settings.OverrideDisplaySettings ? settings : DefaultDrawSettings,
            OverridableDrawSettingsType.Animation =>
                settings.OverrideAnimationSettings ? settings : DefaultDrawSettings,
            OverridableDrawSettingsType.Color => settings.OverrideColorSettings ? settings : DefaultDrawSettings,
            OverridableDrawSettingsType.StudentImage => settings.OverrideStudentImageSettings
                ? settings
                : DefaultDrawSettings,
            OverridableDrawSettingsType.Reminder => settings.OverrideReminderSettings ? settings : DefaultDrawSettings,
            OverridableDrawSettingsType.Music => settings.OverrideMusicSettings ? settings : DefaultDrawSettings,
            OverridableDrawSettingsType.VoiceAnnouncement => settings.OverrideVoiceAnnouncementSettings
                ? settings
                : DefaultDrawSettings,
            _ => throw new ArgumentOutOfRangeException(nameof(settingsType), settingsType, null)
        };
    }

    public string GetLotteryProcessDisplayTemplate()
    {
        if (SimpleModeEnabled)
            return SimpleModeDefaults.Model.GetLotteryProcessDisplayTemplate();

        return LotterySettings.OverrideDisplaySettings
            ? LotteryProcessDisplayFormatter.ResolveTemplate(
                LotterySettings.LotteryShowRandom,
                LotterySettings.CustomLotteryShowRandomFormat)
            : LotteryProcessDisplayFormatter.DefaultTemplate;
    }

    public NotificationChannelSettings GetNotificationChannelSettings(NotificationSettingsType notificationSettingsType)
    {
        if (SimpleModeEnabled)
            return SimpleModeDefaults.Model.GetNotificationChannelSettings(notificationSettingsType);

        return notificationSettingsType switch
        {
            NotificationSettingsType.RollCall => NotificationSettings.RollCall,
            NotificationSettingsType.QuickDraw => NotificationSettings.QuickDraw,
            NotificationSettingsType.Lottery => NotificationSettings.Lottery,
            _ => throw new ArgumentOutOfRangeException(
                nameof(notificationSettingsType), notificationSettingsType, null)
        };
    }

    public NotificationChannelSettings GetOverrideNotificationSettings(
        NotificationSettingsType notificationSettingsType,
        OverridableNotificationSettingsType settingsType)
    {
        if (SimpleModeEnabled)
            return SimpleModeDefaults.Model.GetOverrideNotificationSettings(notificationSettingsType, settingsType);

        var settings = (OverridableNotificationChannelSettings)GetNotificationChannelSettings(notificationSettingsType);
        return settingsType switch
        {
            OverridableNotificationSettingsType.Basic => settings,
            OverridableNotificationSettingsType.NotificationWindow => settings.OverrideNotificationWindowSettings
                ? settings
                : NotificationSettings.Default,
            OverridableNotificationSettingsType.Service => settings.OverrideServiceSettings
                ? settings
                : NotificationSettings.Default,
            _ => throw new ArgumentOutOfRangeException(nameof(settingsType), settingsType, null)
        };
    }

    void IJsonOnDeserialized.OnDeserialized()
    {
        ApplyLegacyAnimationMusicLoop();
    }

    private void ApplyLegacyAnimationMusicLoop()
    {
        var legacyMusicLoop = MoreSettings.ConsumeLegacyBackgroundMusicLoop();
        if (legacyMusicLoop is not { } animationMusicLoop)
            return;

        ApplyLegacyAnimationMusicLoop(DefaultDrawSettings, animationMusicLoop);
        ApplyLegacyAnimationMusicLoop(RollCallSettings, animationMusicLoop);
        ApplyLegacyAnimationMusicLoop(QuickDrawSettings, animationMusicLoop);
        ApplyLegacyAnimationMusicLoop(LotterySettings, animationMusicLoop);
    }

    private static void ApplyLegacyAnimationMusicLoop(DrawSettingsConfigBase settings, bool value)
    {
        if (!settings.HasAnimationMusicLoop)
            settings.AnimationMusicLoop = value;
    }
}
