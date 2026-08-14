using System;
using System.Threading;
using Avalonia.Threading;
using SecRandom.Core.Abstraction.Services.Views;
using SecRandom.Views;

namespace SecRandom.Services.Plugins;

/// <summary>
///     Desktop implementation of <see cref="IMainView"/> that delegates to the live <see cref="MainView.Current"/>
///     shell. Calls are marshaled to the UI thread; when no main view is open the drawer/navigation calls no-op.
/// </summary>
public sealed class MainViewAdapter : IMainView
{
    public void OpenDrawer(object content)
    {
        RunOnUiThread(() => MainView.Current?.OpenDrawer(content));
    }

    public void CloseDrawer()
    {
        RunOnUiThread(() => MainView.Current?.CloseDrawer());
    }

    public bool IsDrawerOpen => MainView.Current?.ViewModel.IsDrawerOpen ?? false;

    public void NavigateToPage(string id)
    {
        RunOnUiThread(() => MainView.Current?.SelectNavigationItemById(id));
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
