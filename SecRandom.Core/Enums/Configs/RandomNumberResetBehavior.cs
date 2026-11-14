namespace SecRandom.Core.Enums.Configs;

/// <summary>
///     随机数抽取页“重置”时处理哪一层数据。
/// </summary>
/// <remarks>
///     随机数没有名单历史记录，重置只涉及两样东西：临时剩余数字和随机数统计。名单持久历史属于
///     公平抽取的权重来源，绝不能被这个重置动作清掉，因此这里刻意不提供“清除名单历史”的选项。
/// </remarks>
public enum RandomNumberResetBehavior
{
    /// <summary>只清临时剩余数字，保留统计；默认值。</summary>
    TemporaryOnly = 0,

    /// <summary>清临时剩余数字，同时清掉概览页使用的随机数统计。</summary>
    TemporaryAndStatistics = 1
}