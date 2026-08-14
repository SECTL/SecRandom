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

    private string FilePath => Utils.GetFilePath("config", "voice", "omnitts-keys.json");

    public string? GetKey(OmniTtsProvider provider)
    {
        lock (_gate)
        {
            _cache ??= LoadCore();
            return _cache.TryGetValue(GetStorageKey(provider), out var key) && !string.IsNullOrWhiteSpace(key)
                ? key
                : null;
        }
    }

    public bool HasKey(OmniTtsProvider provider) => !string.IsNullOrWhiteSpace(GetKey(provider));

    public void SetKey(OmniTtsProvider provider, string key)
    {
        lock (_gate)
        {
            _cache ??= LoadCore();
            _cache[GetStorageKey(provider)] = key.Trim();
            SaveCore(_cache);
        }
    }

    public void ClearKey(OmniTtsProvider provider)
    {
        lock (_gate)
        {
            _cache ??= LoadCore();
            _cache.Remove(GetStorageKey(provider));
            SaveCore(_cache);
        }
    }

    private static string GetStorageKey(OmniTtsProvider provider) => provider.ToString();

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
