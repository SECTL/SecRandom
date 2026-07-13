using System;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Seating;

public sealed class SeatingChartService(IProfileService profileService)
{
    private SeatingChartConfig? _config;
    private string _loadedProfileName = string.Empty;

    public SeatingChartCollection Current
    {
        get
        {
            EnsureCurrentProfile();
            return _config!.Data;
        }
    }

    public void Save()
    {
        EnsureCurrentProfile();
        _config!.Save();
    }

    public void Reload()
    {
        _loadedProfileName = string.Empty;
        EnsureCurrentProfile();
    }

    private void EnsureCurrentProfile()
    {
        var name = profileService.StudentListConfig?.Name ?? "default";
        if (_config is not null && string.Equals(_loadedProfileName, name, StringComparison.Ordinal))
            return;

        _config?.Save();
        _loadedProfileName = name;
        _config = new SeatingChartConfig(name);
    }
}
