using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SecRandom.Mobile.Views;

namespace SecRandom.Mobile.Views.Settings;

/// <summary>
/// 移动设置页的 AXAML 框架：统一处理返回、动态内容区和进入动画。
/// </summary>
public abstract class MobileSettingsPageBase : UserControl
{
    protected MobileSettingsPageBase(IMobileNavigator navigator, IMobileCapabilities capabilities)
    {
        Navigator = navigator;
        Capabilities = capabilities;
    }

    protected IMobileNavigator Navigator { get; }
    protected IMobileCapabilities Capabilities { get; }
    protected bool IsLotteryEnabled => Capabilities.IsLotteryEnabled;

    protected void CloseView() => _ = Navigator.GoBackAsync();

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

    protected void LoadSettingsLayout() => AvaloniaXamlLoader.Load(this);
}
