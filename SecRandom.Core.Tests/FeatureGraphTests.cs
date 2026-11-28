using SecRandom.Core.Services.FeatureConfiguration;

namespace SecRandom.Core.Tests;

public sealed class FeatureGraphTests
{
    private static FeatureGraph CreateGraph() => new([
        new("core", [], Required: true),
        new("floating", ["core"]),
        new("draw", ["core"]),
        new("quick", ["floating", "draw"]),
        new("other", ["core"])
    ]);

    [Fact]
    public void EnablingIncludesTransitiveDependenciesAndRequiredFeatures()
    {
        var result = CreateGraph().Enable([], "quick");
        Assert.True(result.SetEquals(["core", "floating", "draw", "quick"]));
    }

    [Fact]
    public void DisablingRemovesDependentsButKeepsUnrelatedFeatures()
    {
        var result = CreateGraph().Disable(["quick", "other"], "floating");
        Assert.True(result.SetEquals(["core", "draw", "other"]));
    }

    [Fact]
    public void RequiredFeatureAndItsDependenciesCannotBeDisabled()
    {
        var graph = new FeatureGraph([new("base", []), new("core", ["base"], Required: true)]);
        Assert.Throws<InvalidOperationException>(() => graph.Disable([], "base"));
    }

    [Fact]
    public void InvalidDefinitionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new FeatureGraph([new("a", ["missing"])]));
        Assert.Throws<ArgumentException>(() => new FeatureGraph([new("a", ["b"]), new("b", ["a"])]));
        Assert.Throws<ArgumentException>(() => new FeatureGraph([new("a", []), new("a", [])]));
    }

    [Fact]
    public void LegacyModesResolveStartupFeatures()
    {
        var required = StartupFeatureCatalog.Graph.Features.Where(feature => feature.Required).Select(feature => feature.Id).ToHashSet(StringComparer.Ordinal);
        var simple = StartupFeatureCatalog.ResolveLegacyMode(true);
        Assert.True(required.IsSubsetOf(simple));
        Assert.True(simple.SetEquals(required.Concat([StartupFeatureCatalog.QuickDraw, StartupFeatureCatalog.ControlNode,
            StartupFeatureCatalog.Integrations, StartupFeatureCatalog.AutomaticBackup, StartupFeatureCatalog.AutomaticUpdates])));

        var full = StartupFeatureCatalog.ResolveLegacyMode(false);
        Assert.True(full.SetEquals(StartupFeatureCatalog.Graph.Features.Select(feature => feature.Id)));
    }

    [Fact]
    public void DependencyDefinitionsAreCopiedAtRegistration()
    {
        var dependencies = new List<string> { "base" };
        var graph = new FeatureGraph([new("base", []), new("child", dependencies)]);
        dependencies.Clear();
        Assert.True(graph.Resolve(["child"]).SetEquals(["base", "child"]));
    }

    [Fact]
    public void FullscreenRequiresNotificationsAndClosesWithThem()
    {
        var graph = StartupFeatureCatalog.Graph;
        var enabled = graph.Enable([], StartupFeatureCatalog.FullscreenNotifications);
        Assert.Contains(StartupFeatureCatalog.Notifications, enabled);
        Assert.DoesNotContain(StartupFeatureCatalog.FullscreenNotifications,
            graph.Disable(enabled, StartupFeatureCatalog.Notifications));
    }
}
