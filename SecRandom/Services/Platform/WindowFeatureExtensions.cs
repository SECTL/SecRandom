using Avalonia.Controls;
using Avalonia.Platform;
using FluentAvalonia.UI.Windowing;
using SecRandom.Core.Abstraction;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Services.Platform;

internal static class WindowFeatureExtensions
{
    public static WindowFeatureApplyResult ApplyPlatformFeatures(
        this TopLevel topLevel,
        WindowFeatures features,
        bool enabled)
    {
        return Apply(topLevel.TryGetPlatformHandle(), features, enabled);
    }

    public static WindowFeatureApplyResult ApplyPlatformFeatures(
        this FAAppWindow window,
        WindowFeatures features,
        bool enabled)
    {
        return Apply(window.TryGetPlatformHandle(), features, enabled);
    }

    private static WindowFeatureApplyResult Apply(
        IPlatformHandle? handle,
        WindowFeatures features,
        bool enabled)
    {
        var service = IAppHost.TryGetService<IWindowFeatureService>();
        if (service is null)
        {
            return WindowFeatureApplyResult.Unsupported(features,
                "The application host has not registered platform window services.");
        }

        return service.Apply(
            new PlatformWindowHandle(handle?.Handle ?? nint.Zero, handle?.HandleDescriptor),
            new WindowFeatureRequest(features, enabled));
    }
}
