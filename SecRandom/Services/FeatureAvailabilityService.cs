using System;
using System.ComponentModel;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;

namespace SecRandom.Services;

public sealed class FeatureAvailabilityService
{
    private readonly MainConfigHandler _configHandler;
    private MoreSettingsConfig _settings;

    public FeatureAvailabilityService(MainConfigHandler configHandler)
    {
        _configHandler = configHandler;
        _settings = configHandler.Data.MoreSettings;
        _settings.PropertyChanged += SettingsOnPropertyChanged;
    }

    public bool IsLotteryEnabled => _settings.LotteryEnabled;

    public event EventHandler? Changed;

    public void Refresh()
    {
        if (ReferenceEquals(_settings, _configHandler.Data.MoreSettings))
            return;

        _settings.PropertyChanged -= SettingsOnPropertyChanged;
        _settings = _configHandler.Data.MoreSettings;
        _settings.PropertyChanged += SettingsOnPropertyChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MoreSettingsConfig.LotteryEnabled))
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
