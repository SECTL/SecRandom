namespace SecRandom.Core.Abstraction.Services;

/// <summary>
///     Window-level navigation exposed to plugins. Opening the settings window keeps the host's
///     security authorization flow; opening the main window respects lottery capability and the
///     protected window operations that the host already enforces.
/// </summary>
public interface IAppNavigationService
{
    /// <summary>Shows (or restores) the primary main window, optionally navigating to a main page id.</summary>
    void OpenMainWindow(string? pageId = null);

    /// <summary>Shows (or restores) the settings window, optionally navigating to a settings page id.</summary>
    void OpenSettingsWindow(string? pageId = null);

    /// <summary>Opens the floating quick-draw window.</summary>
    void OpenQuickDraw();
}
