using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Hosting;

namespace SecRandom.Services.Desktop;

public class TaskBarIconService : IHostedService
{
    // 托盘图标只能在 UI 线程上创建：AvaloniaObject 会记住创建它的线程的 Dispatcher，而 HostedService
    // 由 Host.StartAsync 在启动线程池线程上构造（启动链路不阻塞 UI 线程），一旦在构造函数里创建图标，
    // UI 线程之后设置菜单或可见性就会抛「The calling thread cannot access this object」。
    private readonly Lazy<TrayIcon> _mainTaskBarIcon =
        new(CreateTaskBarIcon, LazyThreadSafetyMode.ExecutionAndPublication);

    public TaskBarIconService()
    {
        App.Current.AppStopping += CurrentOnAppStopping;
    }

    public TrayIcon MainTaskBarIcon => _mainTaskBarIcon.Value;

    private void CurrentOnAppStopping(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            MainTaskBarIcon.IsVisible = false;
        });
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {

    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {

    }

    private static TrayIcon CreateTaskBarIcon()
    {
        return Dispatcher.UIThread.CheckAccess()
            ? CreateTaskBarIconCore()
            : Dispatcher.UIThread.Invoke(CreateTaskBarIconCore);
    }

    private static TrayIcon CreateTaskBarIconCore()
    {
        return new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SecRandom/Assets/AppLogo.png"))),
            ToolTipText = @"SecRandom"
        };
    }
}
