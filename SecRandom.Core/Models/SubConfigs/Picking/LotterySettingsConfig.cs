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

    /// <summary>
    ///     抽奖页选择器的初始记录组（空串 = 默认记录组，即只算未分组的记录）。
    /// </summary>
    /// <remarks>
    ///     与点名页同义：页面打开时选择器先落到这里，用户当场换组仍然不写配置。抽奖的权重来自
    ///     学生侧历史，所以组名同样按 <see cref="SecRandom.Core.Enums.HistoryGroupKind.RollCall" />
    ///     登记，与点名、闪抽共用同一批组；组已失效时回落到默认记录组。
    /// </remarks>
    [ObservableProperty] private string _historyGroup = string.Empty;

    /// <summary>同 <see cref="ClampImageSize" />：奖品图片的边长同样只认范围内的值。</summary>
    partial void OnLotteryImageSizeChanged(int value)
    {
        var clamped = ClampImageSize(value);
        if (clamped != value)
            LotteryImageSize = clamped;
    }
}
