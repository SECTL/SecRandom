using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Attributes;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.verification", FluentIcons.DocumentCheckmarkFilled, "settings.general")]
public partial class VerificationSettingsPage : UserControl
{
    private static readonly int[] RetentionOptions = [7, 15, 30, 60, 90, 0];
    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    public VerificationSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
    }

    public int SelectedRetentionIndex
    {
        get => Array.IndexOf(RetentionOptions, ConfigHandler.Data.General.ProofRetention.RetentionDays) is var index && index >= 0
            ? index
            : Array.IndexOf(RetentionOptions, 30);
        set
        {
            if (value < 0 || value >= RetentionOptions.Length)
                return;

            ConfigHandler.Data.General.ProofRetention.RetentionDays = RetentionOptions[value];
            ConfigHandler.Save();
        }
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

    private static void OpenVerificationWebsite_OnClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://fair.sectl.cn/") { UseShellExecute = true });
    }
}
