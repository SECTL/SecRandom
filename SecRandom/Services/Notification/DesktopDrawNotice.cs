using System;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using SecRandom.Views;
using FloatingWindowView = SecRandom.Views.FloatingWindow;

namespace SecRandom.Services.Notification;

/// <summary>Displays draw rejection notices independently of the result presentation.</summary>
internal static class DesktopDrawNotice
{
    private static DrawBlockedToastWindow? _window;
    private static int _revision;

    public static void Show(string message, FloatingWindowView? anchor = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()
                                         && !OperatingSystem.IsLinux() && !OperatingSystem.IsFreeBSD())
        {
            MainView.ShowToast(message, FAInfoBarSeverity.Warning);
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
            ShowOnUiThread(message, anchor);
        else
            Dispatcher.UIThread.Post(() => ShowOnUiThread(message, anchor));
    }

    internal static DrawBlockedToastWindow? Current => _window;

    private static void ShowOnUiThread(string message, FloatingWindowView? anchor)
    {
        var window = _window;
        if (window is null)
        {
            window = _window = new DrawBlockedToastWindow();
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window))
                    _window = null;
            };
        }

        window.SetMessage(message);
        if (!window.IsVisible)
            window.Show();
        window.PositionOnScreen(anchor);
        Dispatcher.UIThread.Post(() => window.PositionOnScreen(anchor), DispatcherPriority.Loaded);

        var revision = ++_revision;
        DispatcherTimer.RunOnce(() =>
        {
            if (revision == _revision && ReferenceEquals(_window, window))
                window.Close();
        }, TimeSpan.FromSeconds(5));
    }
}
