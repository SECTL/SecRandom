namespace SecRandom.Shared.Models.Plugins;

/// <summary>
///     Signed plugin-market index. The whole document (including <see cref="PluginCatalogEntry.Sha256"/>)
///     is covered by an Ed25519 signature so a published entry cannot be altered after release.
/// </summary>
public sealed class PluginIndexDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string Product { get; set; } = "SecRandom";
    public List<PluginCatalogEntry> Plugins { get; set; } = [];
}

/// <summary>
///     One plugin-market entry. <see cref="DownloadUrl"/> points at the plugin author's release asset;
///     <see cref="Sha256"/> is verified by the client before staging. <see cref="ApiVersion"/> and
///     <see cref="MinimumHostVersion"/> drive compatibility gating.
/// </summary>
public sealed class PluginCatalogEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = string.Empty;
    public string MinimumHostVersion { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
    public long Downloads { get; set; }
    public string? ProjectUrl { get; set; }
    public string? ReadmeUrl { get; set; }
    public string? IconUrl { get; set; }
    public List<PluginCatalogDependency> Dependencies { get; set; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;
}

/// <summary>
///     Catalog-side dependency reference. Id matches another entry's <see cref="PluginCatalogEntry.Id"/>.
/// </summary>
public sealed class PluginCatalogDependency
{
    public string Id { get; set; } = string.Empty;
    public bool Required { get; set; } = true;
}
