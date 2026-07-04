using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Helpers.UI;
using SecRandom.Services.CrashRecovery;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.debug", "\uE2C8", location: PageLocation.Bottom)]
public partial class DebugSettingsPage : UserControl
{
    public DebugSettingsPage()
    {
        InitializeComponent();
    }

    private void SendToast_OnClick(object? sender, RoutedEventArgs e)
    {
        this.ShowToast("测试 Toast~");
    }

    private void OpenDrawer_Test(object? sender, RoutedEventArgs e)
    {
        SettingsView.Current?.OpenDrawer(this.FindResource("DrawerTest")!);
    }

    private void ShowCrashRecoveryWindow_OnClick(object? sender, RoutedEventArgs e)
    {
        string reportPath = CrashRecoveryRuntime.TryWriteCrashReport(
            new InvalidOperationException("SECRANDOM_DEBUG_CRASH_PROMPT"),
            [Path.Combine(Path.GetTempPath(), "SecRandom", "crashes")]);

        CrashRecoveryWindow window = new(
            new CrashRecoveryPromptOptions(reportPath, false),
            () =>
            {
                App.Current.Restart();
                return true;
            });
        window.Show();
    }

    private void ThrowDispatcherException_OnClick(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => throw new InvalidOperationException("SECRANDOM_DEBUG_DISPATCHER_CRASH"));
    }

    private void RestartProgram_OnClick(object? sender, RoutedEventArgs e)
    {
        App.Current.Restart();
    }
}
