using SecRandom.Helpers;

namespace SecRandom.Core.Tests;

public sealed class AvatarInitialResolverTests
{
    [Fact]
    public void Resolve_PrefersNameOverId()
    {
        var initial = AvatarInitialResolver.Resolve(" Alice ", "2026001");

        Assert.Equal("A", initial);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_UsesIdWhenNameIsMissing(string? name)
    {
        var initial = AvatarInitialResolver.Resolve(name, " 2026001 ");

        Assert.Equal("2", initial);
    }

    [Fact]
    public void Resolve_UsesFallbackWhenNameAndIdAreMissing()
    {
        var initial = AvatarInitialResolver.Resolve(" ", "\t");

        Assert.Equal("?", initial);
    }
}
