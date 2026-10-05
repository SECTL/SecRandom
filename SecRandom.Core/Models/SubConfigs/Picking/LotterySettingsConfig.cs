using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers;

namespace SecRandom.Core.Models.SubConfigs.Picking;

public partial class LotterySettingsConfig : OverridableDrawSettings
{
    [ObservableProperty] private DrawMode _drawMode = DrawMode.NoRepeat;
    [ObservableProperty] private ClearRecordMode _clearRecord = ClearRecordMode.Restarted;
    [ObservableProperty] private int _halfRepeat = 1;

    [ObservableProperty] private LotteryDrawType _drawType = LotteryDrawType.Count;
    [ObservableProperty] private string _algorithmId = "builtin.inventory";
    [ObservableProperty] private string _defaultPool = string.Empty;
    [ObservableProperty] private LotteryShowRandomMode _lotteryShowRandom = LotteryShowRandomMode.PrizeIdPrizeBreakGroupHyphenMember;
    [ObservableProperty] private string _customLotteryShowRandomFormat = LotteryProcessDisplayFormatter.DefaultTemplate;
    [ObservableProperty] private bool _lotteryImage = false;
    [ObservableProperty] private StudentImagePositionMode _lotteryImagePosition = StudentImagePositionMode.Left;
    [ObservableProperty] private int _lotteryImageSize = DefaultImageSize;

    /// <summary>同 <see cref="ClampImageSize" />：奖品图片的边长同样只认范围内的值。</summary>
    partial void OnLotteryImageSizeChanged(int value)
    {
        var clamped = ClampImageSize(value);
        if (clamped != value)
            LotteryImageSize = clamped;
    }
}
