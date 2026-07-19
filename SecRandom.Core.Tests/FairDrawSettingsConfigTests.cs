using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Interfaces;
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
                AnimationStyle = DrawAnimationStyleMode.DirectRotate
            },
            RollCallSettings = new RollCallSettingsConfig
            {
                AnimationStyle = DrawAnimationStyleMode.FadeFloat
            },
            LotterySettings = new LotterySettingsConfig
            {
                AnimationStyle = DrawAnimationStyleMode.HorizontalShake
            }
        };

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);
        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.Contains("animation_style", json);
        Assert.NotNull(restored);
        Assert.Equal(DrawAnimationStyleMode.DirectRotate, restored.DefaultDrawSettings.AnimationStyle);
        Assert.Equal(DrawAnimationStyleMode.FadeFloat, restored.RollCallSettings.AnimationStyle);
        Assert.Equal(DrawAnimationStyleMode.HorizontalShake, restored.LotterySettings.AnimationStyle);
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
        var engine = CreateEngine(host);

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
        var engine = CreateEngine(host);

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
        var engine = CreateEngine(host);

        var weights = engine.CalculateStudentWeight([shielded], new Dictionary<Student, History>
        {
            [shielded] = new() { LastDrawnTime = DateTime.Now.AddMinutes(-1) }
        });

        Assert.Equal(0, weights[0].Weight);
    }

    [Fact]
    public void CalculateStudentWeight_CourseScopeUsesOnlyCourseGroupAndGenderHistory()
    {
        var groupA = new Student { Name = "A", Group = "A", Gender = "男" };
        var groupB = new Student { Name = "B", Group = "B", Gender = "女" };
        var history = new StudentHistory();
        history.GroupStats["A"] = 100;
        history.GenderStatus["男"] = 100;
        history.Students["legacy"] = new History
        {
            Histories = [new HistoryItem { CourseName = "数学", RecordGroup = "A", RecordGender = "男" }]
        };
        var config = BuildConfig(new FairDrawSettingsConfig
        {
            FairDraw = true,
            FairDrawGroup = true,
            FairDrawGender = true,
            FairDrawTime = false,
            ColdStartEnabled = false,
            FrequencyWeight = 0,
            BaseWeight = 0,
            GroupWeight = 1,
            GenderWeight = 1,
            MinWeight = 0,
            MaxWeight = 10
        });

        using var host = BuildHost(config, new TestProfileService(history));
        var weights = CreateEngine(host).CalculateStudentWeight(
            [groupA, groupB],
            new Dictionary<Student, History> { [groupA] = new(), [groupB] = new() },
            "数学");

        Assert.True(weights.Single(item => item.Candidate == groupB).Weight
                    > weights.Single(item => item.Candidate == groupA).Weight);
    }

    [Fact]
    public void MobileDesktopDefaults_IgnoresPersistedFairDrawSettingsAndKeepsFilteredWeights()
    {
        var first = new Student { Name = "A", RecordId = Guid.NewGuid() };
        var second = new Student { Name = "B", RecordId = Guid.NewGuid() };
        var overdrawn = new Student { Name = "C", RecordId = Guid.NewGuid() };
        var history = new StudentHistory
        {
            Students =
            {
                [first.RecordId.ToString("D")] = new History { TotalCount = 0 },
                [second.RecordId.ToString("D")] = new History { TotalCount = 0 },
                [overdrawn.RecordId.ToString("D")] = new History { TotalCount = 3 }
            }
        };
        var configA = BuildConfig(new FairDrawSettingsConfig
        {
            FairDraw = false,
            EnableAvgGapProtection = false,
            GapThreshold = 99,
            FairDrawGroup = false,
            FairDrawGender = false,
            FairDrawTime = false,
            ColdStartEnabled = false,
            BaseWeight = 20,
            MinWeight = 20,
            MaxWeight = 20
        });
        configA.RollCallSettings.DrawType = DrawType.Fair;

        var configB = BuildConfig(new FairDrawSettingsConfig
        {
            FairDraw = true,
            EnableAvgGapProtection = true,
            GapThreshold = 1,
            FairDrawGroup = true,
            FairDrawGender = true,
            FairDrawTime = true,
            ColdStartEnabled = true,
            BaseWeight = 1,
            MinWeight = 0.5,
            MaxWeight = 5.0
        });
        configB.RollCallSettings.DrawType = DrawType.Fair;

        using var hostA = BuildHost(configA, new TestProfileService(history));
        using var hostB = BuildHost(configB, new TestProfileService(history));
        var engineA = CreateEngine(hostA, new ScriptedRandomSource(0));
        var engineB = CreateEngine(hostB, new ScriptedRandomSource(0));

        var preparedA = engineA.PrepareStudentsForMobileDesktopDefaults(1, [first, second, overdrawn], DrawSettingsType.RollCall, DrawType.Fair);
        var preparedB = engineB.PrepareStudentsForMobileDesktopDefaults(1, [first, second, overdrawn], DrawSettingsType.RollCall, DrawType.Fair);
        var outputA = engineA.DrawPreparedStudents(preparedA, 1);
        var outputB = engineB.DrawPreparedStudents(preparedB, 1);

        Assert.Equal(preparedA.UsableCandidates, preparedB.UsableCandidates);
        Assert.Equal(preparedA.WeightedCandidates.Select(candidate => (candidate.Candidate, candidate.Weight)), preparedB.WeightedCandidates.Select(candidate => (candidate.Candidate, candidate.Weight)));
        Assert.DoesNotContain(overdrawn, preparedA.UsableCandidates);
        Assert.True(outputA.IsSuccess);
        Assert.True(outputB.IsSuccess);
        Assert.Equal(outputA.Result, outputB.Result);
        Assert.DoesNotContain(overdrawn, outputA.Result);
    }

    [Fact]
    public void CreateStudentVerificationInput_AppliesAverageGapProtection()
    {
        var first = new Student { Name = "A", RecordId = Guid.NewGuid() };
        var second = new Student { Name = "B", RecordId = Guid.NewGuid() };
        var overdrawn = new Student { Name = "C", RecordId = Guid.NewGuid() };
        var history = new StudentHistory
        {
            Students =
            {
                [first.RecordId.ToString("D")] = new History { TotalCount = 0 },
                [second.RecordId.ToString("D")] = new History { TotalCount = 0 },
                [overdrawn.RecordId.ToString("D")] = new History { TotalCount = 3 }
            }
        };
        var config = BuildConfig(new FairDrawSettingsConfig
        {
            FairDraw = true,
            EnableAvgGapProtection = true,
            GapThreshold = 1,
            FairDrawGroup = false,
            FairDrawGender = false,
            FairDrawTime = false,
            ColdStartEnabled = false
        });
        config.RollCallSettings.DrawType = DrawType.Fair;

        using var host = BuildHost(config, new TestProfileService(history));

        var input = CreateEngine(host).CreateStudentVerificationInput(
            1,
            [first, second, overdrawn],
            DrawSettingsType.RollCall);

        Assert.Equal(2, input.Candidates.Count);
        Assert.DoesNotContain(input.Candidates, candidate => candidate.RecordId == overdrawn.RecordId);
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

    private static DrawEngine CreateEngine(IHost host, IRandomSource? randomSource = null)
    {
        return new DrawEngine(
            host.Services.GetRequiredService<MainConfigHandler>(),
            host.Services.GetRequiredService<IProfileService>(),
            host.Services.GetRequiredService<ILogger<DrawEngine>>(),
            randomSource);
    }

    private sealed class ScriptedRandomSource(params double[] values) : IRandomSource
    {
        private readonly Queue<double> _values = new(values);

        public int NextInt32(int maxExclusive) => throw new InvalidOperationException("This test uses weighted sampling.");

        public double NextDouble() => _values.Dequeue();
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

    private sealed class TestProfileService(StudentHistory? studentHistory = null) : IProfileService
    {
        public StudentList? CurrentStudentList { get; } = new();
        public StudentHistory? CurrentStudentHistory { get; } = studentHistory ?? new();
        public PrizeList? CurrentPrizeList { get; } = new();
        public PrizeHistory? CurrentPrizeHistory { get; } = new();
        public StudentListConfig? StudentListConfig => null;
        public StudentHistoryConfig? StudentHistoryConfig => null;
        public PrizeListConfig? PrizeListConfig => null;
        public PrizeHistoryConfig? PrizeHistoryConfig => null;
        public void LoadStudentProfile(string name, bool saveCurrent = true) { }
        public void LoadPrizeProfile(string name, bool saveCurrent = true) { }
        public void RecordStudentHistory(IReadOnlyList<Student> students, DateTime now, int requestedCount, string drawGroup = "", string drawGender = "", int drawMethod = 0, IReadOnlyDictionary<Student, double>? weights = null, string courseName = "") { }
        public void RecordPrizeHistory(IReadOnlyList<Prize> prizes, DateTime now, int requestedCount) { }
        public void ClearCurrentStudentHistory() { }
        public void ClearCurrentPrizeHistory() { }
        public void SaveProfile() { }
    }
}
