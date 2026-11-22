using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Services.Presentation;

/// <summary>Shared display order; persisted enum values retain their original meaning.</summary>
public static class NotificationBackgroundCatalog
{
    public static IReadOnlyList<WindowBackgroundStyle> FloatingStyles { get; } =
        Array.AsReadOnly(new[]
        {
            WindowBackgroundStyle.Theme,
            WindowBackgroundStyle.Solid, WindowBackgroundStyle.Gradient, WindowBackgroundStyle.Image
        });

    public static FullscreenBackgroundMode ToFullscreenMode(WindowBackgroundStyle style) => style switch
    {
        WindowBackgroundStyle.Theme => FullscreenBackgroundMode.Theme,
        WindowBackgroundStyle.Solid => FullscreenBackgroundMode.Solid,
        WindowBackgroundStyle.Gradient => FullscreenBackgroundMode.Gradient,
        WindowBackgroundStyle.Image => FullscreenBackgroundMode.Image,
        _ => FullscreenBackgroundMode.Theme
    };
}
