using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Rendering;

namespace SecRandom.Helpers;

internal static class AvaloniaUnsafeAccessorHelpers
{
    public enum Win32CompositionMode
    {
        WinUiComposition = 1,
        DirectComposition = 2,
        LowLatencyDxgiSwapChain = 3,
        RedirectionSurface = 4
    }

    private static IAvaloniaDependencyResolver? AvaloniaLocator { get; } = GetCurrentAvaloniaLocator(null);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "get_Current")]
    private static extern IAvaloniaDependencyResolver? GetCurrentAvaloniaLocator(AvaloniaLocator? nullLocator);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "GetService")]
    private static extern object? GetAvaloniaDependencyService(IAvaloniaDependencyResolver? avaloniaLocator,
        Type serviceType);

    private static T? GetAvaloniaLocatorService<T>() where T : class
    {
        if (AvaloniaLocator is null)
            return null;
        var result = GetAvaloniaDependencyService(AvaloniaLocator, typeof(T));
        return result as T;
    }

    public static Win32CompositionMode? GetActiveWin32CompositionMode()
    {
        var renderTimer = GetAvaloniaLocatorService<IRenderTimer>();
        var renderTimerClassName = renderTimer?.GetType().Name;
        var win32CompositionMode = renderTimerClassName switch
        {
            "WinUiCompositorConnection" => Win32CompositionMode.WinUiComposition,
            "DirectCompositionConnection" => Win32CompositionMode.DirectComposition,
            "DxgiConnection" => Win32CompositionMode.LowLatencyDxgiSwapChain,
            _ => Win32CompositionMode.RedirectionSurface
        };
        return win32CompositionMode;
    }
}