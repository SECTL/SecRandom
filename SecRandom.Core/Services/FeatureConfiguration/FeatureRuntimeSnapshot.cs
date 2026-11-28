namespace SecRandom.Core.Services.FeatureConfiguration;

/// <summary>The feature selection used by this host until it is restarted.</summary>
public sealed class FeatureRuntimeSnapshot
{
    private readonly HashSet<string> _enabled;

    public FeatureRuntimeSnapshot(IEnumerable<string> enabled)
    {
        _enabled = new HashSet<string>(enabled, StringComparer.Ordinal);
    }

    public bool IsEnabled(string id) => _enabled.Contains(id);
}
