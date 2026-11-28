using System.Diagnostics;

const string packageRootEnvironmentVariable = "SECRANDOM_PACKAGE_ROOT";
var root = Path.GetFullPath(AppContext.BaseDirectory);
var executableName = OperatingSystem.IsWindows() ? "SecRandom.Desktop.exe" : "SecRandom.Desktop";

var installation = PortableInstallationSelector.Select(root, executableName);

if (installation is null)
{
    Console.Error.WriteLine("找不到可启动的 SecRandom 版本。请重新下载完整安装包。");
    return 1;
}

var startInfo = new ProcessStartInfo
{
    FileName = Path.Combine(installation, executableName),
    WorkingDirectory = installation,
    UseShellExecute = false
};
startInfo.Environment[packageRootEnvironmentVariable] = root;
foreach (var argument in args)
    startInfo.ArgumentList.Add(argument);

try
{
    Process.Start(startInfo);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"无法启动 SecRandom：{exception.Message}");
    return 1;
}

public static class PortableInstallationSelector
{
public static string? Select(string root, string executableName) => Directory.EnumerateDirectories(root, "app-*")
    .Where(path => IsValidInstallation(path, executableName) && ParseVersion(path) is not null)
    .OrderByDescending(path => ParseVersion(path)!, Comparer<string>.Create(SecRandom.Services.Updates.ReleaseVersion.Compare))
    .ThenByDescending(ParseBuildNumber)
    .FirstOrDefault();

static bool IsValidInstallation(string path, string executableName)
{
    if (File.Exists(Path.Combine(path, ".partial")) || !File.Exists(Path.Combine(path, executableName)))
        return false;

    var markerPath = Path.Combine(path, "SecRandom.package.json");
    if (!File.Exists(markerPath))
        return false;

    try
    {
        using var marker = System.Text.Json.JsonDocument.Parse(File.ReadAllText(markerPath));
        var root = marker.RootElement;
        return root.TryGetProperty("product", out var product)
               && string.Equals(product.GetString(), "SecRandom", StringComparison.Ordinal)
               && root.TryGetProperty("schemaVersion", out var schemaVersion)
               && schemaVersion.GetInt32() == 1
               && root.TryGetProperty("packageKind", out var packageKind)
               && string.Equals(packageKind.GetString(), "portable-zip", StringComparison.Ordinal);
    }
    catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
    {
        return false;
    }
}

static string? ParseVersion(string path)
{
    var directoryName = Path.GetFileName(path);
    if (!directoryName.StartsWith("app-v", StringComparison.Ordinal))
        return null;

    var versionText = directoryName[5..];
    var separatorIndex = versionText.LastIndexOf('-');
    if (separatorIndex >= 0 && int.TryParse(versionText[(separatorIndex + 1)..], out _))
        versionText = versionText[..separatorIndex];

    return SecRandom.Services.Updates.ReleaseVersion.TrySplit(versionText, out _, out _) ? versionText : null;
}

static int ParseBuildNumber(string path)
{
    var directoryName = Path.GetFileName(path);
    var separatorIndex = directoryName.LastIndexOf('-');
    return separatorIndex >= 0 && int.TryParse(directoryName[(separatorIndex + 1)..], out var build)
        ? build
        : 0;
}
}
