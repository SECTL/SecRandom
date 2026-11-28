using System.IO.Compression;
using SecRandom.Services.Updates;
using SecRandom.Shared;
using SecRandom.Shared.Updates;

namespace SecRandom.Core.Tests;

public sealed class PortableUpdateDeploymentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SecRandom", "update-tests", Guid.NewGuid().ToString("N"));
    private readonly UpdateArtifact _artifact = new() { RuntimeKind = "self-contained" };

    [Fact]
    public async Task DeploysBesideExistingVersionAndRetriesWithoutReplacingData()
    {
        Directory.CreateDirectory(_root);
        var data = Path.Combine(_root, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings"), "keep");
        var zip = CreatePackage(includeLauncher: true);

        await UpdateCenterService.DeployPortablePackageAsync(zip, _root, "v3.3.0", "win-x64", _artifact, TestContext.Current.CancellationToken);
        await UpdateCenterService.DeployPortablePackageAsync(zip, _root, "v3.3.0", "win-x64", _artifact, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(_root, "app-v3.3.0-0", DesktopName)));
        Assert.True(File.Exists(Path.Combine(_root, "app-v3.3.0-1", DesktopName)));
        Assert.False(File.Exists(Path.Combine(_root, "app-v3.3.0-1", ".partial")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(data, "settings")));
        Assert.Equal(Path.Combine(_root, "app-v3.3.0-1"), PortableInstallationSelector.Select(_root, DesktopName));
        Assert.Empty(Directory.GetDirectories(_root, ".update-staging-*"));
    }

    [Fact]
    public async Task IncompletePackageDoesNotActivateOrReplaceLauncher()
    {
        Directory.CreateDirectory(_root);
        var launcher = Path.Combine(_root, Utils.LauncherFileName);
        File.WriteAllText(launcher, "old");
        var zip = CreatePackage(includeLauncher: false);

        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateCenterService.DeployPortablePackageAsync(
            zip, _root, "v3.3.0", "win-x64", _artifact, TestContext.Current.CancellationToken));

        Assert.Equal("old", File.ReadAllText(launcher));
        Assert.Empty(Directory.GetDirectories(_root, "app-*"));
        Assert.Empty(Directory.GetDirectories(_root, ".update-staging-*"));
    }

    private static string DesktopName => OperatingSystem.IsWindows() ? "SecRandom.Desktop.exe" : "SecRandom.Desktop";

    private string CreatePackage(bool includeLauncher)
    {
        var path = Path.Combine(_root, "package.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add("app-v3.3.0-0/SecRandom.package.json", """{"schemaVersion":1,"product":"SecRandom","rid":"win-x64","packageKind":"portable-zip","runtimeKind":"self-contained"}""");
        Add("app-v3.3.0-0/" + DesktopName, "desktop");
        if (includeLauncher) Add(Utils.LauncherFileName, "launcher");
        return path;

        void Add(string name, string contents)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(contents);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
