using System.ComponentModel;

namespace SecRandom.Shared;

public static class Utils
{
    private static string GetPath([Localizable(false)] params string[] strings)
    {
        return Path.Combine([AppContext.BaseDirectory, "data", ..strings]);
    }

    public static string GetFilePath([Localizable(false)] params string[] strings)
    {
        var path = GetPath(strings);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

        return path;
    }

    public static string GetDirectoryPath([Localizable(false)] params string[] strings)
    {
        var path = GetPath(strings);

        if (!string.IsNullOrEmpty(path) && !Directory.Exists(path)) Directory.CreateDirectory(path);

        return path;
    }
}