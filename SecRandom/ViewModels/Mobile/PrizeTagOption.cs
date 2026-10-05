namespace SecRandom.ViewModels.Mobile;

/// <summary>
///     远程抽奖条件里的一个标签选项：取值**只能来自已读奖池里真实出现过的标签**。
/// </summary>
/// <remarks>
///     不做成自由文本输入：设备侧会把"该奖池里不存在的标签"按 <c>invalid_value:prize_tags:not_in_list</c>
///     拒绝，让用户在手机上敲一个注定被拒的字符串，等于把一次失败留给他自己发现。
/// </remarks>
/// <param name="Tag">标签原文（与设备侧归一化后的值完全一致）。</param>
public sealed record PrizeTagOption(string Tag)
{
    public string Label => Tag;

    public override string ToString() => Tag;
}
