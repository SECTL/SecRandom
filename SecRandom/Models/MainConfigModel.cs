using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Models.SubConfigs;

namespace SecRandom.Models;

public partial class MainConfigModel : ConfigBase
{
    [JsonIgnore]
    public override string ConfigFilePath => Utils.GetFilePath("config", "settings.json");

    [ObservableProperty] private FloatPositionConfig _floatPosition = new();
    
    [ObservableProperty] private BasicSettingsConfig _basicSettings = new();
}
