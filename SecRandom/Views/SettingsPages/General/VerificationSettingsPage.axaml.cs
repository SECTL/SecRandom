using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Shared;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.verification", FluentIcons.DocumentCheckmarkFilled, "settings.general")]
public partial class VerificationSettingsPage : UserControl
{
    public VerificationSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
    }

    private void OpenProofFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var directory = Utils.GetDirectoryPath("proofs");
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        else if (OperatingSystem.IsLinux())
            Process.Start(new ProcessStartInfo("xdg-open", directory) { UseShellExecute = false });
        else if (OperatingSystem.IsMacOS())
            Process.Start(new ProcessStartInfo("open", directory) { UseShellExecute = false });
    }
}
