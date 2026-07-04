namespace SecRandom.Core.Tests;

using System.Text.Json;
using ConfigServiceBase = global::SecRandom.Core.Abstraction.ConfigServiceBase;
using CrashRecoveryMode = global::SecRandom.Core.Enums.Configs.CrashRecoveryMode;
using MainConfigModel = global::SecRandom.Core.Models.MainConfigModel;

public class CrashRecoveryConfigTests
{
    [Fact]
    public void MainConfig_DefaultCrashRecoveryModePromptsAndRestarts()
    {
        MainConfigModel config = new();

        Assert.Equal(CrashRecoveryMode.PromptAndRestart, config.General.CrashRecovery.Mode);
    }

    [Fact]
    public void MainConfig_CrashRecoveryModeRoundTripsThroughJson()
    {
        MainConfigModel config = new();
        config.General.CrashRecovery.Mode = CrashRecoveryMode.RestartOnly;

        string json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);
        MainConfigModel? restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(CrashRecoveryMode.RestartOnly, restored.General.CrashRecovery.Mode);
    }
}
