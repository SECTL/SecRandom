using System.Reflection;
using SecRandom.Shared;
using SecRandom.Shared.DataRoot;

namespace SecRandom.Core.Tests;

public sealed class DesktopDataRootPreparationTests : IDisposable
{
    private readonly string _tempRoot =
        Path.Combine(Path.GetTempPath(), "SecRandom", "desktop-root-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void PrepareDesktopDataRoot_InstalledWritablePackageDirectory_UsesTheStandardRootAndLocksIt()
    {
        ResetDataRootForTests();
        var packageRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "install")).FullName;
        var standardRoot = Path.Combine(_tempRoot, "standard-data");
        var anchorPath = Path.Combine(_tempRoot, "anchor", "data-root.json");

        var result = Utils.PrepareDesktopDataRoot(new DesktopDataRootOptions
        {
            AppDirectory = packageRoot,
            PackageRoot = packageRoot,
            StandardDataRoot = standardRoot,
            AnchorPath = anchorPath,
            IsDevelopmentBuild = false
        });

        var expectedDataRoot = DataRootPaths.Normalize(standardRoot);
        Assert.False(result.IsPortablePackage);
        Assert.True(result.IsWritable);
        Assert.Equal(DataRootMode.Standard, result.Mode);
        Assert.Equal(DataRootSource.ModeDefault, result.Source);
        Assert.Equal(expectedDataRoot, result.DataRoot);
        Assert.Equal(expectedDataRoot, Utils.DataRoot);
        Assert.True(File.Exists(anchorPath));
        Assert.Equal(expectedDataRoot, DataRootAnchorStore.Read(anchorPath).Anchor!.DataRoot);

        var exception = Assert.Throws<TargetInvocationException>(
            () => ConfigureDataRootForTests(Path.Combine(_tempRoot, "other")));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public void PrepareDesktopDataRoot_InstalledLegacyData_ClaimsItAndRaisesTheMigrationNotice()
    {
        ResetDataRootForTests();
        var packageRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "install")).FullName;
        var legacyData = Directory.CreateDirectory(Path.Combine(packageRoot, "data")).FullName;
        File.WriteAllText(Path.Combine(legacyData, "settings.json"), "{}");

        var result = Utils.PrepareDesktopDataRoot(new DesktopDataRootOptions
        {
            AppDirectory = packageRoot,
            PackageRoot = packageRoot,
            StandardDataRoot = Path.Combine(_tempRoot, "standard-data"),
            AnchorPath = Path.Combine(_tempRoot, "anchor", "data-root.json"),
            IsDevelopmentBuild = false
        });

        Assert.Equal(DataRootMode.Package, result.Mode);
        Assert.Equal(DataRootSource.Legacy, result.Source);
        Assert.Equal(DataRootPaths.Normalize(legacyData), result.DataRoot);
        Assert.True(result.NeedsMigrationNotice);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Temp cleanup must never fail the run.
        }
    }

    private static void ConfigureDataRootForTests(string dataRoot)
    {
        GetUtilsMethod("ConfigureDataRoot", typeof(string)).Invoke(null, [dataRoot]);
    }

    private static void ResetDataRootForTests()
    {
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);
    }

    private static MethodInfo GetUtilsMethod(string name, params Type[] parameterTypes)
    {
        return typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic, parameterTypes)
               ?? throw new InvalidOperationException($"Utils.{name} was not found.");
    }
}