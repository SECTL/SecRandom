using SecRandom.Core.Services.Profiles;

namespace SecRandom.Core.Tests;

/// <summary>
///     Resolution of the persisted "default record group" that the roll-call, lottery and Quick Draw surfaces
///     start from. The configuration value is deliberately tri-state-free: only the empty string is stored, and
///     it means the default group (ungrouped records) rather than "no group scope". The helper is the single
///     place that decides when a scope becomes <see langword="null" /> (feature off, group catalogue unreadable)
///     versus the default group, because collapsing those two is exactly the unfairness the group axis avoids.
/// </summary>
public sealed class HistoryGroupDefaultScopeTests
{
    [Fact]
    public void FeatureOff_MeansNoGroupScopeAtAll()
    {
        var scope = HistoryGroupScope.Resolve(false, "Class A", ["Class A"]);

        Assert.Null(scope.Group);
        Assert.False(scope.ConfiguredGroupMissing);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankConfiguration_MeansTheDefaultGroup(string? configured)
    {
        var scope = HistoryGroupScope.Resolve(true, configured, ["Class A"]);

        Assert.Equal(string.Empty, scope.Group);
        Assert.False(scope.ConfiguredGroupMissing);
    }

    [Fact]
    public void ConfiguredGroupThatStillExists_IsUsed()
    {
        var scope = HistoryGroupScope.Resolve(true, "Class A", ["Class A", "Class B"]);

        Assert.Equal("Class A", scope.Group);
        Assert.False(scope.ConfiguredGroupMissing);
    }

    [Fact]
    public void ConfiguredGroupIsTrimmedBeforeMatching()
    {
        var scope = HistoryGroupScope.Resolve(true, "  Class A  ", ["Class A"]);

        Assert.Equal("Class A", scope.Group);
        Assert.False(scope.ConfiguredGroupMissing);
    }

    [Fact]
    public void DeletedGroup_FallsBackToTheDefaultGroupAndReportsIt()
    {
        // Deleting a group moves its records back into the default group, so the fallback keeps the
        // history the user was actually using instead of silently widening to every group.
        var scope = HistoryGroupScope.Resolve(true, "Deleted", ["Class A"]);

        Assert.Equal(string.Empty, scope.Group);
        Assert.True(scope.ConfiguredGroupMissing);
    }

    [Fact]
    public void GroupMatchingIsOrdinal_SoCaseOnlyDifferencesAreNotSilentlyAccepted()
    {
        var scope = HistoryGroupScope.Resolve(true, "class a", ["Class A"]);

        Assert.Equal(string.Empty, scope.Group);
        Assert.True(scope.ConfiguredGroupMissing);
    }

    [Fact]
    public void UnreadableCatalogue_DoesNotNarrowTheWeightPool()
    {
        // A failed catalogue read must not be treated as "the group was deleted": narrowing weights to a
        // group we could not verify would be worse than this draw counting every record.
        var scope = HistoryGroupScope.Resolve(true, "Class A", null);

        Assert.Null(scope.Group);
        Assert.False(scope.ConfiguredGroupMissing);
    }
}
