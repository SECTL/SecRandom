using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs.General;

public partial class BackupConfig : ObservableObject
{
    [ObservableProperty] private bool _autoBackupEnabled = true;
    [ObservableProperty] private int _autoBackupIntervalDays = 7;
    [ObservableProperty] private int _autoBackupMaxCount = 16;

    [ObservableProperty] private bool _includeConfig = true;
    [ObservableProperty] private bool _includeList = true;
    [ObservableProperty] private bool _includeHistory = true;
    [ObservableProperty] private bool _includeProofs = true;
    [ObservableProperty] private bool _includeAudio = false;
    [ObservableProperty] private bool _includeCses = true;
    [ObservableProperty] private bool _includeImages = true;
    [ObservableProperty] private bool _includeThemes = true;
    [ObservableProperty] private bool _includeLogs = false;
}
