using Avalonia.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.ViewModels.SettingsPages;

namespace SecRandom.Views.SettingsPages.General;

/// <summary>
///     集控设置页（<c>settings.general.control</c>）：本机开关、节点身份、连接状态。
/// </summary>
/// <remarks>
///     页面本身不持有状态：开关、组 ID、节点通道地址与期望状态都通过
///     <see cref="ControlSettingsPageViewModel" /> 直接读写 <c>data/config/control/node-state.json</c>，
///     刻意不放进 <c>settings.json</c>——设置导入不该能悄悄打开远控。
/// </remarks>
[PageInfo("settings.general.control", FluentIcons.PlugConnectedFilled, "settings.general")]
public partial class ControlSettingsPage : UserControl
{
    public ControlSettingsPage()
    {
        ViewModel = IAppHost.GetService<ControlSettingsPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public ControlSettingsPageViewModel ViewModel { get; }
}
