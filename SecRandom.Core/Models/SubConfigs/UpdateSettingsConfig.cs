using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Updates;

namespace SecRandom.Core.Models.SubConfigs;

public partial class UpdateSettingsConfig : ObservableObject, IJsonOnDeserialized
{
    private static readonly DateOnly LegacyUnsetLastCheckDate = new(1970, 1, 1);
    [JsonIgnore] private bool _updateModeVersionLoaded;

    [ObservableProperty] private int _autoUpdateMode = 3;
    [ObservableProperty] private int _updateModeVersion = 1;
    [ObservableProperty] private UpdateChannel _updateChannel = UpdateChannel.Release;
    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private DateTime? _lastCheckTime;

    partial void OnLastCheckTimeChanged(DateTime? value)
    {
        if (value.HasValue && DateOnly.FromDateTime(value.Value) == LegacyUnsetLastCheckDate)
            LastCheckTime = null;
    }

    partial void OnUpdateModeVersionChanged(int value) => _updateModeVersionLoaded = true;

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (!_updateModeVersionLoaded)
        {
            // Old nonzero values meant scheduled checks; retain that behavior without upgrading users to auto-install.
            AutoUpdateMode = AutoUpdateMode == 0 ? 0 : 1;
            UpdateModeVersion = 1;
        }
    }
}
