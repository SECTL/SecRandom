using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;
using SecRandom.Core.Services.FeatureConfiguration;

namespace SecRandom.Core.Models.SubConfigs.General;

/// <summary>
///     简洁模式。开启后只保留点名相关的界面入口，其余功能的设置页不再显示，
///     这些功能在运行时改用默认配置，但用户已经存进 <c>settings.json</c> 的设置原样保留。
/// </summary>
public partial class SimpleModeSettingsConfig : ObservableObject, IJsonOnDeserialized
{
    [ObservableProperty] private bool _enabled;

    // A null selection means that the legacy Enabled flag still owns the mode.
    [ObservableProperty] private string? _selectedProfileId;
    [ObservableProperty] private List<FeatureProfile> _profiles = [];

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (SelectedProfileId == StartupFeatureCatalog.SimpleProfile)
            Enabled = true;
        else if (SelectedProfileId == StartupFeatureCatalog.FullProfile ||
                 (SelectedProfileId is not null && Profiles?.Any(profile => profile?.Id == SelectedProfileId) == true))
            Enabled = false;
    }
}

public sealed class FeatureProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> EnabledFeatures { get; set; } = [];
}
