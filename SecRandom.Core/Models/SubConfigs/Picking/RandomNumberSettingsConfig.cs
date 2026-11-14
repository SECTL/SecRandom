using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Profiles;

namespace SecRandom.Core.Models.SubConfigs.Picking;

/// <summary>
///     随机数抽取（原“不使用名单”模式）的独立设置。
/// </summary>
/// <remarks>
///     这些值只是两个主页数字模式的初始值，不会在用户当场修改范围后回写页面。范围合法性仍由
///     <see cref="SecRandom.Core.Services.Draw.NumberTickets.IsValidRange" /> 与
///     <see cref="SecRandom.Core.Services.Profiles.RosterSelection.MaxNumberRangeSpan" /> 共同约束；
///     页面在应用默认值时会再次夹取，导入的非法配置不会让抽取入口失效。
/// </remarks>
public partial class RandomNumberSettingsConfig : ObservableObject
{
    /// <summary>点名页数字范围的起始值。</summary>
    [ObservableProperty] private int _rollCallRangeStart = 1;

    /// <summary>点名页数字范围的结束值（含）。</summary>
    [ObservableProperty] private int _rollCallRangeEnd = 50;

    /// <summary>抽奖页数字范围的起始值。</summary>
    [ObservableProperty] private int _lotteryRangeStart = 1;

    /// <summary>抽奖页数字范围的结束值（含）。</summary>
    [ObservableProperty] private int _lotteryRangeEnd = 50;

    /// <summary>重置按钮的行为；默认只清临时剩余数字。</summary>
    [ObservableProperty]
    private RandomNumberResetBehavior _resetBehavior = RandomNumberResetBehavior.TemporaryOnly;

    /// <summary>
    ///     把两组范围夹回可抽取区间。返回是否真的改过，调用方据此决定是否落盘。
    ///     导入的配置可能来自旧版本或被手工编辑，因此设置页和主页都要经过这里。
    /// </summary>
    public bool Normalize()
    {
        var changed = false;

        var rollCallStart = Math.Max(0, RollCallRangeStart);
        if (rollCallStart != RollCallRangeStart)
        {
            RollCallRangeStart = rollCallStart;
            changed = true;
        }

        var rollCallEnd = RosterSelection.ClampEnd(RollCallRangeStart, RollCallRangeEnd);
        if (rollCallEnd != RollCallRangeEnd)
        {
            RollCallRangeEnd = rollCallEnd;
            changed = true;
        }

        var lotteryStart = Math.Max(0, LotteryRangeStart);
        if (lotteryStart != LotteryRangeStart)
        {
            LotteryRangeStart = lotteryStart;
            changed = true;
        }

        var lotteryEnd = RosterSelection.ClampEnd(LotteryRangeStart, LotteryRangeEnd);
        if (lotteryEnd != LotteryRangeEnd)
        {
            LotteryRangeEnd = lotteryEnd;
            changed = true;
        }

        return changed;
    }
}