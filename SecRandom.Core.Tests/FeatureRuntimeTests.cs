using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.FeatureConfiguration;
using SecRandom.Services.ControlNode;

namespace SecRandom.Core.Tests;

public sealed class FeatureRuntimeTests
{
    [Fact]
    public void LotteryRequiresBothFeatureAndExistingUserSetting()
    {
        var config = new MainConfigModel();
        config.MoreSettings.LotteryEnabled = true;
        using var disabled = CreateProvider(config, []);
        Assert.False(disabled.GetRequiredService<IFeatureAvailabilityService>().IsLotteryEnabled);
        Assert.True(config.MoreSettings.LotteryEnabled);

        using var enabled = CreateProvider(config, [StartupFeatureCatalog.Lottery]);
        var availability = enabled.GetRequiredService<IFeatureAvailabilityService>();
        Assert.True(availability.IsLotteryEnabled);
        config.MoreSettings.LotteryEnabled = false;
        Assert.False(availability.IsLotteryEnabled);
    }

    [Fact]
    public async Task DisabledVoiceRejectsRemoteAnnouncementEvenWhenOldSettingIsOn()
    {
        var config = new MainConfigModel();
        config.VoiceSettings.VoiceEnable = true;
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(config));
        var service = new ControlMediaPlayHandler(handler, NullLogger<ControlMediaPlayHandler>.Instance);
        using var request = JsonDocument.Parse("{\"text\":\"hello\"}");
        var result = await service.ExecuteAsync(request.RootElement, CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Equal("media_disabled", result.Reason);
        Assert.True(config.VoiceSettings.VoiceEnable);
    }

    private static ServiceProvider CreateProvider(MainConfigModel config, string[] enabled)
    {
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(config));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreRuntimeServices();
        services.AddSingleton(handler);
        services.AddSingleton(new FeatureRuntimeSnapshot(enabled));
        return services.BuildServiceProvider();
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) { }
        public override void DeleteConfig<T>(T value) { }
    }
}
