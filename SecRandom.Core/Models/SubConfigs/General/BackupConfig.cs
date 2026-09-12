using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs.General;

public partial class BackupConfig : ObservableObject
{
    [ObservableProperty] private bool _autoBackupEnabled = true;
    [ObservableProperty] private int _autoBackupIntervalDays = 7;
    [ObservableProperty] private int _autoBackupMaxCount = 16;

    /// <summary>
    ///     Cloud backup stays opt-in: it uploads the account's data to SECTL on its own schedule, so
    ///     an existing installation must not start doing that just because the app updated.
    /// </summary>
    [ObservableProperty] private bool _cloudAutoBackupEnabled = false;
    [ObservableProperty] private int _cloudAutoBackupIntervalDays = 7;
    [ObservableProperty] private int _cloudAutoBackupMaxCount = 5;

    /// <summary>
    ///     Cloud backup content is selected separately from the local backup, and it is shorter on
    ///     purpose: logs, the device identity, the generated voice cache, draw proofs, and theme
    ///     resources have no row because a cloud archive may never carry them. Images stay off by
    ///     default because member/prize pictures are what fills the account quota fastest.
    /// </summary>
    [ObservableProperty] private bool _cloudIncludeConfig = true;
    [ObservableProperty] private bool _cloudIncludeList = true;
    [ObservableProperty] private bool _cloudIncludeHistory = true;
    [ObservableProperty] private bool _cloudIncludeAudio = false;
    [ObservableProperty] private bool _cloudIncludeCses = true;
    [ObservableProperty] private bool _cloudIncludeImages = false;

    [ObservableProperty] private bool _includeConfig = true;
    [ObservableProperty] private bool _includeList = true;
    [ObservableProperty] private bool _includeHistory = true;
    [ObservableProperty] private bool _includeProofs = true;
    [ObservableProperty] private bool _includeAudio = false;
    [ObservableProperty] private bool _includeCses = true;
    [ObservableProperty] private bool _includeImages = true;
    [ObservableProperty] private bool _includeLogs = false;
}
