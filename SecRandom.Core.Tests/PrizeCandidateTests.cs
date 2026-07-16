using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public sealed class PrizeCandidateTests
{
    [Theory]
    [InlineData("001", "", true)]
    [InlineData("", "Gift", true)]
    [InlineData("", "", false)]
    [InlineData(" ", "\t", false)]
    public void IsCandidate_RequiresAtLeastOnePrizeNumberOrName(string id, string name, bool expected)
    {
        var prize = new Prize { Id = id, Name = name };

        Assert.Equal(expected, prize.IsCandidate);
    }

    [Fact]
    public void IsCandidate_ExcludesDisabledPrize()
    {
        var prize = new Prize { Id = "001", Exists = false };

        Assert.False(prize.IsCandidate);
    }
}
