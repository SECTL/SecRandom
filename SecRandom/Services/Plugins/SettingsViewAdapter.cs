using System;
using Avalonia.Threading;
using SecRandom.Core.Abstraction.Services.Views;
using SecRandom.Views;

namespace SecRandom.Services.Plugins;

/// <summary>
///     Desktop implementation of <see cref="ISettingsView"/> that delegates to the live
///     <see cref="SettingsView.Current"/> shell. Calls are marshaled to the UI thread; when no settings
///     view is open the drawer/navigation calls no-op.
/// </summary>
public sealed class SettingsViewAdapter : ISettingsView
{
    public void OpenDrawer(object content)
    {
        RunOnUiThread(() => SettingsView.Current?.OpenDrawer(content));
    }

    public void CloseDrawer()
    {
        RunOnUiThread(() => SettingsView.Current?.CloseDrawer());
    }

    public bool IsDrawerOpen => SettingsView.Current?.ViewModel.IsDrawerOpen ?? false;

    public void NavigateToPage(string id)
    {
        RunOnUiThread(() => SettingsView.Current?.NavigateToPage(id));
    }

    public void NavigateToPreviewPage(string id)
    {
        RunOnUiThread(() => SettingsView.Current?.NavigateToPreviewPage(id));
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
