using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs.General;

public partial class ProofRetentionConfig : ObservableObject
{
    // Zero keeps local proof files until the user removes them manually.
    [ObservableProperty] private int _retentionDays = 30;

    [ObservableProperty] private long _maximumStorageBytes = 64L * 1024 * 1024;
}
