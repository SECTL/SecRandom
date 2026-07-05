using System;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Services;

namespace SecRandom.Views.SettingsPages.About;

[PageInfo("settings.about", "\uE9E4", location: PageLocation.Bottom, hidePageTitle: true)]
public partial class AboutSettingsPage : UserControl
{
    private OnlineStatusService OnlineStatusService { get; } = IAppHost.Host!.Services
        .GetServices<IHostedService>().OfType<OnlineStatusService>().First();
    public int OnlineUsersCount => OnlineStatusService.CachedOnlineCount;
    
    public AboutSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
    }

    private void OpenLink(string url)
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        else if (OperatingSystem.IsLinux())
            Process.Start(new ProcessStartInfo("xdg-open", url)
            {
                UseShellExecute = false
            });
        else if (OperatingSystem.IsMacOS())
            Process.Start(new ProcessStartInfo("open", url)
            {
                UseShellExecute = false
            });
    }

    private void UriNavigationCommands_OnClick(object sender, RoutedEventArgs e)
    {
        var url = e.Source switch
        {
            FASettingsExpanderItem s => s.CommandParameter?.ToString(),
            Button s => s.CommandParameter?.ToString(),
            _ => null
        };

        if (url != null) OpenLink(url);
    }
}
