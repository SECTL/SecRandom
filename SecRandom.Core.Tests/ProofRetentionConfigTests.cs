using System.Text.Json;
using ConfigServiceBase = global::SecRandom.Core.Abstraction.ConfigServiceBase;
using MainConfigModel = global::SecRandom.Core.Models.MainConfigModel;

namespace SecRandom.Core.Tests;

public class ProofRetentionConfigTests
{
    [Fact]
    public void MainConfig_DefaultProofRetentionIsThirtyDays()
    {
        Assert.Equal(30, new MainConfigModel().General.ProofRetention.RetentionDays);
    }

    [Fact]
    public void MainConfig_ProofRetentionRoundTripsThroughJson()
    {
        MainConfigModel config = new();
        config.General.ProofRetention.RetentionDays = 0;

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);
        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(0, restored.General.ProofRetention.RetentionDays);
    }
}
