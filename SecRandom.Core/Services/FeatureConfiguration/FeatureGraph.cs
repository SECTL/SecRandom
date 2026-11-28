namespace SecRandom.Core.Services.FeatureConfiguration;

/// <summary>Metadata needed to resolve feature dependencies without constructing a feature.</summary>
public sealed record FeatureDefinition(
    string Id,
    IReadOnlyList<string> Dependencies,
    bool Required = false,
    string NameKey = "",
    string DescriptionKey = "",
    string CategoryKey = "");

/// <summary>Validates feature definitions and computes complete enable/disable operations.</summary>
public sealed class FeatureGraph
{
    private readonly Dictionary<string, FeatureDefinition> _features;

    public FeatureGraph(IEnumerable<FeatureDefinition> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        _features = new Dictionary<string, FeatureDefinition>(StringComparer.Ordinal);
        foreach (var feature in features)
        {
            if (feature is null || string.IsNullOrWhiteSpace(feature.Id) || feature.Dependencies is null)
                throw new ArgumentException("Feature IDs must be nonempty and unique", nameof(features));
            var snapshot = feature with { Dependencies = Array.AsReadOnly(feature.Dependencies.ToArray()) };
            if (!_features.TryAdd(snapshot.Id, snapshot))
                throw new ArgumentException("Feature IDs must be nonempty and unique", nameof(features));
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in _features.Keys)
            Validate(id, visiting, visited);
    }

    public IReadOnlyCollection<FeatureDefinition> Features => _features.Values;

    public IReadOnlySet<string> Enable(IEnumerable<string> enabled, string id)
    {
        var result = Normalize(enabled);
        AddDependencies(id, result);
        return result;
    }

    public IReadOnlySet<string> Disable(IEnumerable<string> enabled, string id)
    {
        GetFeature(id);
        var result = Normalize(enabled);
        var removed = new HashSet<string>(StringComparer.Ordinal) { id };
        bool changed;
        do
        {
            changed = false;
            foreach (var feature in _features.Values)
                if (result.Contains(feature.Id) && feature.Dependencies.Any(removed.Contains))
                    changed |= removed.Add(feature.Id);
        } while (changed);

        if (removed.Any(featureId => _features[featureId].Required))
            throw new InvalidOperationException("A required feature cannot be disabled");
        result.ExceptWith(removed);
        return result;
    }

    public IReadOnlySet<string> Resolve(IEnumerable<string> enabled) => Normalize(enabled);

    private HashSet<string> Normalize(IEnumerable<string> enabled)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in _features.Values.Where(feature => feature.Required))
            AddDependencies(feature.Id, result);
        foreach (var id in enabled)
            AddDependencies(id, result);
        return result;
    }

    private void AddDependencies(string id, HashSet<string> result)
    {
        var feature = GetFeature(id);
        if (!result.Add(id))
            return;
        foreach (var dependency in feature.Dependencies)
            AddDependencies(dependency, result);
    }

    private FeatureDefinition GetFeature(string id) =>
        id is not null && _features.TryGetValue(id, out var feature)
            ? feature
            : throw new ArgumentException($"Unknown feature: {id}");

    private void Validate(string id, HashSet<string> visiting, HashSet<string> visited)
    {
        if (visited.Contains(id))
            return;
        if (!visiting.Add(id))
            throw new ArgumentException($"Circular feature dependency: {id}");
        foreach (var dependency in GetFeature(id).Dependencies)
            Validate(dependency, visiting, visited);
        visiting.Remove(id);
        visited.Add(id);
    }
}
