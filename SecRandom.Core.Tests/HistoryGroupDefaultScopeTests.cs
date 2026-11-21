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

/// <summary>
///     Write-side counterpart of the same decision: which group marker a new record carries. It is deliberately a
///     separate entry point, because the feature master switch only governs whether the selector may be shown — a
///     channel whose history is not group-scoped still writes its own configured default group. Collapsing the two
///     would let every mode pile into the default group as soon as the feature is turned off, which cannot be undone
///     by turning it back on.
/// </summary>
public sealed class HistoryRecordGroupWriteScopeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankConfiguration_WritesTheDefaultGroup(string? configured)
    {
        Assert.Equal(string.Empty, HistoryGroupScope.ResolveRecordGroup(configured, ["Class A"]));
    }

    [Fact]
    public void ExistingGroup_IsWrittenAndTrimmed()
    {
        Assert.Equal("Class A", HistoryGroupScope.ResolveRecordGroup("  Class A  ", ["Class A", "Class B"]));
    }

    [Fact]
    public void UnreadableCatalogue_KeepsTheConfiguredName()
    {
        // The opposite of the read side, on purpose: a name is only a marker, so keeping what the user configured is
        // the recoverable choice, while silently folding the records into the default group is irreversible.
        Assert.Equal("Class A", HistoryGroupScope.ResolveRecordGroup("Class A", null));
    }

    [Fact]
    public void DeletedGroup_WritesTheDefaultGroupInsteadOfAGhostName()
    {
        Assert.Equal(string.Empty, HistoryGroupScope.ResolveRecordGroup("Deleted", ["Class A"]));
    }

    [Fact]
    public void MatchingIsOrdinal()
    {
        Assert.Equal(string.Empty, HistoryGroupScope.ResolveRecordGroup("class a", ["Class A"]));
    }

    [Fact]
    public void TheFeatureSwitch_IsNotPartOfTheWriteDecision()
    {
        // Pinned by signature: adding an `enabled` parameter here would re-introduce exactly the collapse this type
        // exists to prevent (feature off ⇒ every mode writes the default group).
        var method = typeof(HistoryGroupScope).GetMethod(nameof(HistoryGroupScope.ResolveRecordGroup));

        Assert.NotNull(method);
        Assert.Equal(2, method!.GetParameters().Length);
    }
}
