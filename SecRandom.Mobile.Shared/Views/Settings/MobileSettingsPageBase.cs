using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Views;
using SecRandom.Mobile.Views;

namespace SecRandom.Mobile.Views.Settings;

/// <summary>
/// 移动设置页的 AXAML 框架：统一处理返回、动态内容区和进入动画。
/// </summary>
public abstract class MobileSettingsPageBase : ViewBase
{
    protected void CloseView() => _ = CloseAsync(reason: ViewCloseReason.Back);

    // 能力投影直通属性，页面统一经由基类消费（详见 MobileCapabilities）。
    protected static bool IsLotteryEnabled => MobileCapabilities.IsLotteryEnabled;
    protected static bool IsAndroid => MobileCapabilities.IsAndroid;
    protected static bool SupportsInAppUpdate => MobileCapabilities.SupportsInAppUpdate;

    protected void RenderPage(IEnumerable<Control> items)
    {
        var content = this.FindControl<StackPanel>("SettingsBody");
        var scroll = this.FindControl<ScrollViewer>("PageScroll");
        if (content is null || scroll is null)
            throw new InvalidOperationException("The settings AXAML page shell is not initialized.");

        content.Children.Clear();
        foreach (var item in items)
            content.Children.Add(item);
        MobileAnimations.PlayPageEnter(scroll);
    }

    protected void OnBackButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => CloseView();

    protected void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
