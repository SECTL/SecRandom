using System.Reflection;
using SecRandom.Shared;

namespace SecRandom.Core.Tests;

public sealed class DesktopDataRootPreparationTests : IDisposable
{
    private readonly string _packageRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "desktop-root-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void PrepareDesktopDataRoot_UsesWritableInstalledPackageDirectoryAndLocksTheRoot()
    {
        ResetDataRootForTests();

        var result = (Utils.DesktopDataRootPreparationResult)GetUtilsMethod("PrepareDesktopDataRoot", typeof(string), typeof(bool))
            .Invoke(null, [_packageRoot, false])!;

        var expectedDataRoot = Path.Combine(Path.GetFullPath(_packageRoot), "data");
        Assert.False(result.IsPortablePackage);
        Assert.True(result.IsWritable);
        Assert.Equal(expectedDataRoot, result.DataRoot);
        Assert.Equal(expectedDataRoot, Utils.DataRoot);

        var exception = Assert.Throws<TargetInvocationException>(() => ConfigureDataRootForTests(Path.Combine(_packageRoot, "other")));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_packageRoot))
            Directory.Delete(_packageRoot, recursive: true);
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
