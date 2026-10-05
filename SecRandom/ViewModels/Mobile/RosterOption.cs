using SecRandom.Core.Services.ControlNode;

namespace SecRandom.ViewModels.Mobile;

/// <summary>
///     远程抽取页上的一份名单（控制面 <c>roster.read</c> 返回的一项）。
/// </summary>
/// <remarks>
///     保留成员本体而不是只留名字：条件选项（性别、分组）必须**从这份名单的成员派生**，
///     写死"男/女/第一组"在真实名单上一定选不中——分组名是老师自己输入的。
/// </remarks>
public sealed record RosterOption(
    string Name,
    bool IsDefault,
    int MemberCount,
    int Total,
    bool Truncated,
    IReadOnlyList<ControlRosterMemberPayload> Members)
{
    /// <summary>下拉框里显示的文字；默认名单带一个星号，省得用户去猜哪一份会被本机默认使用。</summary>
    public string DisplayLabel => IsDefault ? $"{Name} ★" : Name;
}
