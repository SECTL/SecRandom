using System;
using Avalonia.Media;
using SecRandom.Helpers;

namespace SecRandom;

public partial class App
{
    public static bool IsMicaSupported { get; } =
        OperatingSystem.IsWindows()
        && Environment.OSVersion.Version >= new Version(10, 0, 22000, 0)
        && AvaloniaUnsafeAccessorHelpers.GetActiveWin32CompositionMode() ==
        AvaloniaUnsafeAccessorHelpers.Win32CompositionMode.WinUIComposition;

    public static bool SupportsProgrammaticWindowPositioning { get; } = !OperatingSystem.IsLinux()
        || (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            && !string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase));

    /// <summary>Linux/macOS 使用窗口后端的原生移动循环，避免合成器拒绝应用自行写入窗口坐标。</summary>
    public static bool UsesNativeWindowDrag { get; } = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
}
