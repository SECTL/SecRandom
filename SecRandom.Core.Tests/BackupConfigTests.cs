using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;

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
}
