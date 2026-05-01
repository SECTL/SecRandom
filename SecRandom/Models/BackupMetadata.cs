using System;

namespace SecRandom.Models;

public class BackupMetadata
{
    public string FileName { get; set; } = string.Empty;
    public DateTime DateTime { get; set; } = DateTime.MinValue;
    public string Size { get; set; } = string.Empty;
}