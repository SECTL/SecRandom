using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public class FairDrawSettingsConfigTests
{
    [Fact]
    public void MainConfigModel_DeserializesMissingFairDrawFieldsWithDefaults()
    {
        const string json = """
                            {
                              "fair_draw_settings": {
                                "fair_draw": true
                              }
                            }
                            """;

        MainConfigModel? settings = JsonSerializer.Deserialize<MainConfigModel>(
            json,
            ConfigServiceBase.JsonOptions);

        Assert.NotNull(settings);
        Assert.True(settings.FairDrawSettings.FairDrawGroup);
        Assert.True(settings.FairDrawSettings.FairDrawGender);
        Assert.True(settings.FairDrawSettings.FairDrawTime);
        Assert.Equal(FrequencyFunctionMode.SquareRoot, settings.FairDrawSettings.FrequencyFunction);
        Assert.Equal(1.0, settings.FairDrawSettings.FrequencyWeight);
        Assert.True(settings.FairDrawSettings.EnableAvgGapProtection);
        Assert.Equal(1, settings.FairDrawSettings.GapThreshold);
        Assert.Equal(5, settings.FairDrawSettings.MinPoolSize);
        Assert.True(settings.FairDrawSettings.ColdStartEnabled);
        Assert.Equal(10, settings.FairDrawSettings.ColdStartRounds);
        Assert.False(settings.FairDrawSettings.ShieldEnabled);
        Assert.Equal(0.5, settings.FairDrawSettings.MinWeight);
        Assert.Equal(5.0, settings.FairDrawSettings.MaxWeight);
    }

    [Fact]
    public void MainConfigModel_RoundTripsAnimationStyle()
    {
        var config = new MainConfigModel
        {
            DefaultDrawSettings = new DefaultDrawSettingsConfig
            {
                AnimationStyle = DrawAnimationStyleMode.WheelFreeze
            },
            RollCallSettings = new RollCallSettingsConfig
            {
                AnimationStyle = DrawAnimationStyleMode.Fireworks
            },
            LotterySettings = new LotterySettingsConfig
            {
                AnimationStyle = DrawAnimationStyleMode.ShuffleDeck
            }
        };

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);
        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.Contains("animation_style", json);
        Assert.NotNull(restored);
        Assert.Equal(DrawAnimationStyleMode.WheelFreeze, restored.DefaultDrawSettings.AnimationStyle);
        Assert.Equal(DrawAnimationStyleMode.Fireworks, restored.RollCallSettings.AnimationStyle);
        Assert.Equal(DrawAnimationStyleMode.ShuffleDeck, restored.LotterySettings.AnimationStyle);
    }

    [Fact]
    public void CalculateStudentWeight_FairDrawDisabledUsesBaseWeight()
    {
        var first = new Student { Name = "A" };
        var second = new Student { Name = "B" };
        var config = BuildConfig(new FairDrawSettingsConfig
        {
            FairDraw = false,
            BaseWeight = 2.5
        });

        using var host = BuildHost(config, new TestProfileService());
        IAppHost.Host = host;
        var engine = new DrawEngine();

        var weights = engine.CalculateStudentWeight([first, second], new Dictionary<Student, History>
        {
            [first] = new() { TotalCount = 0 },
            [second] = new() { TotalCount = 10 }
        });

        Assert.All(weights, item => Assert.Equal(2.5, item.Weight));
    }

    [Fact]
    public void CalculateStudentWeight_FrequencyFunctionChangesRelativeWeight()
    {
        var lowCount = new Student { Name = "A" };
        var highCount = new Student { Name = "B" };
        var config = BuildConfig(new FairDrawSettingsConfig
        {
            FairDraw = true,
            FairDrawGroup = false,
            FairDrawGender = false,
            FairDrawTime = false,
            ColdStartEnabled = false,
            FrequencyFunction = FrequencyFunctionMode.Linear,
            FrequencyWeight = 1.0,
            BaseWeight = 1.0,
            MinWeight = 0.1,
            MaxWeight = 10.0
        });

        using var host = BuildHost(config, new TestProfileService());
        IAppHost.Host = host;
        var engine = new DrawEngine();

        var weights = engine.CalculateStudentWeight([lowCount, highCount], new Dictionary<Student, History>
        {
            [lowCount] = new() { TotalCount = 0 },
            [highCount] = new() { TotalCount = 9 }
        });

        Assert.True(weights[0].Weight > weights[1].Weight);
    }

    [Fact]
    public void CalculateStudentWeight_ShieldedStudentGetsZeroWeight()
    {
        var shielded = new Student { Name = "A" };
        var config = BuildConfig(new FairDrawSettingsConfig
        {
            FairDraw = true,
            FairDrawGroup = false,
            FairDrawGender = false,
            FairDrawTime = false,
            ColdStartEnabled = false,
            ShieldEnabled = true,
            ShieldTime = 10,
            ShieldTimeUnit = ShieldTimeUnit.Minutes
        });

        using var host = BuildHost(config, new TestProfileService());
        IAppHost.Host = host;
        var engine = new DrawEngine();

        var weights = engine.CalculateStudentWeight([shielded], new Dictionary<Student, History>
        {
            [shielded] = new() { LastDrawnTime = DateTime.Now.AddMinutes(-1) }
        });

        Assert.Equal(0, weights[0].Weight);
    }

    private static MainConfigModel BuildConfig(FairDrawSettingsConfig fairSettings)
    {
        return new MainConfigModel
        {
            FairDrawSettings = fairSettings,
            RollCallSettings = new RollCallSettingsConfig(),
            LotterySettings = new LotterySettingsConfig(),
            DefaultDrawSettings = new DefaultDrawSettingsConfig()
        };
    }

    private static IHost BuildHost(MainConfigModel config, IProfileService profile)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
                services.AddSingleton<IProfileService>(profile);
                services.AddSingleton<ConfigServiceBase>(new TestConfigService(config));
                services.AddSingleton<MainConfigHandler>();
            })
            .Build();
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;

        public override T LoadConfig<T>(T fallback)
        {
            return config is T typed ? typed : fallback;
        }

        public override void SaveConfig<T>(T config)
        {
        }

        public override void DeleteConfig<T>(T config)
        {
        }
    }

    private sealed class TestProfileService : IProfileService
    {
        public StudentList? CurrentStudentList { get; } = new();
        public StudentHistory? CurrentStudentHistory { get; } = new();
        public PrizeList? CurrentPrizeList { get; } = new();
        public PrizeHistory? CurrentPrizeHistory { get; } = new();
        public StudentListConfig? StudentListConfig => null;
        public StudentHistoryConfig? StudentHistoryConfig => null;
        public PrizeListConfig? PrizeListConfig => null;
        public PrizeHistoryConfig? PrizeHistoryConfig => null;
        public void LoadStudentProfile(string name, bool saveCurrent = true) { }
        public void LoadPrizeProfile(string name, bool saveCurrent = true) { }
        public void RecordStudentHistory(IReadOnlyList<Student> students, DateTime now, int requestedCount, string drawGroup = "", string drawGender = "", int drawMethod = 0, IReadOnlyDictionary<Student, double>? weights = null) { }
        public void RecordPrizeHistory(IReadOnlyList<Prize> prizes, DateTime now, int requestedCount) { }
        public void ClearCurrentStudentHistory() { }
        public void ClearCurrentPrizeHistory() { }
        public void SaveProfile() { }
    }
}
