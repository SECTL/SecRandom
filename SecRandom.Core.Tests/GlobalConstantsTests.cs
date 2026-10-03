using System.Reflection;
using System.Reflection.Emit;
using SecRandom.Core;

namespace SecRandom.Core.Tests;

/// <summary>
///     Version metadata is compiled into the platform head assembly (root AssemblyInfo.cs plus the GitInfo
///     generator). Android has no managed entry point, so Assembly.GetEntryAssembly() is null there and the head
///     publishes its own assembly instead; without that hand-off the settings title bar rendered v0.0.0.0 because
///     the lookup fell back to this library, which carries no build metadata.
/// </summary>
public sealed class GlobalConstantsTests
{
    [Fact]
    public void PublishedHeadAssemblySuppliesTheDisplayedVersion()
    {
        var originalSource = GlobalConstants.VersionSource;
        try
        {
            GlobalConstants.SetVersionAssembly(
                CreateProbeAssembly("v3.0.0-beta.1+0123456789abcdef0123456789abcdef01234567"));

            Assert.Equal("v3.0.0-beta.1", GlobalConstants.Version);
            Assert.Equal("v3.0.0-beta.1", GlobalConstants.Tag);
            Assert.Equal("0123456", GlobalConstants.CommitHash);
            Assert.Equal("v3.0.0-beta.1-Nonomi-0123456(Unknown)", GlobalConstants.VersionLong);
        }
        finally
        {
            GlobalConstants.SetVersionAssembly(originalSource);
        }
    }

    [Fact]
    public void AssemblyWithoutInformationalVersionKeepsThePlaceholder()
    {
        var originalSource = GlobalConstants.VersionSource;
        try
        {
            GlobalConstants.SetVersionAssembly(CreateProbeAssembly(null));

            Assert.Equal("v0.0.0.0", GlobalConstants.Version);
            Assert.Equal("0.0.0.0", GlobalConstants.Tag);
            Assert.Equal("Unknown", GlobalConstants.Branch);
        }
        finally
        {
            GlobalConstants.SetVersionAssembly(originalSource);
        }
    }

    [Fact]
    public void RepublishingRestoresMetadataFromTheOriginalSource()
    {
        var originalSource = GlobalConstants.VersionSource;
        var originalVersion = GlobalConstants.Version;
        try
        {
            GlobalConstants.SetVersionAssembly(CreateProbeAssembly("v9.9.9+ffffffffffffffffffffffffffffffffffffffff"));
            Assert.Equal("v9.9.9", GlobalConstants.Version);

            GlobalConstants.SetVersionAssembly(originalSource);
            Assert.Equal(originalVersion, GlobalConstants.Version);
        }
        finally
        {
            GlobalConstants.SetVersionAssembly(originalSource);
        }
    }

    private static Assembly CreateProbeAssembly(string? informationalVersion)
    {
        var builder = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"SecRandom.VersionProbe.{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.Run);
        if (informationalVersion is not null)
        {
            var constructor = typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!;
            builder.SetCustomAttribute(new CustomAttributeBuilder(constructor, [informationalVersion]));
        }

        return builder;
    }
}
