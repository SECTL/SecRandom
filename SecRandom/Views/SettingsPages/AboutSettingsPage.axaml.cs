using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.about", "\uE9E4", location: PageLocation.Bottom, hidePageTitle: true)]
public partial class AboutSettingsPage : UserControl
{
    public AboutSettingsPage()
    {
        InitializeComponent();
    }

    private void OpenLink(string url)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        else if (OperatingSystem.IsLinux())
        {
            Process.Start(new ProcessStartInfo("xdg-open", url)
            {
                UseShellExecute = false
            });
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo("open", url)
            {
                UseShellExecute = false
            });
        }
    }
    
    private void UriNavigationCommands_OnClick(object sender, RoutedEventArgs e)
    {
        var url = e.Source switch
        {
            SettingsExpanderItem s => s.CommandParameter?.ToString(),
            Button s => s.CommandParameter?.ToString(),
            _ => null
        };

        if (url != null)
        {
            OpenLink(url);
        }
    }
}