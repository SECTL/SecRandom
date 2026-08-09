namespace SecRandom.Core.Enums.Configs;

public enum LotteryShowRandomMode
{
    /// <summary>
    ///     序号 奖品[换行]分组[短横杠]名称
    /// </summary>
    PrizeIdPrizeBreakGroupHyphenMember,

    /// <summary>
    ///     奖品[换行]分组[短横杠]名称
    /// </summary>
    PrizeBreakGroupHyphenMember,

    /// <summary>
    ///     奖品[短横杠]名称
    /// </summary>
    PrizeHyphenMember,

    /// <summary>
    ///     奖品[短横杠]分组
    /// </summary>
    PrizeHyphenGroup,

    /// <summary>
    ///     自定义格式
    /// </summary>
    Custom
}
