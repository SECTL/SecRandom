using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using SecRandom.Services.Notification;

namespace SecRandom.Mobile.Tests;

public sealed class DesktopDrawNoticeTests
{
    [AvaloniaFact]
    public async Task RejectionShowsOnlyWarningAndReusesWindowForNextNotice()
    {
        DesktopDrawNotice.Show("当前不能抽取");
        Dispatcher.UIThread.RunJobs();
        var window = Assert.IsType<SecRandom.Views.DrawBlockedToastWindow>(DesktopDrawNotice.Current);

        try
        {
            Assert.True(window.IsVisible);
            Assert.False(window.ShowActivated);
            Assert.True(window.Topmost);
            var notice = Assert.IsType<FAInfoBar>(window.FindControl<FAInfoBar>("Notice"));
            Assert.Equal("当前不能抽取", notice.Message);
            Assert.Equal(FAInfoBarSeverity.Warning, notice.Severity);

            DesktopDrawNotice.Show("请稍后再试");
            Dispatcher.UIThread.RunJobs();
            Assert.Same(window, DesktopDrawNotice.Current);
            Assert.Equal("请稍后再试", notice.Message);
            await Task.Delay(TimeSpan.FromSeconds(5.5));
            Assert.False(window.IsVisible);
            Assert.Null(DesktopDrawNotice.Current);
        }
        finally
        {
            if (window.IsVisible)
                window.Close();
        }

        Assert.Null(DesktopDrawNotice.Current);
    }
}
