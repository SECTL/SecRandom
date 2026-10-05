using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.ViewModels.Mobile;
using SecRandom.Views.Mobile;

namespace SecRandom.Views.MainPages;

/// <summary>
///     桌面/平板版式的"远程抽取"页：**左侧是结果区，右侧是控制区**。
/// </summary>
/// <remarks>
///     <para>
///         <b>一个 ViewModel，两个视图</b>：协议、状态与命令仍然只有
///         <see cref="MobileRemoteDrawViewModel" /> 一份（设备列表、读名单、条件、抽取、重置、回执解析），
///         这个页面只负责桌面版式。因此这里**不复制任何协议逻辑**，需要的展示态要么已经能从 VM 绑到，
///         要么加进 VM（例如 <c>DrawnCountText</c>），绝不在视图里自己算——两侧各算一遍必然有一天对不上。
///     </para>
///     <para>
///         手机仍用 <c>Views/Mobile/MobileRemoteDrawPage</c>（竖排表单 + 底部第 5 档）。桌面/平板的差异是
///         <b>版式</b>差异，不是行为差异：同一份 VM 决定了两个视图说的是同一件事。
///     </para>
///     <para>
///         结果区**只显示远端设备回执里真的返回了什么**（<c>detail.drawn</c>）：没有回执就一条成员都不列，
///         只显示状态文案（还没抽/失败/超时/被拒）。前端不造结果，也不在缺回执时编一句"抽到了某某"。
///     </para>
/// <remarks>
///     <para>
///         <b>页面不报自己的名字</b>（<c>hidePageTitle: true</c>）：与点名/抽奖页一致——主界面侧栏已经写着
///         「远程抽取」，页内再顶一行同名的标题只会占掉首屏。标题由外壳（`MainView` 的 TitleContainer，
///         绑 `SelectedPageInfo.Name`）负责，导航标签照旧用它，只是不再画进内容区。
///     </para>
/// </remarks>
[PageInfo(MobilePageIds.RemoteDraw, FluentIcons.SendFilled, location: PageLocation.Bottom, hidePageTitle: true)]
public sealed partial class RemoteDrawPage : UserControl
{
    public RemoteDrawPage(MobileRemoteDrawViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public MobileRemoteDrawViewModel ViewModel { get; }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // 每次进页面都刷新一次：组与设备是"现在"的状态，用进页面时缓存的数据发命令只会撞上过期的节点。
        await ViewModel.InitializeAsync();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) =>
        ViewModel.Dispose();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
