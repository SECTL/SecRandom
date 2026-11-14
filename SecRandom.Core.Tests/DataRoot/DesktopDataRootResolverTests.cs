using SecRandom.Shared.DataRoot;

namespace SecRandom.Core.Tests.DataRoot;

public sealed class DesktopDataRootResolverTests
{
    [Fact]
    public void FreshInstalled_WithWritableInstallDirectory_UsesStandardRoot()
    {
        using var env = new DataRootTestEnvironment();
        var appDirectory = env.CreateDirectory("install");

        var resolution = DesktopDataRootResolver.Resolve(Options(env, appDirectory));

        Assert.False(resolution.IsPortablePackage);
        Assert.Equal(DataRootMode.Standard, resolution.Mode);
        Assert.Equal(DataRootSource.ModeDefault, resolution.Source);
        Assert.Equal(DataRootPaths.Normalize(env.PathOf("standard-data")), resolution.DataRoot);
        Assert.False(resolution.NeedsMigrationNotice);
    }

    [Fact]
    public void Installed_WithLegacyInstallDirectoryData_ClaimsItAndAsksForAMigration()
    {
        using var env = new DataRootTestEnvironment();
        var appDirectory = env.CreateDirectory("install");
        env.WriteFile("install/data/config/settings.json", "{}");

        var resolution = DesktopDataRootResolver.Resolve(Options(env, appDirectory));

        Assert.Equal(DataRootMode.Package, resolution.Mode);
        Assert.Equal(DataRootSource.Legacy, resolution.Source);
        Assert.Equal(DataRootPaths.Normalize(env.PathOf("install", "data")), resolution.DataRoot);
        Assert.True(resolution.NeedsMigrationNotice);
    }

    [Fact]
    public void Installed_WithStandardData_PrefersStandardWithoutNotice()
    {
        using var env = new DataRootTestEnvironment();
        var appDirectory = env.CreateDirectory("install");
        env.WriteFile("install/data/config/settings.json", "{}");
        env.WriteFile("standard-data/config/settings.json", "{}");

        var resolution = DesktopDataRootResolver.Resolve(Options(env, appDirectory));

        Assert.Equal(DataRootMode.Standard, resolution.Mode);
        Assert.Equal(DataRootSource.ModeDefault, resolution.Source);
        Assert.Equal(DataRootPaths.Normalize(env.PathOf("standard-data")), resolution.DataRoot);
        Assert.False(resolution.NeedsMigrationNotice);
    }

    [Fact]
    public void Portable_KeepsDataBesideThePayload()
    {
        using var env = new DataRootTestEnvironment();
        var packageRoot = env.CreateDirectory("portable");
        var appDirectory = env.CreateDirectory("portable", "app-3.0.0");
        env.WritePayloadMarker(appDirectory);
        env.WriteFile("portable/data/config/settings.json", "{}");

        var resolution = DesktopDataRootResolver.Resolve(Options(
            env,
            appDirectory,
            packageRoot: packageRoot));

        Assert.True(resolution.IsPortablePackage);
        Assert.Equal(packageRoot, resolution.PackageRoot);
        Assert.Equal(DataRootMode.Package, resolution.Mode);
        Assert.Equal(DataRootSource.ModeDefault, resolution.Source);
        Assert.Equal(DataRootPaths.Normalize(env.PathOf("portable", "data")), resolution.DataRoot);
    }

    [Fact]
    public void Portable_WithPayloadLocalData_RepairsItOutOfTheVersionDirectory()
    {
        using var env = new DataRootTestEnvironment();
        var packageRoot = env.CreateDirectory("portable");
        var appDirectory = env.CreateDirectory("portable", "app-3.0.0");
        env.WritePayloadMarker(appDirectory);
        env.WriteFile("portable/app-3.0.0/data/config/settings.json", "{}");

        var resolution = DesktopDataRootResolver.Resolve(Options(
            env,
            appDirectory,
            packageRoot: packageRoot));

        Assert.Equal(DataRootSource.RepairedPayload, resolution.Source);
        Assert.Equal(
            DataRootPaths.Normalize(env.PathOf("portable", "app-3.0.0", "data")),
            resolution.PayloadDataRootToRepair);
        Assert.Equal(DataRootPaths.Normalize(env.PathOf("portable", "data")), resolution.DataRoot);
    }

    [Fact]
    public void CommandLineOverride_WinsAndStaysOneShot()
    {
        using var env = new DataRootTestEnvironment();
        var appDirectory = env.CreateDirectory("install");
        env.WriteFile("install/data/config/settings.json", "{}");
        var customRoot = env.PathOf("custom-root");

        var options = Options(env, appDirectory);
        options = options with { CommandLineDataRoot = customRoot };
        var resolution = DesktopDataRootResolver.Resolve(options);

        Assert.Equal(DataRootMode.Custom, resolution.Mode);
        Assert.Equal(DataRootSource.CommandLine, resolution.Source);
        Assert.Equal(DataRootPaths.Normalize(customRoot), resolution.DataRoot);
        Assert.Contains(env.PathOf("install", "data"),
            resolution.OtherDataRoots.Select(DataRootPaths.Normalize));
    }

    [Fact]
    public void Anchor_IsHonouredOverTheModeDefault()
    {
        using var env = new DataRootTestEnvironment();
        var appDirectory = env.CreateDirectory("install");
        var options = Options(env, appDirectory);
        var customRoot = env.CreateDirectory("moved-data");
        DataRootAnchorStore.Write(options.AnchorPath!, new DataRootAnchor
        {
            Mode = DataRootModeExtensions.ToStorageValue(DataRootMode.Custom),
            DataRoot = customRoot
        });

        var resolution = DesktopDataRootResolver.Resolve(options);

        Assert.Equal(DataRootSource.Anchor, resolution.Source);
        Assert.Equal(DataRootMode.Custom, resolution.Mode);
        Assert.Equal(DataRootPaths.Normalize(customRoot), resolution.DataRoot);
    }

    private static DesktopDataRootOptions Options(
        DataRootTestEnvironment env,
        string appDirectory,
        string? packageRoot = null)
    {
        return new DesktopDataRootOptions
        {
            AppDirectory = appDirectory,
            PackageRoot = packageRoot ?? appDirectory,
            StandardDataRoot = env.PathOf("standard-data"),
            StandardAnchorDirectory = env.PathOf("anchor"),
            AnchorPath = env.PathOf("anchor", DataRootPaths.AnchorFileName),
            WriteAnchor = false
        };
    }
}