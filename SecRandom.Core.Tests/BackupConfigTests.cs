using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs.General;

namespace SecRandom.Core.Tests;

public class BackupConfigTests
{
    [Fact]
    public void BackupDefaultsAndLegacyConfigIncludeDrawProofFiles()
    {
        MainConfigModel defaults = new();
        Assert.True(defaults.General.Backup.IncludeProofs);

        const string legacyJson = """
                                  {
                                    "general": {
                                      "backup": {
                                        "include_history": false
                                      }
                                    }
                                  }
                                  """;

        MainConfigModel? restored = JsonSerializer.Deserialize<MainConfigModel>(legacyJson, ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.True(restored.General.Backup.IncludeProofs);
    }

    [Fact]
    public void LegacyCloudBackupFields_AreIgnoredAndLocalFieldsStillLoad()
    {
        const string legacyJson = """
                                  {
                                    "general": {
                                      "backup": {
                                        "cloud_auto_backup_enabled": true,
                                        "cloud_auto_backup_max_count": 9,
                                        "cloud_device_alias": "old-device",
                                        "cloud_include_images": true,
                                        "auto_backup_interval_days": 3
                                      }
                                    }
                                  }
                                  """;

        MainConfigModel? restored = JsonSerializer.Deserialize<MainConfigModel>(legacyJson, ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(3, restored.General.Backup.AutoBackupIntervalDays);
        Assert.True(restored.General.Backup.IncludeProofs);
    }
}
