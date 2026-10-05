using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Views;

namespace SecRandom.Mobile.Tests;

/// <summary>
/// Issue #268: the settings sidebar entry opens a separate window/view, so it must not take the
/// navigation highlight away from the main page that stays visible.
/// </summary>
public sealed class MainViewSettingsNavigationTests
{
    [AvaloniaFact]
    public void SettingsEntryKeepsItsPageMetadataWithoutSelectingItself()
    {
        var item = Assert.IsType<FANavigationViewItem>(MainView.CreateSettingsNavigationItem());

        Assert.False(item.SelectsOnInvoked);
        var info = Assert.IsType<PageInfo>(item.Tag);
        Assert.Equal("settings", info.Id);
        Assert.Equal(PageLocation.Bottom, info.Location);
    }

    [AvaloniaFact]
    public void InvokingTheSettingsEntryKeepsTheSelectedMainPage()
    {
        var rollCall = new FANavigationViewItem { Content = "Roll call" };
        var settings = MainView.CreateSettingsNavigationItem();
        var navigation = new FANavigationView
        {
            MenuItemsSource = new object[] { rollCall },
            FooterMenuItemsSource = new object[] { settings },
            SelectedItem = rollCall
        };

        var window = new Window
        {
            Width = 900,
            Height = 600,
            Content = navigation
        };
        window.Show();
        PumpUi(window);

        PageInfo? invoked = null;
        navigation.ItemInvoked += (_, args) =>
        {
            if (args.InvokedItemContainer is FANavigationViewItem { Tag: PageInfo info })
                invoked = info;
        };

        var settingsControl = (Control)settings;
        var center = settingsControl.TranslatePoint(
            new Point(settingsControl.Bounds.Width / 2, settingsControl.Bounds.Height / 2), window);
        Assert.NotNull(center);

        // 按下与抬起分两个调度帧：headless 里窗口的布局/模板/渲染都排在调度器队列上，
        // 同一个帧里连着发按下+抬起时，抬起可能在项自己的模板/指针捕获落地之前就被处理掉，
        // 于是 ItemInvoked 根本不触发（这条用例历史上就是这么假红的）。
        window.MouseDown(center.Value, MouseButton.Left);
        PumpUi(window);
        window.MouseUp(center.Value, MouseButton.Left);
        PumpUi(window);

        Assert.True(
            invoked is not null,
            $"设置入口没有触发 ItemInvoked：itemBounds={settingsControl.Bounds}, center={center}, " +
            $"selected={navigation.SelectedItem}, clientSize={window.ClientSize}");
        Assert.Equal("settings", invoked.Id);
        Assert.Same(rollCall, navigation.SelectedItem);

        window.Close();
    }

    /// <summary>
    ///     把一个 headless 窗口的挂起工作推到稳定：调度器队列 + 一次渲染计时器推进。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>为什么必须显式推渲染</b>：headless 平台不会自己跑渲染计时器，
    ///         <c>Show()</c> 只是把布局/模板/渲染排进队列。测试宿主并发跑别的测试类时，
    ///         一次 <c>RunJobs()</c> 未必能把这些队列全部排空，合成鼠标事件就会落在一个还没准备好的视觉树上。
    ///         实测：同一份二进制，<c>-v q</c> 下整包 8/10 失败、<c>-v n</c> 下 0/10 失败——
    ///         差别只是宿主输出带来的时序，因此这里不能靠"再跑一次 RunJobs"赌运气。
    ///     </para>
    ///     <para>
    ///         推进两次（队列 → 渲染 → 队列）覆盖"渲染排新工作、新工作又要再渲染"的一轮，
    ///         这是 headless 测试在需要真实输入前应当做的等待，不是把断言放宽。
    ///     </para>
    /// </remarks>
    private static void PumpUi(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
