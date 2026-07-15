using System.Diagnostics;
using System.Text.Json;

const string packageRootEnvironmentVariable = "SECRANDOM_PACKAGE_ROOT";
var root = Path.GetFullPath(AppContext.BaseDirectory);
var executableName = OperatingSystem.IsWindows() ? "SecRandom.Desktop.exe" : "SecRandom.Desktop";

var installation = Directory.EnumerateDirectories(root, "app-*")
    .Where(path => IsValidInstallation(path, executableName))
    .OrderByDescending(path => File.Exists(Path.Combine(path, ".current")))
    .ThenByDescending(ParseVersion)
    .ThenByDescending(ParseBuildNumber)
    .FirstOrDefault();

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

static bool IsValidInstallation(string path, string executableName)
{
    if (File.Exists(Path.Combine(path, ".partial")) || !File.Exists(Path.Combine(path, executableName)))
        return false;

    var markerPath = Path.Combine(path, "SecRandom.package.json");
    if (!File.Exists(markerPath))
        return false;

    try
    {
        using var marker = JsonDocument.Parse(File.ReadAllText(markerPath));
        var root = marker.RootElement;
        return root.TryGetProperty("product", out var product)
               && string.Equals(product.GetString(), "SecRandom", StringComparison.Ordinal)
               && root.TryGetProperty("schemaVersion", out var schemaVersion)
               && schemaVersion.GetInt32() == 1
               && root.TryGetProperty("packageKind", out var packageKind)
               && string.Equals(packageKind.GetString(), "portable-zip", StringComparison.Ordinal);
    }
    catch (JsonException)
    {
        return false;
    }
}

static Version ParseVersion(string path)
{
    var directoryName = Path.GetFileName(path);
    if (!directoryName.StartsWith("app-v", StringComparison.Ordinal))
        return new Version();

    var versionText = directoryName[5..];
    var separatorIndex = versionText.LastIndexOf('-');
    if (separatorIndex >= 0 && int.TryParse(versionText[(separatorIndex + 1)..], out _))
        versionText = versionText[..separatorIndex];

    var prereleaseIndex = versionText.IndexOf('-');
    if (prereleaseIndex >= 0)
        versionText = versionText[..prereleaseIndex];
    return Version.TryParse(versionText, out var version) ? version : new Version();
}

static int ParseBuildNumber(string path)
{
    var directoryName = Path.GetFileName(path);
    var separatorIndex = directoryName.LastIndexOf('-');
    return separatorIndex >= 0 && int.TryParse(directoryName[(separatorIndex + 1)..], out var build)
        ? build
        : 0;
}
