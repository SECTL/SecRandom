using System.Text.Json;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Services.FeatureConfiguration;

namespace SecRandom.Core.Tests;

public sealed class FeatureProfileTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacySettingsKeepEveryRequiredCapability(bool simple)
    {
        var settings = new SimpleModeSettingsConfig { Enabled = simple };
        var selected = StartupFeatureCatalog.Resolve(settings);
        Assert.All(StartupFeatureCatalog.Graph.Features.Where(feature => feature.Required),
            feature => Assert.Contains(feature.Id, selected));
        var requiredCount = StartupFeatureCatalog.Graph.Features.Count(feature => feature.Required);
        Assert.Equal(requiredCount + (simple ? 5 : StartupFeatureCatalog.Graph.Features.Count - requiredCount), selected.Count);
    }

    [Fact]
    public void ProfileRoundTripsAndIgnoresUnknownFeaturesWithoutDeletingThem()
    {
        var settings = new SimpleModeSettingsConfig
        {
            SelectedProfileId = "user.one",
            Profiles = [new FeatureProfile { Id = "user.one", Name = "Example", EnabledFeatures = ["plugins", "future"] }]
        };
        var loaded = JsonSerializer.Deserialize<SimpleModeSettingsConfig>(JsonSerializer.Serialize(settings))!;
        var selected = StartupFeatureCatalog.Resolve(loaded);
        Assert.Contains(StartupFeatureCatalog.Plugins, selected);
        Assert.All(StartupFeatureCatalog.Graph.Features.Where(feature => feature.Required),
            feature => Assert.Contains(feature.Id, selected));
        Assert.Equal(StartupFeatureCatalog.Graph.Features.Count(feature => feature.Required) + 1, selected.Count);
        Assert.Contains("future", loaded.Profiles[0].EnabledFeatures);
    }

    [Fact]
    public void MissingProfileFallsBackToLegacyMode()
    {
        var settings = new SimpleModeSettingsConfig { Enabled = true, SelectedProfileId = "missing" };
        var selected = StartupFeatureCatalog.Resolve(settings);
        Assert.Contains(StartupFeatureCatalog.QuickDraw, selected);
        Assert.All(StartupFeatureCatalog.Graph.Features.Where(feature => feature.Required),
            feature => Assert.Contains(feature.Id, selected));
        Assert.Equal(StartupFeatureCatalog.Graph.Features.Count(feature => feature.Required) + 5, selected.Count);
        Assert.Equal(StartupFeatureCatalog.SimpleProfile, StartupFeatureCatalog.ResolveSelectedProfileId(settings));
        Assert.Equal("missing", settings.SelectedProfileId);
    }

    [Fact]
    public void PendingSelectionChangesLegacyModeOnlyOnNextLoad()
    {
        var settings = new SimpleModeSettingsConfig { Enabled = true };
        settings.SelectedProfileId = StartupFeatureCatalog.FullProfile;
        Assert.True(settings.Enabled);
        var loaded = JsonSerializer.Deserialize<SimpleModeSettingsConfig>(JsonSerializer.Serialize(settings))!;
        Assert.False(loaded.Enabled);
        Assert.Equal(StartupFeatureCatalog.Graph.Features.Count, StartupFeatureCatalog.Resolve(loaded).Count);
    }
}
