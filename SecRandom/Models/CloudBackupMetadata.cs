using System;

namespace SecRandom.Models;

public class CloudBackupMetadata
{
    public string BackupId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime DateTime { get; set; } = DateTime.MinValue;
    public string Size { get; set; } = string.Empty;
    public bool IsComplete { get; set; } = true;
    public int PartCount { get; set; }
    public string StatusText { get; set; } = string.Empty;

    /// <summary>
    ///     Whether the uploaded package is encrypted, taken from the cloud file names so the listing
    ///     can show it without a manifest download per backup.
    /// </summary>
    public bool IsEncrypted { get; set; }

    /// <summary>Localized encryption label shown in the backup list.</summary>
    public string EncryptionText { get; set; } = string.Empty;
}
