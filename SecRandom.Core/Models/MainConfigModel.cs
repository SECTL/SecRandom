using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Shared;
using SecRandom.Shared.Abstraction;
using AppearanceSettingsConfig = SecRandom.Core.Models.SubConfigs.Personalized.AppearanceSettingsConfig;
using BackupConfig = SecRandom.Core.Models.SubConfigs.General.BackupConfig;
using BasicSettingsConfig = SecRandom.Core.Models.SubConfigs.General.BasicSettingsConfig;
using GeneralSettingsConfig = SecRandom.Core.Models.SubConfigs.General.GeneralSettingsConfig;
using DefaultDrawSettingsConfig = SecRandom.Core.Models.SubConfigs.Picking.DefaultDrawSettingsConfig;
using DrawSettingsConfigBase = SecRandom.Core.Models.SubConfigs.Picking.DrawSettingsConfigBase;
using FaceDetectorSettingsConfig = SecRandom.Core.Models.SubConfigs.Picking.FaceDetectorSettingsConfig;
using FairDrawSettingsConfig = SecRandom.Core.Models.SubConfigs.Picking.FairDrawSettingsConfig;
using LotterySettingsConfig = SecRandom.Core.Models.SubConfigs.Picking.LotterySettingsConfig;
using OverridableDrawSettings = SecRandom.Core.Models.SubConfigs.Picking.OverridableDrawSettings;
using QuickDrawSettingsConfig = SecRandom.Core.Models.SubConfigs.Picking.QuickDrawSettingsConfig;
using RollCallSettingsConfig = SecRandom.Core.Models.SubConfigs.Picking.RollCallSettingsConfig;

namespace SecRandom.Core.Models;

public partial class MainConfigModel : ConfigBase
{
    [ObservableProperty] private FloatPositionConfig _floatPosition = new();

    // 通用
    [ObservableProperty] private GeneralSettingsConfig _general = new();

    // 个性化
    [ObservableProperty] private AppearanceSettingsConfig _appearance = new();
    [ObservableProperty] private ThemeManagementSettingsConfig _themeManagementSettings = new();

    // 抽取设置
    [ObservableProperty] private FairDrawSettingsConfig _fairDrawSettings = new();
    [ObservableProperty] private DefaultDrawSettingsConfig _defaultDrawSettings = new();
    [ObservableProperty] private RollCallSettingsConfig _rollCallSettings = new();
    [ObservableProperty] private QuickDrawSettingsConfig _quickDrawSettings = new();
    [ObservableProperty] private LotterySettingsConfig _lotterySettings = new();
    [ObservableProperty] private FaceDetectorSettingsConfig _faceDetectorSettings = new();

    [ObservableProperty] private FloatingWindowSettingsConfig _floatingWindowSettings = new();
    [ObservableProperty] private NotificationSettingsConfig _notificationSettings = new();
    [ObservableProperty] private SecuritySettingsConfig _securitySettings = new();
    [ObservableProperty] private LinkageSettingsConfig _linkageSettings = new();
    [ObservableProperty] private VoiceSettingsConfig _voiceSettings = new();
    [ObservableProperty] private HistoryManagementSettingsConfig _historyManagementSettings = new();
    [ObservableProperty] private UpdateSettingsConfig _updateSettings = new();
    [ObservableProperty] private MoreSettingsConfig _moreSettings = new();

    [JsonIgnore] public override string ConfigFilePath => Utils.GetFilePath("config", "settings.json");

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

    public DrawSettingsConfigBase GetOverrideDrawSettings(
        DrawSettingsType drawSettingsType, OverridableDrawSettingsType settingsType)
    {
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
            _ => throw new ArgumentOutOfRangeException(nameof(settingsType), settingsType, null)
        };
    }
}
