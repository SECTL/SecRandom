namespace SecRandom.Shared.DataRoot;

/// <summary>
///     Converts a path persisted inside a data file between the absolute form older builds wrote and the
///     data-root-relative form this build writes. Relative values survive a data-root move; absolute values keep
///     working exactly as before, so a migration never has to guess what an external path meant.
/// </summary>
public static class DataPathReference
{
    /// <summary>
    ///     True when the stored value is meant to be resolved against the current data root. URLs and rooted paths
    ///     are explicitly not: they are self-contained or deliberately outside the data root.
    /// </summary>
    public static bool IsStoredRelative(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal))
            return false;

        return !Path.IsPathRooted(trimmed) && !trimmed.StartsWith('~');
    }

    /// <summary>
    ///     Stores <paramref name="absolutePath" /> relative to <paramref name="dataRoot" /> when it lives inside it,
    ///     and otherwise keeps the original value untouched (an external or shared path must not be rewritten).
    /// </summary>
    public static string ToStored(string? absolutePath, string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || string.IsNullOrWhiteSpace(dataRoot))
            return absolutePath ?? string.Empty;

        var value = absolutePath.Trim();
        if (IsStoredRelative(value))
            return value;

        string fullPath;
        string fullRoot;
        try
        {
            fullPath = Path.GetFullPath(value);
            fullRoot = Path.GetFullPath(dataRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or
                                           PathTooLongException)
        {
            return value;
        }

        if (!DataRootPaths.IsSameOrNested(fullRoot, fullPath))
            return value;

        var relative = Path.GetRelativePath(fullRoot, fullPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    /// <summary>Resolves a stored value against the current data root; absolute values are returned unchanged.</summary>
    public static string Resolve(string? storedPath, string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return storedPath ?? string.Empty;

        var value = storedPath.Trim();
        if (!IsStoredRelative(value) || string.IsNullOrWhiteSpace(dataRoot))
            return value;

        return Path.GetFullPath(Path.Combine(dataRoot, value.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>True when the two stored values point at the same entry, allowing relative and absolute forms.</summary>
    public static bool RefersToSame(string? left, string? right, string dataRoot)
    {
        return string.Equals(
            Resolve(left, dataRoot).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar),
            Resolve(right, dataRoot).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar),
            DataRootPaths.PathComparison);
    }
}