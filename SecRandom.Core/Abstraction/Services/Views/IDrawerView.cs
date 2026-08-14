namespace SecRandom.Core.Abstraction.Services.Views;

/// <summary>
///     Host drawer capability exposed to plugins. A plugin may open/close the host shell's right-side
///     drawer and query its open state. Content is the raw Avalonia visual the plugin wants to show;
///     the host decides how to present it. Empty shells (no visible main/settings view) are no-ops.
/// </summary>
public interface IDrawerView
{
    void OpenDrawer(object content);

    void CloseDrawer();

    bool IsDrawerOpen { get; }
}
