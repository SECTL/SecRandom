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
    public void CloudBackupDefaults_StayOffAndKeepImagesOutOfTheUpload()
    {
        MainConfigModel defaults = new();
        BackupConfig backup = defaults.General.Backup;

        Assert.False(backup.CloudAutoBackupEnabled);
        Assert.Equal(5, backup.CloudAutoBackupMaxCount);
        Assert.Equal(string.Empty, backup.CloudDeviceAlias);
        Assert.False(backup.CloudIncludeImages);
        Assert.False(backup.CloudIncludeAudio);
        Assert.True(backup.CloudIncludeConfig);
        Assert.True(backup.CloudIncludeList);
        Assert.True(backup.CloudIncludeHistory);
    }
}
