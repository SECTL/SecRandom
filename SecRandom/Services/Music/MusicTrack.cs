using System;
using System.IO;

namespace SecRandom.Services.Music;

public sealed class MusicTrack(string id, string displayName, long length)
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public string Format => Path.GetExtension(Id).TrimStart('.').ToUpperInvariant();
    public string Size => FormatSize(length);

    private static string FormatSize(long length)
    {
        if (length < 1024)
            return $"{length} B";
        if (length < 1024 * 1024)
            return $"{length / 1024d:F1} KB";
        return $"{length / 1024d / 1024d:F1} MB";
    }
}

public sealed class MusicSelection(string id, string displayName, bool isAvailable = true)
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public bool IsAvailable { get; } = isAvailable;
}
