using SecRandom.Shared.DataRoot;

namespace SecRandom.Core.Tests.DataRoot;

public sealed class DataPathReferenceTests
{
    [Fact]
    public void ToStored_RewritesPathsInsideTheRootAsRelative()
    {
        using var env = new DataRootTestEnvironment();
        var root = env.CreateDirectory("data");
        var image = env.WriteFile("data/images/student.png", "x");

        var stored = DataPathReference.ToStored(image, root);

        Assert.Equal("images/student.png", stored);
        Assert.Equal(image, DataPathReference.Resolve(stored, root));
    }

    [Fact]
    public void ToStored_KeepsExternalAbsolutePathsUntouched()
    {
        using var env = new DataRootTestEnvironment();
        var root = env.CreateDirectory("data");
        var external = env.WriteFile("pictures/photo.jpg", "x");

        var stored = DataPathReference.ToStored(external, root);

        Assert.Equal(DataRootPaths.Normalize(external), DataRootPaths.Normalize(stored));
        Assert.True(Path.IsPathRooted(stored));
    }

    [Fact]
    public void Resolve_LeavesAbsoluteAndUrlValuesAlone()
    {
        using var env = new DataRootTestEnvironment();
        var root = env.CreateDirectory("data");
        var absolute = env.PathOf("elsewhere", "a.png");

        Assert.False(DataPathReference.IsStoredRelative(absolute));
        Assert.Equal(absolute, DataPathReference.Resolve(absolute, root));
        Assert.False(DataPathReference.IsStoredRelative("https://example.com/a.png"));
    }
}