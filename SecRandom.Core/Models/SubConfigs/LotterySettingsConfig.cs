using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class LotterySettingsConfig : OverridableDrawSettings
{
    [ObservableProperty] private ClearRecordMode _clearRecord = ClearRecordMode.Restarted;
    [ObservableProperty] private string _defaultPool = string.Empty;
    [ObservableProperty] private DrawMode _drawMode = DrawMode.NoRepeat;

    [ObservableProperty] private LotteryDrawType _drawType = LotteryDrawType.Pan;
    [ObservableProperty] private int _halfRepeat = 1;

    [ObservableProperty]
    private LotteryShowRandomMode _lotteryShowRandom = LotteryShowRandomMode.PrizeBreakGroupHyphenName;
}