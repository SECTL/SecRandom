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
}
