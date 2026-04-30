using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Shared;
using SecRandom.Shared.Abstraction;

namespace SecRandom.Core.Models;

public partial class MainConfigModel : ConfigBase
{
    [JsonIgnore]
    public override string ConfigFilePath => Utils.GetFilePath("config", "settings.json");

    [ObservableProperty] private FloatPositionConfig _floatPosition = new();
    
    // 基本设置
    [ObservableProperty] private BasicSettingsConfig _basicSettings = new();
    [ObservableProperty] private BackupConfig _backup = new();
    // 抽取设置
    [ObservableProperty] private DefaultDrawSettingsConfig _defaultDrawSettings = new();
    [ObservableProperty] private RollCallSettingsConfig _rollCallSettings = new();
    [ObservableProperty] private QuickDrawSettingsConfig _quickDrawSettings = new();
    [ObservableProperty] private LotterySettingsConfig _lotterySettings = new();
    [ObservableProperty] private FaceDetectorSettingsConfig _faceDetectorSettingsConfig = new();
    // ...
    // 更多设置->公平抽取设置
    [ObservableProperty] private FairDrawSettingsConfig _fairDrawSettings = new();
    // ...

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
            OverridableDrawSettingsType.Animation => settings.OverrideAnimationSettings ? settings : DefaultDrawSettings,
            OverridableDrawSettingsType.Color => settings.OverrideColorSettings ? settings : DefaultDrawSettings,
            OverridableDrawSettingsType.StudentImage => settings.OverrideStudentImageSettings ? settings : DefaultDrawSettings,
            OverridableDrawSettingsType.Music => settings.OverrideMusicSettings ? settings : DefaultDrawSettings,
            _ => throw new ArgumentOutOfRangeException(nameof(settingsType), settingsType, null)
        };
    }
}
