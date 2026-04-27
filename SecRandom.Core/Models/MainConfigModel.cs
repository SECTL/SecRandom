using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models.SubConfigs;

namespace SecRandom.Core.Models;

public partial class MainConfigModel : ConfigBase
{
    [JsonIgnore]
    public override string ConfigFilePath => Utils.GetFilePath("config", "settings.json");

    [ObservableProperty] private FloatPositionConfig _floatPosition = new();
    
    [ObservableProperty] private BasicSettingsConfig _basicSettings = new();
    [ObservableProperty] private FairDrawSettingsConfig _fairDrawSettings = new();
}
