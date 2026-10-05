using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SecRandom.ViewModels.Mobile;

namespace SecRandom.Views.Mobile;

/// <summary>
///     手机端"远程抽取"页：底部导航的独立入口，页面本身只有布局，
///     状态与命令都在 <see cref="MobileRemoteDrawViewModel" /> 里（能脱离界面单测的部分都放那儿）。
/// </summary>
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
