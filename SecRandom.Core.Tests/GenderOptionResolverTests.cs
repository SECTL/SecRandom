using SecRandom.Core.Services.Ipc;

namespace SecRandom.Core.Tests;

public sealed class GenderOptionResolverTests
{
    [Theory]
    [InlineData("male")]
    [InlineData("M")]
    [InlineData("Male")]
    [InlineData("男")]
    public void Resolve_MatchesLatinAndChineseMaleValues(string value)
    {
        Assert.Equal("男", GenderOptionResolver.Resolve(value, ["男", "女"]));
    }

    [Theory]
    [InlineData("female")]
    [InlineData("F")]
    [InlineData("Female")]
    [InlineData("女")]
    public void Resolve_MatchesLatinAndChineseFemaleValues(string value)
    {
        Assert.Equal("女", GenderOptionResolver.Resolve(value, ["男", "女"]));
    }

    [Fact]
    public void Resolve_MatchesInitalsWhenTheRosterUsesLetterValues()
    {
        // A roster that stores "M"/"F" must still be reachable through the documented male/female tokens;
        // the router used to only accept the Chinese labels here.
        Assert.Equal("M", GenderOptionResolver.Resolve("male", ["M", "F"]));
        Assert.Equal("F", GenderOptionResolver.Resolve("female", ["M", "F"]));
        Assert.Equal("M", GenderOptionResolver.Resolve("male", ["F", "M"]));
    }

    [Fact]
    public void Resolve_ReturnsTheFirstOptionForTheAllToken()
    {
        Assert.Equal("全部", GenderOptionResolver.Resolve("all", ["全部", "男", "女"]));
        Assert.Equal("全部", GenderOptionResolver.Resolve("ALL", ["全部", "男", "女"]));
        Assert.Null(GenderOptionResolver.Resolve("all", []));
    }

    [Fact]
    public void Resolve_PrefersALiteralMatchOverTheAliasTable()
    {
        // A class that literally uses the word "male" keeps working, and custom values stay addressable.
        Assert.Equal("male", GenderOptionResolver.Resolve("MALE", ["male", "female"]));
        Assert.Equal("甲", GenderOptionResolver.Resolve("甲", ["甲", "乙"]));
    }

    [Fact]
    public void Resolve_ReturnsNullForUnknownOrMissingValues()
    {
        Assert.Null(GenderOptionResolver.Resolve("unknown", ["男", "女"]));
        Assert.Null(GenderOptionResolver.Resolve(null, ["男", "女"]));
        Assert.Null(GenderOptionResolver.Resolve("   ", ["男", "女"]));
    }
}
