namespace SecRandom.Core.Services;

/// <summary>
/// Keeps the online-status device type vocabulary consistent across hosts.
/// </summary>
public static class OnlineStatusDeviceType
{
    public static string Detect()
    {
        if (OperatingSystem.IsAndroid())
            return "android";

        if (OperatingSystem.IsIOS())
            return "ios";

        if (OperatingSystem.IsWindows())
            return "windows-desktop";

        if (OperatingSystem.IsMacOS())
            return "macos-desktop";

        if (OperatingSystem.IsLinux())
            return "linux-desktop";

        return "unknown-desktop";
    }
}
