using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class LotterySettingsConfig : OverridableDrawSettings
{
    [ObservableProperty] private LotteryDrawType _drawType = LotteryDrawType.Pan;
    [ObservableProperty] private string _defaultPool = string.Empty;
    [ObservableProperty] private LotteryShowRandomMode _lotteryShowRandom = LotteryShowRandomMode.PrizeBreakGroupHyphenName;
}