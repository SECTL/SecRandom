using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.Orchestration;

namespace SecRandom.Views.SettingsPages.Picking;

/// <summary>排斥名单：全局不参加，抽取前就被拿掉。</summary>
[PageInfo(OrchestrationEditingSession.ExcludePage, FluentIcons.ProhibitedFilled, "settings.picking", isHide: true)]
public sealed class OrchestrationExcludeSettingsPage : OrchestrationRulePageBase
{
    public OrchestrationExcludeSettingsPage() : base(OrchestrationRuleKind.Exclude)
    {
        // 页面不出现在侧栏：由编排抽取总览的「排斥名单」设置卡跳转进来。
    }
}

/// <summary>权重名单：轮级手动权重，作为公平权重的乘数。</summary>
[PageInfo(OrchestrationEditingSession.WeightPage, FluentIcons.ScalesFilled, "settings.picking", isHide: true)]
public sealed class OrchestrationWeightSettingsPage : OrchestrationRulePageBase
{
    public OrchestrationWeightSettingsPage() : base(OrchestrationRuleKind.Weight)
    {
        // 页面不出现在侧栏：由编排抽取总览的「权重名单」设置卡跳转进来。
    }
}

/// <summary>延后名单：只把名字挪到本轮队尾，不取消参与资格。</summary>
[PageInfo(OrchestrationEditingSession.DeferPage, FluentIcons.TimerFilled, "settings.picking", isHide: true)]
public sealed class OrchestrationDeferSettingsPage : OrchestrationRulePageBase
{
    public OrchestrationDeferSettingsPage() : base(OrchestrationRuleKind.Defer)
    {
        // 页面不出现在侧栏：由编排抽取总览的「延后名单」设置卡跳转进来。
    }
}

/// <summary>顺序名单：本轮出场队列，前面的先抽、抽完就离队。</summary>
[PageInfo(OrchestrationEditingSession.OrderPage, FluentIcons.ArrowSortFilled, "settings.picking", isHide: true)]
public sealed class OrchestrationOrderSettingsPage : OrchestrationRulePageBase
{
    public OrchestrationOrderSettingsPage() : base(OrchestrationRuleKind.Order)
    {
        // 页面不出现在侧栏：由编排抽取总览的「顺序名单」设置卡跳转进来。
    }
}

/// <summary>必中名单：本轮必定出场的人，与顺序名单互斥。</summary>
[PageInfo(OrchestrationEditingSession.GuaranteePage, FluentIcons.TargetFilled, "settings.picking", isHide: true)]
public sealed class OrchestrationGuaranteeSettingsPage : OrchestrationRulePageBase
{
    public OrchestrationGuaranteeSettingsPage() : base(OrchestrationRuleKind.Guarantee)
    {
        // 页面不出现在侧栏：由编排抽取总览的「必中名单」设置卡跳转进来。
    }
}