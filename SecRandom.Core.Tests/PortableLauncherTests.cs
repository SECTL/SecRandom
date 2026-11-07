using System.Diagnostics;
using System.Reflection;
using SecRandom.Services.Desktop;
using SecRandom.Shared;

namespace SecRandom.Core.Tests;

public sealed class PortableLauncherTests : IDisposable
{
    private const string PortableMarker =
        """{"schemaVersion":1,"product":"SecRandom","rid":"win-x64","packageKind":"portable-zip","runtimeKind":"self-contained"}""";

    private readonly string _packageRoot =
        Path.Combine(Path.GetTempPath(), "SecRandom", "portable-launcher-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TryGetPortableLauncherPath_ResolvesLauncherOutsideActivatedPayload()
    {
        var appDirectory = CreatePortablePackage(_packageRoot);

        Assert.True(TryResolvePortableLauncher(_packageRoot, appDirectory, out var launcherPath));
        Assert.Equal(
            Path.Combine(Path.GetFullPath(_packageRoot), Utils.LauncherFileName),
            launcherPath);
    }

    [Fact]
    public void TryGetPortableLauncherPath_RejectsInstalledPackageWithoutSeparateLauncher()
    {
        var appDirectory = CreatePortablePackage(_packageRoot);

        Assert.False(TryResolvePortableLauncher(appDirectory, appDirectory, out _));
    }

    [Fact]
    public void TryGetPortableLauncherPath_RequiresPortableZipMarker()
    {
        var appDirectory = Directory.CreateDirectory(Path.Combine(_packageRoot, "app-v3.2.0-0")).FullName;
        File.WriteAllText(
            Path.Combine(appDirectory, "SecRandom.package.json"),
            """{"schemaVersion":1,"product":"SecRandom","rid":"win-x64","packageKind":"windows-exe","runtimeKind":"self-contained"}""");
        File.WriteAllText(Path.Combine(_packageRoot, Utils.LauncherFileName), string.Empty);

        Assert.False(TryResolvePortableLauncher(_packageRoot, appDirectory, out _));
    }

    [Fact]
    public void TryGetPortableLauncherPath_RequiresLauncherAtPackageRoot()
    {
        var appDirectory = Directory.CreateDirectory(Path.Combine(_packageRoot, "app-v3.2.0-0")).FullName;
        File.WriteAllText(Path.Combine(appDirectory, "SecRandom.package.json"), PortableMarker);
        File.WriteAllText(Path.Combine(appDirectory, Utils.LauncherFileName), string.Empty);

        Assert.False(TryResolvePortableLauncher(_packageRoot, appDirectory, out _));
    }

    [Fact]
    public void IntegrationLaunchTarget_UsesPortableLauncherWhenResolved()
    {
        var appDirectory = CreatePortablePackage(_packageRoot);
        Assert.True(TryResolvePortableLauncher(_packageRoot, appDirectory, out var launcherPath));

        ProcessStartInfo? startInfo = CreateIntegrationStartInfo(launcherPath, ["--url", "%1"]);

        Assert.NotNull(startInfo);
        Assert.Equal(launcherPath, startInfo!.FileName);
        Assert.Equal(["--url", "%1"], startInfo.ArgumentList);
    }

    public void Dispose()
    {
        if (Directory.Exists(_packageRoot))
            Directory.Delete(_packageRoot, recursive: true);
    }

    private static string CreatePortablePackage(string packageRoot)
    {
        var appDirectory = Directory.CreateDirectory(Path.Combine(packageRoot, "app-v3.2.0-0")).FullName;
        File.WriteAllText(Path.Combine(appDirectory, "SecRandom.package.json"), PortableMarker);
        File.WriteAllText(Path.Combine(packageRoot, Utils.LauncherFileName), string.Empty);
        return appDirectory;
    }

    private static bool TryResolvePortableLauncher(
        string? packageRoot,
        string? appBaseDirectory,
        out string launcherPath)
    {
        object?[] arguments = [packageRoot, appBaseDirectory, null];
        var resolved = (bool)GetUtilsMethod(
                "TryGetPortableLauncherPath",
                typeof(string),
                typeof(string),
                typeof(string).MakeByRefType())
            .Invoke(null, arguments)!;
        launcherPath = resolved ? (string)arguments[2]! : string.Empty;
        return resolved;
    }

    private static ProcessStartInfo? CreateIntegrationStartInfo(string? launcherPath, string[] arguments)
    {
        return (ProcessStartInfo?)typeof(DesktopIntegrationService)
            .GetMethod(
                "CreateIntegrationStartInfo",
                BindingFlags.Static | BindingFlags.NonPublic,
                [typeof(string), typeof(IEnumerable<string>)])
            ?.Invoke(null, [launcherPath, arguments]);
    }

    private static MethodInfo GetUtilsMethod(string name, params Type[] parameterTypes)
    {
        return typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic, parameterTypes)
               ?? throw new InvalidOperationException($"Utils.{name} was not found.");
    }
}
