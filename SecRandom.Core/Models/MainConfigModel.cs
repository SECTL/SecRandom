using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
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
    // 抽取设置
    [ObservableProperty] private RollCallSettingsConfig _rollCallSettings = new();
    [ObservableProperty] private QuickDrawSettingsConfig _quickDrawSettings = new();
    [ObservableProperty] private LotterySettingsConfig _lotterySettings = new();
    [ObservableProperty] private FaceDetectorSettingsConfig _faceDetectorSettingsConfig = new();
    // ...
    // 更多设置->公平抽取设置
    [ObservableProperty] private FairDrawSettingsConfig _fairDrawSettings = new();
    // ...
}
