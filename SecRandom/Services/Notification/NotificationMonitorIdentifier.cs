using System;
using Avalonia;

namespace SecRandom.Services.Notification;

public static class NotificationMonitorIdentifier
{
    private const string BoundsPrefix = "bounds:";

    public static string Get(string? displayName, PixelRect bounds)
    {
        return string.IsNullOrWhiteSpace(displayName)
            ? $"{BoundsPrefix}{bounds.X}:{bounds.Y}:{bounds.Width}:{bounds.Height}"
            : displayName;
    }

    public static bool Matches(string? displayName, PixelRect bounds, string identifier)
    {
        return string.Equals(Get(displayName, bounds), identifier, StringComparison.OrdinalIgnoreCase)
            || string.Equals(displayName, identifier, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                $"{BoundsPrefix}{bounds.X}:{bounds.Y}:{bounds.Width}:{bounds.Height}",
                identifier,
                StringComparison.OrdinalIgnoreCase);
    }

    public static string GetLabel(
        string? displayName,
        PixelRect bounds,
        bool isPrimary,
        int index,
        string format,
        string primarySuffix)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? $"Display {index + 1}" : displayName;
        return string.Format(
            format,
            name,
            bounds.Width,
            bounds.Height,
            bounds.X,
            bounds.Y,
            isPrimary ? primarySuffix : string.Empty);
    }
}
