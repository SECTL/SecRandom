using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SecRandom.Core.Enums.Configs;
using SecRandom.Shared;

namespace SecRandom.Services.Voice;

/// <summary>
/// Stores OmniTTS service API keys outside <c>settings.json</c>.
/// Keys are credentials: they must never enter <c>MainConfigModel</c>, IPC payloads,
/// logs, telemetry, or backup/export archives.
/// </summary>
public sealed class OmniTtsCredentialStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly object _gate = new();
    private Dictionary<string, string>? _cache;

    public string? GetKey(OmniTtsProvider provider, string? baseUrl)
    {
        var storageKey = GetStorageKey(provider, baseUrl);
        if (storageKey is null)
            return null;
        lock (_gate)
        {
            _cache ??= LoadCore();
            return _cache.TryGetValue(storageKey, out var key) && !string.IsNullOrWhiteSpace(key)
                ? key
                : null;
        }
    }

    public bool HasKey(OmniTtsProvider provider, string? baseUrl) => !string.IsNullOrWhiteSpace(GetKey(provider, baseUrl));

    public void SetKey(OmniTtsProvider provider, string? baseUrl, string key)
    {
        var storageKey = GetStorageKey(provider, baseUrl)
                         ?? throw new ArgumentException("The API base URL must use HTTPS, or HTTP on loopback, without user information, a query or a fragment.", nameof(baseUrl));
        lock (_gate)
        {
            _cache ??= LoadCore();
            _cache[storageKey] = key.Trim();
            SaveCore(_cache);
        }
    }

    public void ClearKey(OmniTtsProvider provider, string? baseUrl)
    {
        var storageKey = GetStorageKey(provider, baseUrl);
        if (storageKey is null)
            return;
        lock (_gate)
        {
            _cache ??= LoadCore();
            _cache.Remove(storageKey);
            SaveCore(_cache);
        }
    }

    public static string? NormalizeOrigin(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && (uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback)) ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return null;

        var host = uri.IdnHost.ToLowerInvariant();
        if (uri.HostNameType == UriHostNameType.IPv6 && !host.StartsWith('['))
            host = $"[{host}]";
        return $"{uri.Scheme}://{host}{(uri.IsDefaultPort ? string.Empty : $":{uri.Port}")}";
    }

    // Provider-only entries have no trusted destination and are deliberately never upgraded from settings.
    private static string? GetStorageKey(OmniTtsProvider provider, string? baseUrl) =>
        Enum.IsDefined(provider) && NormalizeOrigin(baseUrl) is { } origin ? $"{provider}|{origin}" : null;

    private static Dictionary<string, string> LoadCore()
    {
        var path = Utils.GetFilePath("config", "voice", "omnitts-keys.json");
        try
        {
            if (!File.Exists(path))
                return [];

            var content = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
            return parsed ?? [];
        }
        catch (Exception)
        {
            // A corrupt credential file must not block voice settings; treat it as empty.
            return [];
        }
    }

    private static void SaveCore(Dictionary<string, string> keys)
    {
        var path = Utils.GetFilePath("config", "voice", "omnitts-keys.json");
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(keys, SerializerOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
