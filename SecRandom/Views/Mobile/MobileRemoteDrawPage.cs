using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.ViewModels.Mobile;

namespace SecRandom.Views.Mobile;

/// <summary>
///     控制端"远程抽取"页：选教室设备、读名单/奖池、下发抽取，页面本身只有布局，
///     状态与命令都在 <see cref="MobileRemoteDrawViewModel" /> 里（能脱离界面单测的部分都放那儿）。
/// </summary>
/// <remarks>
///     <para>
///         <b>这是手机版式的那个视图</b>：底部导航的第 5 档按 <see cref="MobilePageIds.RemoteDraw" /> 取它。
///         桌面与平板用桌面主页面版式的 <c>Views/MainPages/RemoteDrawPage</c>（左侧结果区 + 右侧控制区），
///         因为竖排表单在桌面尺寸上既浪费宽度也不像这个应用的其他主页。
///     </para>
///     <para>
///         <b>但 ViewModel 只有一份</b>（<see cref="MobileRemoteDrawViewModel" />）：协议、状态与命令两个
///         视图共用，UI 差异只是版式差异。谁为某个宿主复制一份 VM，协议一改就会有一边先烂掉。
///     </para>
/// </remarks>
[PageInfo(MobilePageIds.RemoteDraw, FluentIcons.SendFilled, location: PageLocation.Top)]
public sealed partial class MobileRemoteDrawPage : UserControl
{
    public MobileRemoteDrawPage(MobileRemoteDrawViewModel viewModel)
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
