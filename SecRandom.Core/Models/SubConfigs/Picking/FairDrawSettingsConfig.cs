using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs.Picking;

public partial class FairDrawSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _fairDraw = true;
    [ObservableProperty] private bool _fairDrawGroup = true;
    [ObservableProperty] private bool _fairDrawGender = true;
    [ObservableProperty] private bool _fairDrawTime = true;
    [ObservableProperty] private FrequencyFunctionMode _frequencyFunction = FrequencyFunctionMode.SquareRoot;
    [ObservableProperty] private double _frequencyWeight = 1.0;

    /// <summary>
    ///     Selects how a fair draw treats students ahead of the class average. <see cref="FairDrawMode.UniformFirst" />
    ///     keeps the hard average gate; <see cref="FairDrawMode.RandomFirst" /> keeps the full roster in the pool and
    ///     gives an above-threshold student weight 0 instead. Only fair draws read this value; a random draw keeps
    ///     its equal-probability pool.
    /// </summary>
    [ObservableProperty] private FairDrawMode _mode = FairDrawMode.UniformFirst;

    // Average-gap protection is the mandatory gate of the UniformFirst fair mode; RandomFirst
    // replaces it with a zero-probability weight instead. Keep these compatibility properties for
    // source callers, but do not persist or expose them as user settings.
    [JsonIgnore]
    public bool EnableAvgGapProtection
    {
        get => true;
        set { }
    }

    [JsonIgnore]
    public int GapThreshold
    {
        get => 1;
        set { }
    }
    [ObservableProperty] private int _minPoolSize = 5;
    [ObservableProperty] private bool _shieldEnabled = false;
    [ObservableProperty] private int _shieldTime = 0;
    [ObservableProperty] private ShieldTimeUnit _shieldTimeUnit = ShieldTimeUnit.Minutes;
    [ObservableProperty] private bool _coldStartEnabled = true;
    [ObservableProperty] private int _coldStartRounds = 10;
    [ObservableProperty] private double _baseWeight = 1.0;
    [ObservableProperty] private double _minWeight = 0.5;
    [ObservableProperty] private double _maxWeight = 5.0;
    [ObservableProperty] private double _groupWeight = 0.8;
    [ObservableProperty] private double _genderWeight = 0.8;
    [ObservableProperty] private double _timeWeight = 0.5;
}
