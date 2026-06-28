using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs.Picking;

public partial class LotterySettingsConfig : OverridableDrawSettings
{
    [ObservableProperty] private DrawMode _drawMode = DrawMode.NoRepeat;
    [ObservableProperty] private ClearRecordMode _clearRecord = ClearRecordMode.Restarted;
    [ObservableProperty] private int _halfRepeat = 1;

    [ObservableProperty] private LotteryDrawType _drawType = LotteryDrawType.Count;
    [ObservableProperty] private string _defaultPool = string.Empty;
    [ObservableProperty] private LotteryShowRandomMode _lotteryShowRandom = LotteryShowRandomMode.PrizeBreakGroupHyphenName;
    [ObservableProperty] private bool _lotteryImage = false;
    [ObservableProperty] private StudentImagePositionMode _lotteryImagePosition = StudentImagePositionMode.Left;
}
