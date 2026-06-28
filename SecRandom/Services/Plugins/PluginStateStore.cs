using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SecRandom.Shared;

namespace SecRandom.Services.Plugins;

public sealed class PluginStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Dictionary<string, PluginState> _states = [];

    public PluginStateStore()
    {
        Load();
        ClearAppliedRestartFlags();
    }

    public IReadOnlyDictionary<string, PluginState> States => _states;

    public PluginState GetOrCreate(string pluginId)
    {
        if (_states.TryGetValue(pluginId, out var state))
            return state;

        state = new PluginState { PluginId = pluginId };
        _states[pluginId] = state;
        return state;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StateFilePath)!);
        var states = _states.Values.OrderBy(x => x.PluginId, StringComparer.Ordinal).ToList();
        File.WriteAllText(StateFilePath, JsonSerializer.Serialize(states, JsonOptions));
    }

    private static string StateFilePath => Utils.GetFilePath("plugins", "plugins-state.json");

    private void Load()
    {
        if (!File.Exists(StateFilePath))
            return;

        try
        {
            var states = JsonSerializer.Deserialize<List<PluginState>>(File.ReadAllText(StateFilePath), JsonOptions) ?? [];
            foreach (var state in states.Where(x => !string.IsNullOrWhiteSpace(x.PluginId)))
                _states[state.PluginId] = state;
        }
        catch
        {
            _states.Clear();
        }
    }

    private void ClearAppliedRestartFlags()
    {
        var changed = false;
        foreach (var state in _states.Values.Where(state => state.RequiresRestart))
        {
            state.RequiresRestart = false;
            changed = true;
        }

        if (changed)
            Save();
    }
}

public sealed class PluginState
{
    public string PluginId { get; init; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool RequiresRestart { get; set; }
}
