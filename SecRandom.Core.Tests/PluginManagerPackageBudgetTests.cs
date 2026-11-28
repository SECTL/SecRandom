using System.IO.Compression;
using SecRandom.Services.Plugins;

namespace SecRandom.Core.Tests;

public sealed class PluginManagerPackageBudgetTests
{
    [Fact]
    public void LocalPackage_StagesTheValidatedSnapshotAfterSourceChanges()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "source.srpx");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("manifest.yml").Open()))
                    writer.Write("id: test.plugin\nname: Test\nentranceAssembly: plugin.dll\napiVersion: 3.0.0\nversion: 1.0.0\nauthor: Test Author\n");
                using var payloadWriter = new StreamWriter(archive.CreateEntry("plugin.dll").Open());
                payloadWriter.Write("original");
            }

            var original = File.ReadAllBytes(path);
            var package = PluginManager.ReadLocalPackage(path);
            Assert.Equal("Test", package.Manifest.Name);
            File.WriteAllText(path, "changed");
            package.Stage(directory);

            Assert.Equal(original, File.ReadAllBytes(Path.Combine(directory, "test.plugin.srpx")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ValidatePackageBudget_RejectsTooManyEntries()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("manifest.yml");
            archive.CreateEntry("one.bin");
            archive.CreateEntry("two.bin");
        }

        stream.Position = 0;
        using var package = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.Throws<InvalidDataException>(() => PluginManager.ValidatePackageBudgetForTests(
            package,
            packageSize: 1,
            maxPackageBytes: 10,
            maxEntryCount: 2,
            maxManifestBytes: 100,
            maxEntryBytes: 100,
            maxTotalBytes: 100));
    }

    [Fact]
    public void ValidatePackageBudget_CountsManifestAndEntryBytesFromStreams()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = archive.CreateEntry("manifest.yml");
            using (var writer = new StreamWriter(manifest.Open()))
                writer.Write(new string('a', 11));

            var payload = archive.CreateEntry("plugin.dll");
            using var output = payload.Open();
            output.Write(new byte[10]);
        }

        stream.Position = 0;
        using var package = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.Throws<InvalidDataException>(() => PluginManager.ValidatePackageBudgetForTests(
            package,
            packageSize: 1,
            maxPackageBytes: 100,
            maxEntryCount: 10,
            maxManifestBytes: 10,
            maxEntryBytes: 100,
            maxTotalBytes: 100));
    }
}
