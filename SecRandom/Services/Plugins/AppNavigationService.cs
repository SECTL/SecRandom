using SecRandom.Core.Abstraction.Services;

namespace SecRandom.Services.Plugins;

/// <summary>
///     App-layer implementation of <see cref="IAppNavigationService"/> that delegates to the host window
///     entry points, preserving the settings authorization and lottery-capability gates already enforced there.
/// </summary>
public sealed class AppNavigationService : IAppNavigationService
{
    public void OpenMainWindow(string? pageId = null)
    {
        App.ShowMainWindow(pageId);
    }

    public void OpenSettingsWindow(string? pageId = null)
    {
        App.ShowSettingsWindow(pageId);
    }

    public void OpenQuickDraw()
    {
        App.ShowQuickDrawWindow();
    }
}
