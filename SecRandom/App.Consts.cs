using System;
using Avalonia.Media;
using SecRandom.Helpers;

namespace SecRandom;

public partial class App
{
    public static bool IsAcrylicBlurSupported { get; } =
        OperatingSystem.IsWindows()
        && Environment.OSVersion.Version >= new Version(10, 0, 18362, 0)
        && AvaloniaUnsafeAccessorHelpers.GetActiveWin32CompositionMode() ==
        AvaloniaUnsafeAccessorHelpers.Win32CompositionMode.WinUIComposition;

    public static bool IsMicaSupported { get; } =
        OperatingSystem.IsWindows()
        && Environment.OSVersion.Version >= new Version(10, 0, 22000, 0)
        && AvaloniaUnsafeAccessorHelpers.GetActiveWin32CompositionMode() ==
        AvaloniaUnsafeAccessorHelpers.Win32CompositionMode.WinUIComposition;

    public static bool SupportsProgrammaticWindowPositioning { get; } = !OperatingSystem.IsLinux()
        || (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            && !string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase));
}
