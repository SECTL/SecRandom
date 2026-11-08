using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.Linux;

/// <summary>
///     Linux 应用图标提取：可执行文件 → 同名 <c>.desktop</c> 入口 → <c>Icon=</c> →
///     XDG 图标主题里的 PNG。SVG/XPM 图标不解码，直接报告"格式不支持"，让用户改用图片导入。
/// </summary>
public sealed class LinuxApplicationIconExtractor : IPlatformApplicationIconExtractor
{
    private const int MaxIconBytes = 4 * 1024 * 1024;
    private const int MaxDesktopEntryBytes = 256 * 1024;
    private const int MaxMatches = 200;

    private static readonly string[] DesktopEntryDirectories = BuildDesktopEntryDirectories();

    private static readonly string[] IconRoots = BuildIconRoots();

    public PlatformIconExtractionResult Extract(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) && !Directory.Exists(path))
            return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.Failed);

        var extension = Path.GetExtension(path).ToLowerInvariant();
        switch (extension)
        {
            case ".desktop":
                return ExtractFromDesktopEntry(path);
            case ".png":
                return ReadPngFile(path);
            case ".svg" or ".xpm":
                return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.UnsupportedFormat);
            case ".jpg" or ".jpeg" or ".webp":
                // 应用层自己能解码普通图片，这里只声明"不是应用图标"。
                return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.NotAnApplication);
        }

        var desktopEntry = FindDesktopEntryForExecutable(path);
        return desktopEntry is null
            ? PlatformIconExtractionResult.Failure(PlatformIconExtractionError.NotAnApplication)
            : ExtractFromDesktopEntry(desktopEntry);
    }

    private static PlatformIconExtractionResult ExtractFromDesktopEntry(string desktopEntryPath)
    {
        string content;
        try
        {
            var info = new FileInfo(desktopEntryPath);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxDesktopEntryBytes)
                return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.NoIconFound);
            content = File.ReadAllText(desktopEntryPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.Failed);
        }

        var iconValue = DesktopEntryIconReader.ReadIconValue(content);
        if (string.IsNullOrWhiteSpace(iconValue))
            return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.NoIconFound);

        return ResolveIcon(iconValue.Trim());
    }

    private static PlatformIconExtractionResult ResolveIcon(string iconValue)
    {
        if (iconValue.Contains('/'))
            return File.Exists(iconValue)
                ? ReadPngFile(iconValue)
                : PlatformIconExtractionResult.Failure(PlatformIconExtractionError.NoIconFound);

        var png = FindIconFile(iconValue, ".png");
        if (png is not null)
            return ReadPngFile(png);

        if (FindIconFile(iconValue, ".svg") is not null || FindIconFile(iconValue, ".xpm") is not null)
            return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.UnsupportedFormat);

        return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.NoIconFound);
    }

    private static string? FindIconFile(string name, string extension)
    {
        var fileName = name + extension;
        string? best = null;
        var bestScore = -1;
        var matches = 0;

        foreach (var root in IconRoots)
        {
            if (!Directory.Exists(root))
                continue;

            try
            {
                foreach (var candidate in Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
                {
                    var score = ScoreIconPath(candidate);
                    if (score > bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                    }

                    if (++matches >= MaxMatches)
                        return best;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // 某个主题目录不可读时继续看下一个根。
            }
        }

        return best;
    }

    private static int ScoreIconPath(string path)
    {
        if (path.Contains("scalable", StringComparison.OrdinalIgnoreCase))
            return 300;
        if (path.Contains("512x512", StringComparison.OrdinalIgnoreCase))
            return 512;
        if (path.Contains("256x256", StringComparison.OrdinalIgnoreCase))
            return 256;
        if (path.Contains("128x128", StringComparison.OrdinalIgnoreCase))
            return 128;
        if (path.Contains("64x64", StringComparison.OrdinalIgnoreCase))
            return 64;
        if (path.Contains("48x48", StringComparison.OrdinalIgnoreCase))
            return 48;
        if (path.Contains("32x32", StringComparison.OrdinalIgnoreCase))
            return 32;
        return 16;
    }

    private static PlatformIconExtractionResult ReadPngFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxIconBytes)
                return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.NoIconFound);

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 8 ||
                bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47 ||
                bytes[4] != 0x0D || bytes[5] != 0x0A || bytes[6] != 0x1A || bytes[7] != 0x0A)
                return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.UnsupportedFormat);

            var image = new PlatformIconImage(0, 0, bytes, PlatformIconPixelFormat.Png);
            return PlatformIconExtractionResult.Success(image);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.Failed);
        }
    }

    private static string? FindDesktopEntryForExecutable(string executablePath)
    {
        var baseName = Path.GetFileNameWithoutExtension(executablePath);
        if (string.IsNullOrWhiteSpace(baseName))
            return null;

        foreach (var directory in DesktopEntryDirectories)
        {
            if (!Directory.Exists(directory))
                continue;

            var candidate = Path.Combine(directory, baseName + ".desktop");
            if (!File.Exists(candidate))
                continue;

            try
            {
                var content = File.ReadAllText(candidate);
                var exec = DesktopEntryIconReader.ReadExecutable(content);
                if (exec is null)
                    continue;

                var execName = Path.GetFileName(exec);
                if (string.Equals(execName, baseName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFullPath(executablePath), Path.GetFullPath(exec), StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // 忽略单个损坏的入口文件。
            }
        }

        return null;
    }

    private static string[] BuildDesktopEntryDirectories()
    {
        var directories = new List<string>
        {
            "/usr/share/applications",
            "/usr/local/share/applications",
            "/var/lib/flatpak/exports/share/applications",
            "/var/lib/snapd/desktop/applications"
        };

        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
                dataHome = Path.Combine(home, ".local", "share");
        }

        if (!string.IsNullOrWhiteSpace(dataHome))
            directories.Insert(0, Path.Combine(dataHome, "applications"));

        var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        if (!string.IsNullOrWhiteSpace(dataDirs))
        {
            foreach (var dir in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                directories.Add(Path.Combine(dir, "applications"));
        }

        return directories.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string[] BuildIconRoots()
    {
        var roots = new List<string>
        {
            "/usr/share/pixmaps",
            "/usr/local/share/pixmaps",
            "/usr/share/icons",
            "/usr/local/share/icons"
        };

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            roots.Insert(0, Path.Combine(home, ".local", "share", "icons"));
            roots.Insert(0, Path.Combine(home, ".icons"));
        }

        var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        if (!string.IsNullOrWhiteSpace(dataDirs))
        {
            foreach (var dir in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                roots.Add(Path.Combine(dir, "icons"));
        }

        return roots.Distinct(StringComparer.Ordinal).ToArray();
    }
}
