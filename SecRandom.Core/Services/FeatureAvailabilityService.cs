using System.ComponentModel;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.FeatureConfiguration;

namespace SecRandom.Core.Services;

public static partial class CoreRuntimeServiceCollectionExtensions
{
    private sealed class FeatureAvailabilityService : IFeatureAvailabilityService
    {
    private readonly MainConfigHandler _configHandler;
    private MoreSettingsConfig _settings;
    private SimpleModeSettingsConfig _simpleMode;
    private readonly FeatureRuntimeSnapshot? _snapshot;

    public FeatureAvailabilityService(MainConfigHandler configHandler, FeatureRuntimeSnapshot? snapshot = null)
    {
        _configHandler = configHandler;
        _snapshot = snapshot;
        _settings = configHandler.Data.MoreSettings;
        _settings.PropertyChanged += SettingsOnPropertyChanged;
        _simpleMode = configHandler.Data.General.SimpleMode;
        _simpleMode.PropertyChanged += SimpleModeOnPropertyChanged;
    }

    public bool IsSimpleModeEnabled => _simpleMode.Enabled;

    public bool IsLotteryEnabled => !IsSimpleModeEnabled && _settings.LotteryEnabled &&
                                    _snapshot?.IsEnabled(StartupFeatureCatalog.Lottery) != false;

    public event EventHandler? Changed;

    public void Refresh()
    {
        var changed = false;

        if (!ReferenceEquals(_settings, _configHandler.Data.MoreSettings))
        {
            _settings.PropertyChanged -= SettingsOnPropertyChanged;
            _settings = _configHandler.Data.MoreSettings;
            _settings.PropertyChanged += SettingsOnPropertyChanged;
            changed = true;
        }

        if (!ReferenceEquals(_simpleMode, _configHandler.Data.General.SimpleMode))
        {
            _simpleMode.PropertyChanged -= SimpleModeOnPropertyChanged;
            _simpleMode = _configHandler.Data.General.SimpleMode;
            _simpleMode.PropertyChanged += SimpleModeOnPropertyChanged;
            changed = true;
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MoreSettingsConfig.LotteryEnabled))
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SimpleModeOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SimpleModeSettingsConfig.Enabled))
            Changed?.Invoke(this, EventArgs.Empty);
    }
    }
}
