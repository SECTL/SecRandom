using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.FeatureConfiguration;

namespace SecRandom.Views.SettingsPages.Picking;

internal static class DrawFeatureVisibility
{
    public static bool Music => IAppHost.TryGetService<FeatureRuntimeSnapshot>()?.IsEnabled(StartupFeatureCatalog.Music) != false;
    public static bool Voice => IAppHost.TryGetService<FeatureRuntimeSnapshot>()?.IsEnabled(StartupFeatureCatalog.Voice) != false;
}
