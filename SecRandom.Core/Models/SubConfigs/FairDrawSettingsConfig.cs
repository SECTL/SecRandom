using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class FairDrawSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _fairDraw = true;  // 总抽取次数
    [ObservableProperty] private bool _fairDrawGroup = true;  // 小组
    [ObservableProperty] private bool _fairDrawGender = true;  // 性别
    [ObservableProperty] private bool _fairDrawTime = false;  // 时间

    [ObservableProperty] private FrequencyFunctionMode _frequencyFunction = FrequencyFunctionMode.SquareRoot;
    [ObservableProperty] private double _frequencyWeight = 1.0;
    [ObservableProperty] private bool _enableAvgGapProtection = false;
    [ObservableProperty] private int _gapThreshold = 1;
    [ObservableProperty] private int _minPoolSize = 5;

    [ObservableProperty] private bool _shieldEnabled = false;
    [ObservableProperty] private double _shieldTime = 5.00;
    [ObservableProperty] private ShieldTimeUnit _shieldTimeUnit = ShieldTimeUnit.Seconds;
    
    [ObservableProperty] private bool _coolStartEnabled = true;
    [ObservableProperty] private int _coolStartRounds = 10;
    
	[ObservableProperty] private double _baseWeight = 1.0;
	[ObservableProperty] private double _minWeight = 0.5;
	[ObservableProperty] private double _maxWeight = 5.0;
	[ObservableProperty] private double _groupWeight = 0.8;
	[ObservableProperty] private double _genderWeight = 0.8;
	[ObservableProperty] private double _timeWeight = 0.5;

	[ObservableProperty] private bool _enableBehindSceneSettings = false;
}