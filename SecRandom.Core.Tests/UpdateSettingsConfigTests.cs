using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;

namespace SecRandom.Core.Tests;

public class UpdateSettingsConfigTests
{
    [Fact]
    public void MainConfig_DefaultLastUpdateCheckTimeIsUnset()
    {
        MainConfigModel config = new();

        Assert.Null(config.UpdateSettings.LastCheckTime);
        Assert.DoesNotContain("last_check_time", JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions));
    }

    [Fact]
    public void MainConfig_NormalizesLegacyPlaceholderLastUpdateCheckTime()
    {
        const string json = "{\"update_settings\":{\"last_check_time\":\"1970-01-01T08:00:00+00:00\"}}";

        MainConfigModel? config = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(config);
        Assert.Null(config.UpdateSettings.LastCheckTime);
    }

    [Fact]
    public void MainConfig_PreservesActualLastUpdateCheckTime()
    {
        var time = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Local);
        MainConfigModel config = new();
        config.UpdateSettings.LastCheckTime = time;

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);
        MainConfigModel? restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(time, restored.UpdateSettings.LastCheckTime);
    }

    [Fact]
    public void MainConfig_MigratesLegacyScheduledCheckToNotificationMode()
    {
        const string json = "{\"update_settings\":{\"auto_update_mode\":2}}";

        MainConfigModel? config = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(config);
        Assert.Equal(1, config.UpdateSettings.AutoUpdateMode);
        Assert.Equal(1, config.UpdateSettings.UpdateModeVersion);
    }

    [Fact]
    public void MainConfig_DefaultUpdateModeIsAutomaticInstall()
    {
        MainConfigModel config = new();

        Assert.Equal(3, config.UpdateSettings.AutoUpdateMode);
    }
}
