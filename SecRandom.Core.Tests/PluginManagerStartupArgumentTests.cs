using SecRandom.Services.Plugins;

namespace SecRandom.Core.Tests;

public sealed class PluginManagerStartupArgumentTests
{
    [Theory]
    [InlineData("--epp")]
    [InlineData("--externalPluginPath")]
    public void ParseExternalPluginDirectories_ReadsBothFlagNames(string flag)
    {
        var parsed = PluginManager.ParseExternalPluginDirectories([flag, "/tmp/plugins/dev"]);

        Assert.Single(parsed);
        Assert.Equal("/tmp/plugins/dev", parsed[0]);
    }

    [Fact]
    public void ParseExternalPluginDirectories_SupportsRepeatedFlags()
    {
        var parsed = PluginManager.ParseExternalPluginDirectories(
            ["--epp", "/tmp/plugins/a", "--externalPluginPath", "/tmp/plugins/b"]);

        Assert.Equal(2, parsed.Count);
        Assert.Contains("/tmp/plugins/a", parsed);
        Assert.Contains("/tmp/plugins/b", parsed);
    }

    [Fact]
    public void ParseExternalPluginDirectories_SkipsMissingAndBlankValues()
    {
        var parsed = PluginManager.ParseExternalPluginDirectories(
            ["--epp", "", "--externalPluginPath", "   ", "plain-arg"]);

        Assert.Empty(parsed);
    }

    [Fact]
    public void ParseExternalPluginDirectories_IgnoresUnrelatedArguments()
    {
        var parsed = PluginManager.ParseExternalPluginDirectories(
            ["--epp", "/tmp/plugins/dev", "--other", "value"]);

        Assert.Single(parsed);
        Assert.Equal("/tmp/plugins/dev", parsed[0]);
    }
}
