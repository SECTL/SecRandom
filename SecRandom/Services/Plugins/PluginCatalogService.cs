using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Shared;

namespace SecRandom.Services.Plugins;

public interface IPluginCatalogService
{
    IReadOnlyList<PluginCatalogEntry> Entries { get; }
    IReadOnlyList<PluginCatalogMirror> Mirrors { get; }
    IReadOnlyList<PluginCatalogSource> Sources { get; }
    PluginCatalogMirror SelectedMirror { get; }
    bool IgnoreTlsCertificateErrors { get; }
    void SelectMirror(string mirrorId);
    PluginCatalogSource AddSource(string url, string mirrorUrl);
    PluginCatalogSource UpdateSource(string sourceId, string url, string mirrorUrl);
    void RemoveSource(string sourceId);
    void SetIgnoreTlsCertificateErrors(bool value);
    Task<IReadOnlyList<PluginCatalogEntry>> RefreshAsync(CancellationToken cancellationToken = default);
    Task<string> InstallAsync(PluginCatalogEntry entry, IPluginManager pluginManager, CancellationToken cancellationToken = default);
}

public sealed class PluginCatalogService : IPluginCatalogService, IDisposable
{
    private const string BuiltInSecTlMirrorId = "sectl";
    private const string BuiltInGitHubMirrorId = "github";
    private const string OfficialIndexUrl = "https://raw.githubusercontent.com/SECTL/SecRandom-PluginIndex/main/index/index.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly ILogger<PluginCatalogService> _logger;
    private readonly List<PluginCatalogEntry> _entries = [];
    private readonly List<PluginCatalogMirror> _mirrors =
    [
        new() { Id = BuiltInSecTlMirrorId, Name = "SECTL", Url = "https://ghproxy.sectl.cn/" },
        new() { Id = BuiltInGitHubMirrorId, Name = "GitHub", Url = string.Empty }
    ];
    private readonly List<PluginCatalogSource> _sources = [];
    private HttpClient _httpClient;
    private string _selectedMirrorId = BuiltInSecTlMirrorId;
    private bool _ignoreTlsCertificateErrors;
    private bool _stateLoaded;

    public PluginCatalogService(ILogger<PluginCatalogService> logger)
    {
        _logger = logger;
        _httpClient = CreateHttpClient(false);
    }

    public IReadOnlyList<PluginCatalogEntry> Entries => new ReadOnlyCollection<PluginCatalogEntry>(_entries);

    public IReadOnlyList<PluginCatalogMirror> Mirrors
    {
        get
        {
            EnsureStateLoaded();
            return new ReadOnlyCollection<PluginCatalogMirror>(_mirrors);
        }
    }

    public IReadOnlyList<PluginCatalogSource> Sources
    {
        get
        {
            EnsureStateLoaded();
            return new ReadOnlyCollection<PluginCatalogSource>(_sources);
        }
    }

    public PluginCatalogMirror SelectedMirror
    {
        get
        {
            EnsureStateLoaded();
            return _mirrors.FirstOrDefault(x => x.Id == _selectedMirrorId) ?? _mirrors[0];
        }
    }

    public bool IgnoreTlsCertificateErrors
    {
        get
        {
            EnsureStateLoaded();
            return _ignoreTlsCertificateErrors;
        }
    }

    private static string StateFilePath => Utils.GetFilePath("plugins", "plugin-sources.json");

    public void SelectMirror(string mirrorId)
    {
        EnsureStateLoaded();
        if (_mirrors.All(x => x.Id != mirrorId))
            return;

        _selectedMirrorId = mirrorId;
        SaveState();
    }

    public PluginCatalogSource AddSource(string url, string mirrorUrl)
    {
        EnsureStateLoaded();
        var normalizedUrl = NormalizeSourceUrl(url);
        var normalizedMirrorUrl = NormalizeOptionalMirrorUrl(mirrorUrl);
        var existing = _sources.FirstOrDefault(x => string.Equals(x.Url, normalizedUrl, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        var source = new PluginCatalogSource
        {
            Id = "custom-" + Guid.NewGuid().ToString("N"),
            Name = new Uri(normalizedUrl).Host,
            Url = normalizedUrl,
            MirrorUrl = normalizedMirrorUrl
        };

        _sources.Add(source);
        SaveState();
        return source;
    }

    public PluginCatalogSource UpdateSource(string sourceId, string url, string mirrorUrl)
    {
        EnsureStateLoaded();
        var index = _sources.FindIndex(x => x.Id == sourceId);
        if (index < 0)
            throw new ArgumentException("Plugin source was not found.", nameof(sourceId));

        var normalizedUrl = NormalizeSourceUrl(url);
        var normalizedMirrorUrl = NormalizeOptionalMirrorUrl(mirrorUrl);
        var source = _sources[index] with
        {
            Name = new Uri(normalizedUrl).Host,
            Url = normalizedUrl,
            MirrorUrl = normalizedMirrorUrl,
            IsBuiltIn = false
        };
        _sources[index] = source;
        SaveState();
        return source;
    }

    public void RemoveSource(string sourceId)
    {
        EnsureStateLoaded();
        var source = _sources.FirstOrDefault(x => x.Id == sourceId);
        if (source == null)
            return;

        _sources.Remove(source);
        SaveState();
    }

    public void SetIgnoreTlsCertificateErrors(bool value)
    {
        EnsureStateLoaded();
        if (_ignoreTlsCertificateErrors == value)
            return;

        _ignoreTlsCertificateErrors = value;
        var oldClient = _httpClient;
        _httpClient = CreateHttpClient(value);
        oldClient.Dispose();
        SaveState();
    }

    public async Task<IReadOnlyList<PluginCatalogEntry>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        EnsureStateLoaded();
        var nextEntries = new List<PluginCatalogEntry>();
        var errors = new List<Exception>();

        try
        {
            nextEntries.AddRange(await LoadEntriesAsync(OfficialIndexUrl, SelectedMirror.Url, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            errors.Add(ex);
            _logger.LogWarning(ex, "Failed to refresh the built-in plugin source.");
        }

        foreach (var source in _sources)
        {
            try
            {
                nextEntries.AddRange(await LoadEntriesAsync(source.Url, source.MirrorUrl, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                errors.Add(ex);
                _logger.LogWarning(ex, "Failed to refresh custom plugin source: {SourceUrl}", source.Url);
            }
        }

        if (nextEntries.Count == 0 && errors.Count > 0)
            throw errors[0];

        _entries.Clear();
        foreach (var entry in nextEntries
                     .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                     .GroupBy(x => x.Id, StringComparer.Ordinal)
                     .Select(x => x.First()))
            _entries.Add(entry);

        _logger.LogInformation("Plugin catalog refreshed: {Count}", _entries.Count);
        return Entries;
    }

    public async Task<string> InstallAsync(PluginCatalogEntry entry, IPluginManager pluginManager, CancellationToken cancellationToken = default)
    {
        EnsureStateLoaded();
        if (string.IsNullOrWhiteSpace(entry.PackageUrl))
            throw new PluginImportException(PluginImportFailureReason.InvalidPackage, "Plugin catalog entry does not provide a package URL.");

        var tempPackagePath = Path.Combine(Path.GetTempPath(), "SecRandomPluginCatalog", $"{entry.Id}-{Guid.NewGuid():N}.srpx");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPackagePath)!);

        try
        {
            await using (var source = await OpenAsync(entry.PackageUrl, entry.SourceMirrorUrl, cancellationToken).ConfigureAwait(false))
            await using (var destination = File.Create(tempPackagePath))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            return pluginManager.ImportPluginPackage(tempPackagePath);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPackagePath))
                    File.Delete(tempPackagePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean temporary plugin package: {PackagePath}", tempPackagePath);
            }
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static HttpClient CreateHttpClient(bool ignoreTlsCertificateErrors)
    {
        var handler = new HttpClientHandler();
        if (ignoreTlsCertificateErrors)
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private async Task<IReadOnlyList<PluginCatalogEntry>> LoadEntriesAsync(string indexUrl, string mirrorUrl, CancellationToken cancellationToken)
    {
        var stream = await OpenAsync(indexUrl, mirrorUrl, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var document = await JsonSerializer.DeserializeAsync<PluginCatalogDocument>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                           ?? new PluginCatalogDocument();

            foreach (var entry in document.Plugins)
                entry.SourceMirrorUrl = mirrorUrl;

            return document.Plugins;
        }
    }

    private async Task<Stream> OpenAsync(string url, string mirrorUrl, CancellationToken cancellationToken)
    {
        var candidate = BuildSourceUrl(url, mirrorUrl);
        _logger.LogDebug("Reading plugin catalog resource: {Url}", candidate);
        return await _httpClient.GetStreamAsync(candidate, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildSourceUrl(string url, string mirrorUrl)
    {
        if (string.IsNullOrWhiteSpace(mirrorUrl))
            return url;

        if (url.StartsWith(mirrorUrl, StringComparison.OrdinalIgnoreCase))
            return url;

        if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase))
            return url;

        return mirrorUrl + url;
    }

    private static string NormalizeSourceUrl(string url)
    {
        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Plugin source URL must be an absolute HTTP(S) URL.", nameof(url));

        return trimmed;
    }

    private static string NormalizeOptionalMirrorUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        return NormalizeMirrorUrl(url);
    }

    private static string NormalizeMirrorUrl(string url)
    {
        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Plugin mirror URL must be an absolute HTTP(S) URL.", nameof(url));

        return trimmed.TrimEnd('/') + "/";
    }

    private void LoadState()
    {
        if (!File.Exists(StateFilePath))
            return;

        try
        {
            var state = JsonSerializer.Deserialize<PluginCatalogSourceState>(File.ReadAllText(StateFilePath), JsonOptions);
            if (state == null)
                return;

            foreach (var source in state.CustomSources)
            {
                if (!string.IsNullOrWhiteSpace(source.Id) &&
                    !string.IsNullOrWhiteSpace(source.Url) &&
                    _sources.All(x => x.Id != source.Id))
                    _sources.Add(source with { IsBuiltIn = false });
            }

            var selectedMirrorId = string.IsNullOrWhiteSpace(state.SelectedMirrorId)
                ? state.SelectedSourceId
                : state.SelectedMirrorId;
            if (!string.IsNullOrWhiteSpace(selectedMirrorId) &&
                _mirrors.Any(x => x.Id == selectedMirrorId))
                _selectedMirrorId = selectedMirrorId;

            _ignoreTlsCertificateErrors = state.IgnoreTlsCertificateErrors;
            _httpClient.Dispose();
            _httpClient = CreateHttpClient(_ignoreTlsCertificateErrors);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load plugin source settings.");
        }
    }

    private void EnsureStateLoaded()
    {
        if (_stateLoaded)
            return;

        LoadState();
        _stateLoaded = true;
    }

    private void SaveState()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StateFilePath)!);
        var state = new PluginCatalogSourceState
        {
            SelectedMirrorId = _selectedMirrorId,
            IgnoreTlsCertificateErrors = _ignoreTlsCertificateErrors,
            CustomSources = _sources.Select(x => x with { IsBuiltIn = false }).ToList()
        };

        File.WriteAllText(StateFilePath, JsonSerializer.Serialize(state, JsonOptions));
    }
}

public sealed class PluginCatalogDocument
{
    [JsonPropertyName("plugins")] public IReadOnlyList<PluginCatalogEntry> Plugins { get; init; } = [];
}

public sealed class PluginCatalogEntry
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
    [JsonPropertyName("author")] public string Author { get; init; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; init; } = string.Empty;
    [JsonPropertyName("apiVersion")] public string ApiVersion { get; init; } = string.Empty;
    [JsonPropertyName("minimumHostVersion")] public string MinimumHostVersion { get; init; } = string.Empty;
    [JsonPropertyName("packageUrl")] public string PackageUrl { get; init; } = string.Empty;
    [JsonPropertyName("projectUrl")] public string ProjectUrl { get; init; } = string.Empty;
    [JsonPropertyName("readme")] public string Readme { get; init; } = string.Empty;
    [JsonPropertyName("stars")] public int Stars { get; init; }
    [JsonPropertyName("downloads")] public int Downloads { get; init; }
    [JsonIgnore] public string SourceMirrorUrl { get; internal set; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;
}

public sealed record PluginCatalogMirror
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Url : Name;
    public override string ToString() => DisplayName;
}

public sealed record PluginCatalogSource
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string MirrorUrl { get; init; } = string.Empty;
    public bool IsBuiltIn { get; init; }
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Url : Name;
    public override string ToString() => DisplayName;
}

public sealed class PluginCatalogSourceState
{
    public string SelectedMirrorId { get; init; } = BuiltInSourceDefaults.SelectedMirrorId;
    public string SelectedSourceId { get; init; } = string.Empty;
    public bool IgnoreTlsCertificateErrors { get; init; }
    public IReadOnlyList<PluginCatalogSource> CustomSources { get; init; } = [];
}

internal static class BuiltInSourceDefaults
{
    public const string SelectedMirrorId = "sectl";
}
