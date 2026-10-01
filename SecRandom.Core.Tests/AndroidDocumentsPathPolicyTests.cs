using SecRandom.Android.Storage;

namespace SecRandom.Core.Tests;

public sealed class AndroidDocumentsPathPolicyTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SecRandom-provider-policy"));
    private static readonly string Data = Path.Combine(Root, "data");

    [Theory]
    [InlineData("config")]
    [InlineData("config/security")]
    [InlineData("config/security/credentials.json")]
    [InlineData("config/voice")]
    [InlineData("config/voice/omnitts-keys.json")]
    public void DestructiveOperations_RejectProtectedTreesAndAncestors(string relativePath)
    {
        Assert.False(DocumentsPathPolicy.CanMutate(CanonicalDataPath(relativePath), Root, Data));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("data/..")]
    [InlineData("data")]
    [InlineData("data/config/..")]
    [InlineData("data/../data")]
    public void DestructiveOperations_RejectCanonicalRootAliases(string relativePath)
    {
        var canonicalPath = Path.GetFullPath(Path.Combine(Root, relativePath));
        Assert.False(DocumentsPathPolicy.CanMutate(canonicalPath, Root, Data));
    }

    [Theory]
    [InlineData("config/security")]
    [InlineData("config/voice")]
    [InlineData("config/security/new.json")]
    [InlineData("config/voice/reference.wav")]
    [InlineData("audio/../config/voice/reference.wav")]
    public void Destinations_RejectProtectedCanonicalPaths(string relativePath)
    {
        Assert.False(DocumentsPathPolicy.IsAccessible(CanonicalDataPath(relativePath), Root, Data));
    }

    [Theory]
    [InlineData("config")]
    [InlineData("config/settings.json")]
    [InlineData("config/security-old")]
    [InlineData("config/voice-cache")]
    [InlineData("profiles")]
    [InlineData("profiles/class.json")]
    [InlineData("audio/music")]
    [InlineData("audio/voice/record.wav")]
    public void OrdinaryDirectoriesAndFiles_RemainAccessible(string relativePath)
    {
        Assert.True(DocumentsPathPolicy.IsAccessible(CanonicalDataPath(relativePath), Root, Data));
        if (relativePath != "config")
            Assert.True(DocumentsPathPolicy.CanMutate(CanonicalDataPath(relativePath), Root, Data));
    }

    [Fact]
    public void Roots_RemainBrowsableButOutsidePathsAreRejected()
    {
        Assert.True(DocumentsPathPolicy.IsAccessible(Root, Root, Data));
        Assert.True(DocumentsPathPolicy.IsAccessible(Data, Root, Data));
        Assert.False(DocumentsPathPolicy.IsAccessible(Path.Combine(Root, "data-other", "file"), Root, Data));
        Assert.False(DocumentsPathPolicy.IsAccessible(Path.Combine(Root, "other"), Root, Data));
        Assert.False(DocumentsPathPolicy.IsAccessible(CanonicalDataPath("../outside"), Root, Data));
    }

    private static string CanonicalDataPath(string relativePath) =>
        Path.GetFullPath(Path.Combine(Data, relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
