using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Services.Mobile;
using SecRandom.ViewModels.Mobile;

namespace SecRandom.Views.Mobile;

/// <summary>
///     控制端"远程抽取"页：登录账号 → 选地点/年级/班级 → 下发抽取，页面本身只有布局，
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
///     <para>
///         手机/平板没有「设置 → 集控」页，也没有配对码通道：未登录时引导用户打开设置页顶部账号区，
///         因此这里不再需要"重新接入"的导航兜底。
///     </para>
/// </remarks>
[PageInfo(MobilePageIds.RemoteDraw, FluentIcons.SendFilled, location: PageLocation.Top)]
public sealed partial class MobileRemoteDrawPage : UserControl
{
    private readonly IMobileSettingsNavigator _settingsNavigator;

    public MobileRemoteDrawPage(
        MobileRemoteDrawViewModel viewModel,
        IMobileSettingsNavigator settingsNavigator)
    {
        ViewModel = viewModel;
        _settingsNavigator = settingsNavigator;
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public MobileRemoteDrawViewModel ViewModel { get; }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // 每次进页面都刷新一次：地点/年级/班级与占班设备是"现在"的状态，缓存的数据只会让命令打到过期目标上。
        await ViewModel.InitializeAsync();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ViewModel.Dispose();
    }

    private async void OpenSettings_OnClick(object? sender, RoutedEventArgs e)
    {
        await _settingsNavigator.OpenAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}