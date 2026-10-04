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
    ///     Device alias stamped into every cloud backup id, so several signed-in devices can tell
    ///     their own backups apart in the cloud list and keep their own retention budget. It is a
    ///     user-visible display label rather than a device identity, so the device UUID still never
    ///     reaches the cloud; empty means the host name is used.
    /// </summary>
    [ObservableProperty] private string _cloudDeviceAlias = string.Empty;

    /// <summary>
    ///     Encrypts the cloud backup client-side (AES-256-GCM over the whole archive) before it leaves
    ///     the device, so the account cloud only ever stores a ciphertext. It is off by default: an
    ///     existing installation must not start sending a form of backup the user did not ask for, and
    ///     plaintext stays the behaviour a user who never opens this page already has. With it on, an
    ///     upload without a configured passphrase fails or is skipped rather than falling back to
    ///     plaintext, the derived key lives only in memory for the current run (see
    ///     <c>CloudBackupKeyStore</c>), and turning it back off uploads plaintext again after an explicit
    ///     warning.
    /// </summary>
    [ObservableProperty] private bool _cloudEncryptionEnabled;

    /// <summary>
    ///     Cloud backup content is selected separately from the local backup, and it is shorter on
    ///     purpose: logs, the device identity, the generated voice cache, and theme resources have no
    ///     row because a cloud archive may never carry them. Images stay off by default because
    ///     member/prize pictures are what fills the account quota fastest, and draw proofs stay off by
    ///     default because uploading them is a deliberate choice — they are the only copy of the local
    ///     evidence chain once a proof has been deleted locally.
    /// </summary>
    [ObservableProperty] private bool _cloudIncludeConfig = true;
    [ObservableProperty] private bool _cloudIncludeList = true;
    [ObservableProperty] private bool _cloudIncludeHistory = true;
    [ObservableProperty] private bool _cloudIncludeAudio = false;
    [ObservableProperty] private bool _cloudIncludeCses = true;
    [ObservableProperty] private bool _cloudIncludeImages = false;
    [ObservableProperty] private bool _cloudIncludeProofs = false;

    [ObservableProperty] private bool _includeConfig = true;
    [ObservableProperty] private bool _includeList = true;
    [ObservableProperty] private bool _includeHistory = true;
    [ObservableProperty] private bool _includeProofs = true;
    [ObservableProperty] private bool _includeAudio = false;
    [ObservableProperty] private bool _includeCses = true;
    [ObservableProperty] private bool _includeImages = true;
    [ObservableProperty] private bool _includeLogs = false;
}
