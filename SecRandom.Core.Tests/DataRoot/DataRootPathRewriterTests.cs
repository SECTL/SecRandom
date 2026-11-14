using SecRandom.Core.Services.DataRoot;

namespace SecRandom.Core.Tests.DataRoot;

public sealed class DataRootPathRewriterTests
{
    [Fact]
    public void RewriteStaging_MakesRootedReferencesRelativeAndKeepsExternalOnes()
    {
        using var env = new DataRootTestEnvironment();
        var staging = env.CreateDirectory("staging");
        var oldRoot = env.PathOf("old-data");
        var newRoot = env.PathOf("new-data");
        var internalImage = env.PathOf("old-data", "images", "a.png");
        var externalImage = env.PathOf("pictures", "outside.png");

        env.WriteFile("staging/list/students.json",
            "{\"members\":[{\"ImagePath\":\"" + internalImage.Replace("\\", "\\\\") + "\"},"
            + "{\"ImagePath\":\"" + externalImage.Replace("\\", "\\\\") + "\"}]}");

        DataRootPathRewriter.RewriteStaging(staging, oldRoot, newRoot);

        var rewritten = env.ReadFile("staging/list/students.json");
        Assert.Contains("\"ImagePath\":\"images/a.png\"", rewritten, StringComparison.Ordinal);
        Assert.Contains(externalImage.Replace("\\", "\\\\"), rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void RewriteStaging_LeavesUnrelatedFilesByteIdentical()
    {
        using var env = new DataRootTestEnvironment();
        var staging = env.CreateDirectory("staging");
        const string original = "{\"unrelated\":\"旧根/" + "not-a-path\"}";
        env.WriteFile("staging/config/settings.json", original);

        DataRootPathRewriter.RewriteStaging(
            staging,
            env.PathOf("old-data"),
            env.PathOf("new-data"));

        Assert.Equal(original, env.ReadFile("staging/config/settings.json"));
    }
}