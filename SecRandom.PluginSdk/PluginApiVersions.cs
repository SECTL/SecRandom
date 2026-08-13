namespace SecRandom.PluginSdk;

/// <summary>
/// Plugin API contract versions. <see cref="Current"/> must be bumped whenever the
/// application major version changes so the plugin SDK tracks the main program.
/// </summary>
public static class PluginApiVersions
{
    /// <summary>
    /// The plugin API version the current host implements. Plugins declare the API
    /// version they target in <c>manifest.yml</c>; the host rejects plugins whose
    /// declared API major is below <see cref="Current"/>.Major.
    /// </summary>
    public static readonly Version Current = new(3, 0, 0);
}
