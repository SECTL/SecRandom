namespace SecRandom.Android.Storage;

internal static class DocumentsPathPolicy
{
    internal static bool IsAccessible(string canonicalPath, string rootPath, string dataPath) =>
        (Same(canonicalPath, rootPath) || Same(canonicalPath, dataPath) || IsContainedBy(canonicalPath, dataPath))
        && !IsProtected(canonicalPath, dataPath);

    internal static bool CanMutate(string canonicalPath, string rootPath, string dataPath) =>
        IsAccessible(canonicalPath, rootPath, dataPath)
        && !Same(canonicalPath, rootPath)
        && !Same(canonicalPath, dataPath)
        && !IsContainedBy(Path.Combine(dataPath, "config", "security"), canonicalPath)
        && !IsContainedBy(Path.Combine(dataPath, "config", "voice"), canonicalPath);

    private static bool IsProtected(string path, string dataPath)
    {
        var securityPath = Path.Combine(dataPath, "config", "security");
        var voicePath = Path.Combine(dataPath, "config", "voice");
        return Same(path, securityPath) || IsContainedBy(path, securityPath)
               || Same(path, voicePath) || IsContainedBy(path, voicePath);
    }

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);

    private static bool IsContainedBy(string childPath, string parentPath) =>
        childPath.StartsWith(parentPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);
}
