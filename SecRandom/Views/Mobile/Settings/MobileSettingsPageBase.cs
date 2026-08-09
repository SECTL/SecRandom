using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SecRandom.Views.Mobile;

namespace SecRandom.Views.Mobile.Settings;

/// <summary>
/// 移动设置页的 AXAML 框架：SettingsView 统一提供返回，子页只管理内容与进入动画。
/// </summary>
public abstract class MobileSettingsPageBase : UserControl
{
    protected MobileSettingsPageBase(IMobileCapabilities capabilities)
    {
        Capabilities = capabilities;
    }

    protected IMobileCapabilities Capabilities { get; }
    protected bool IsLotteryEnabled => Capabilities.IsLotteryEnabled;
}
