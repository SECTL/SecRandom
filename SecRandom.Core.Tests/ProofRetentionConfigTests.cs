using System.Text.Json;
using ConfigServiceBase = global::SecRandom.Core.Abstraction.ConfigServiceBase;
using MainConfigModel = global::SecRandom.Core.Models.MainConfigModel;
using VerificationMode = global::SecRandom.Core.Enums.Configs.VerificationMode;

namespace SecRandom.Core.Tests;

public class ProofRetentionConfigTests
{
    [Fact]
    public void MainConfig_DefaultProofRetentionIsThirtyDays()
    {
        var retention = new MainConfigModel().General.ProofRetention;
        Assert.Equal(30, retention.RetentionDays);
        Assert.Equal(64L * 1024 * 1024, retention.MaximumStorageBytes);
    }

    [Fact]
    public void MainConfig_ProofRetentionRoundTripsThroughJson()
    {
        MainConfigModel config = new();
        config.General.ProofRetention.RetentionDays = 0;
        config.General.ProofRetention.MaximumStorageBytes = 128L * 1024 * 1024;

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);
        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(0, restored.General.ProofRetention.RetentionDays);
        Assert.Equal(128L * 1024 * 1024, restored.General.ProofRetention.MaximumStorageBytes);
    }

    [Fact]
    public void MainConfig_VerificationModeDefaultsToOrdinaryAndRoundTripsThroughJson()
    {
        MainConfigModel config = new();
        Assert.Equal(VerificationMode.Ordinary, config.General.Verification.Mode);

        config.General.Verification.Mode = VerificationMode.FormalNotarized;
        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);
        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(VerificationMode.FormalNotarized, restored.General.Verification.Mode);
    }
}
