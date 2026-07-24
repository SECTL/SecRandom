using System.Collections.ObjectModel;
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
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public sealed class FairDrawAlgorithmTests
{
    [Fact]
    public void FairDraw_ExcludesAboveAverageCandidatesWhenLowerCandidatesCanSatisfyDraw()
    {
        var low = new Student { Name = "Low", RecordId = Guid.NewGuid() };
        var average = new Student { Name = "Average", RecordId = Guid.NewGuid() };
        var high = new Student { Name = "High", RecordId = Guid.NewGuid() };
        var history = new StudentHistory
        {
            Students =
            {
                [low.RecordId.ToString("D")] = new History { TotalCount = 0 },
                [average.RecordId.ToString("D")] = new History { TotalCount = 2 },
                [high.RecordId.ToString("D")] = new History { TotalCount = 4 }
            }
        };
        var config = CreateConfig(new FairDrawSettingsConfig
        {
            FairDraw = true,
            FairDrawGroup = false,
            FairDrawGender = false,
            FairDrawTime = false,
            ColdStartEnabled = false,
            EnableAvgGapProtection = true,
            GapThreshold = 10,
            MinWeight = 0,
            MaxWeight = 10
        });
        var students = new StudentList { Students = [low, average, high] };

        using var host = CreateHost(config, new TestProfileService(history, students));
        var engine = CreateEngine(host);

        var localResult = engine.DrawPreparedStudents(1, [low, average, high], DrawSettingsType.RollCall);
        var verificationInput = engine.CreateStudentVerificationInput(1, [low, average, high], DrawSettingsType.RollCall);

        Assert.True(localResult.IsSuccess);
        Assert.DoesNotContain(high, localResult.Result);
        Assert.DoesNotContain(verificationInput.Candidates, candidate => candidate.RecordId == high.RecordId);
        using var audit = JsonDocument.Parse(verificationInput.AuditPayload);
        Assert.Equal("secrandom-anonymous-audit/v2", audit.RootElement.GetProperty("format").GetString());
        Assert.All(audit.RootElement.GetProperty("candidates").EnumerateArray(), candidate =>
            Assert.True(Guid.TryParse(candidate.GetProperty("recordId").GetString(), out _)));
        Assert.True(audit.RootElement.GetProperty("fairness").GetProperty("averageGapProtectionApplied").GetBoolean());
        Assert.Equal(3, audit.RootElement.GetProperty("fairness").GetProperty("candidateCountBeforeAverageGapProtection").GetInt32());
        Assert.Equal(2, audit.RootElement.GetProperty("fairness").GetProperty("candidateCountAfterAverageGapProtection").GetInt32());
    }

    [Fact]
    public void CreateStudentVerificationInput_UsesCourseScopedBalanceWeights()
    {
        var groupA = new Student { Name = "A", Group = "A", RecordId = Guid.NewGuid() };
        var groupB = new Student { Name = "B", Group = "B", RecordId = Guid.NewGuid() };
        var history = new StudentHistory();
        history.GroupStats["A"] = 100;
        history.Students["legacy"] = new History
        {
            Histories = new ObservableCollection<HistoryItem>(
                Enumerable.Range(0, 10)
                    .Select(_ => new HistoryItem { CourseName = "数学", RecordGroup = "B" }))
        };
        var config = CreateConfig(new FairDrawSettingsConfig
        {
            FairDraw = true,
            FairDrawGroup = true,
            FairDrawGender = false,
            FairDrawTime = false,
            ColdStartEnabled = false,
            EnableAvgGapProtection = false,
            FrequencyWeight = 0,
            BaseWeight = 0,
            GroupWeight = 1,
            MinWeight = 0,
            MaxWeight = 10
        });
        var students = new StudentList { Students = [groupA, groupB] };

        using var host = CreateHost(config, new TestProfileService(history, students));

        var input = CreateEngine(host).CreateStudentVerificationInput(1, [groupA, groupB], DrawSettingsType.RollCall, "数学");

        Assert.True(input.Candidates.Single(candidate => candidate.RecordId == groupA.RecordId).WeightMicros
                    > input.Candidates.Single(candidate => candidate.RecordId == groupB.RecordId).WeightMicros);
    }

    [Fact]
    public void MobileDesktopDefaultsPolicy_MatchesFreshDesktopDefaultFairWeights()
    {
        var first = new Student { Name = "First", RecordId = Guid.NewGuid(), Group = "A", Gender = "男" };
        var second = new Student { Name = "Second", RecordId = Guid.NewGuid(), Group = "B", Gender = "女" };
        var history = new StudentHistory
        {
            TotalStats = 20,
            Students =
            {
                [first.RecordId.ToString("D")] = new History { TotalCount = 3 },
                [second.RecordId.ToString("D")] = new History { TotalCount = 0 }
            },
            GroupStats =
            {
                ["A"] = 3,
                ["B"] = 0
            },
            GenderStatus =
            {
                ["男"] = 3,
                ["女"] = 0
            }
        };
        var config = CreateConfig(new FairDrawSettingsConfig());

        using var host = CreateHost(config, new TestProfileService(history, new StudentList { Students = [first, second] }));
        var engine = CreateEngine(host);

        var desktopWeights = engine.CalculateStudentWeight([first, second]);
        var mobileWeights = engine.CalculateStudentWeightWithMobileDesktopDefaults([first, second]);

        Assert.Equal(
            desktopWeights.Select(candidate => (candidate.Candidate.RecordId, candidate.Weight)),
            mobileWeights.Select(candidate => (candidate.Candidate.RecordId, candidate.Weight)));
    }

    [Fact]
    public void MobileDesktopDefaultsPolicy_IgnoresPersistedFairDrawSettings()
    {
        var first = new Student { Name = "First", RecordId = Guid.NewGuid(), Group = "A", Gender = "男" };
        var second = new Student { Name = "Second", RecordId = Guid.NewGuid(), Group = "B", Gender = "女" };
        var history = new StudentHistory
        {
            TotalStats = 20,
            Students =
            {
                [first.RecordId.ToString("D")] = new History { TotalCount = 5 },
                [second.RecordId.ToString("D")] = new History { TotalCount = 0 }
            }
        };
        var config = CreateConfig(new FairDrawSettingsConfig
        {
            FairDraw = false,
            FairDrawGroup = false,
            FairDrawGender = false,
            FairDrawTime = false,
            FrequencyFunction = FrequencyFunctionMode.Linear,
            EnableAvgGapProtection = false,
            ColdStartEnabled = false,
            BaseWeight = 9,
            MinWeight = 9,
            MaxWeight = 9,
            GroupWeight = 0,
            GenderWeight = 0,
            TimeWeight = 0
        });

        using var host = CreateHost(config, new TestProfileService(history, new StudentList { Students = [first, second] }));
        var engine = CreateEngine(host);

        var desktopWeights = engine.CalculateStudentWeight([first, second]);
        var mobileWeights = engine.CalculateStudentWeightWithMobileDesktopDefaults([first, second]);

        Assert.All(desktopWeights, candidate => Assert.Equal(9, candidate.Weight));
        Assert.NotEqual(
            desktopWeights.Select(candidate => candidate.Weight),
            mobileWeights.Select(candidate => candidate.Weight));
    }

    [Fact]
    public void RandomStudentVerification_UsesUniformSamplingProfile()
    {
        var first = new Student { Name = "First", RecordId = Guid.NewGuid() };
        var second = new Student { Name = "Second", RecordId = Guid.NewGuid() };
        var config = CreateConfig(new FairDrawSettingsConfig());
        config.RollCallSettings.DrawType = DrawType.Random;
        config.RollCallSettings.DrawMode = DrawMode.HalfRepeat;
        config.RollCallSettings.HalfRepeat = 2;

        using var host = CreateHost(config, new TestProfileService(new StudentHistory(), new StudentList { Students = [first, second] }));

        var input = CreateEngine(host).CreateStudentVerificationInput(1, [first, second], DrawSettingsType.RollCall);

        Assert.Equal(VerificationSamplingMode.WeightedWithoutReplacement, input.SamplingMode);
        Assert.Equal(VerificationAlgorithmProfile.StudentRandomHalfRepeat, input.AlgorithmProfile);
        Assert.All(input.Candidates, candidate => Assert.Equal(1_000_000, candidate.WeightMicros));
    }

    [Fact]
    public void RandomMobileDraw_RemainsUnitWeightedAndScripted()
    {
        var first = new Student { Name = "First", RecordId = Guid.NewGuid() };
        var second = new Student { Name = "Second", RecordId = Guid.NewGuid() };
        var config = CreateConfig(new FairDrawSettingsConfig
        {
            FairDraw = false,
            BaseWeight = 99,
            MinWeight = 99,
            MaxWeight = 99,
            EnableAvgGapProtection = false,
            ColdStartEnabled = false,
            FairDrawGroup = false,
            FairDrawGender = false,
            FairDrawTime = false
        });
        config.RollCallSettings.DrawType = DrawType.Random;

        using var host = CreateHost(config, new TestProfileService(new StudentHistory(), new StudentList { Students = [first, second] }));
        var engine = CreateEngine(host, new WeightedScriptedRandomSource(0.75));

        var output = engine.DrawPreparedStudents(1, [first, second], DrawSettingsType.RollCall);

        Assert.True(output.IsSuccess);
        Assert.Equal(second, output.Result.Single());
    }

    [Fact]
    public void RandomStudentVerification_UsesInternalRuleProfileWhenCandidateWeightsAreOverridden()
    {
        var student = new Student { Name = "First", RecordId = Guid.NewGuid() };
        student.AttachedObjects[Guid.Parse(GlobalConstants.BehindSceneAttachedSettings)] = new BehindSceneAttachedSettings
        {
            IsAttachSettingsEnabled = true,
            Probability = 50
        };
        var config = CreateConfig(new FairDrawSettingsConfig());
        config.RollCallSettings.DrawType = DrawType.Random;
        config.RollCallSettings.DrawMode = DrawMode.NoRepeat;

        using var host = CreateHost(config, new TestProfileService(new StudentHistory(), new StudentList { Students = [student] }));

        var input = CreateEngine(host).CreateStudentVerificationInput(1, [student], DrawSettingsType.RollCall);

        Assert.Equal(VerificationAlgorithmProfile.StudentRandomInternalRuleNoRepeat, input.AlgorithmProfile);
        Assert.Equal(500_000, input.Candidates.Single().WeightMicros);
    }

    [Fact]
    public void StudentVerification_CanIgnoreInternalRules()
    {
        var student = new Student { Name = "First", RecordId = Guid.NewGuid() };
        student.AttachedObjects[Guid.Parse(GlobalConstants.BehindSceneAttachedSettings)] = new BehindSceneAttachedSettings
        {
            IsAttachSettingsEnabled = true,
            Probability = 50
        };
        var config = CreateConfig(new FairDrawSettingsConfig());
        config.RollCallSettings.DrawType = DrawType.Random;
        config.RollCallSettings.DrawMode = DrawMode.NoRepeat;

        using var host = CreateHost(config, new TestProfileService(new StudentHistory(), new StudentList { Students = [student] }));

        var input = CreateEngine(host).CreateStudentVerificationInput(
            1,
            [student],
            DrawSettingsType.RollCall,
            courseName: "",
            includeInternalRules: false);

        Assert.Equal(VerificationAlgorithmProfile.StudentRandomNoRepeat, input.AlgorithmProfile);
        Assert.Equal(1_000_000, input.Candidates.Single().WeightMicros);
        using var audit = JsonDocument.Parse(input.AuditPayload);
        Assert.False(audit.RootElement.GetProperty("internalSettingsApplied").GetBoolean());
    }

    [Fact]
    public void CountLottery_UsesInventoryPermutationRatherThanPrizeWeights()
    {
        var first = new Prize { Name = "First", RecordId = Guid.NewGuid(), Count = 2, Weight = 100 };
        var second = new Prize { Name = "Second", RecordId = Guid.NewGuid(), Count = 1, Weight = 0.01 };
        var prizes = new PrizeList { Prizes = [first, second] };
        var config = CreateConfig(new FairDrawSettingsConfig());
        config.LotterySettings = new LotterySettingsConfig
        {
            DrawType = LotteryDrawType.Count,
            DrawMode = DrawMode.Repeat
        };

        using var host = CreateHost(config, new TestProfileService(new StudentHistory(), new StudentList(), prizes));
        var engine = CreateEngine(host, new ScriptedRandomSource(2, 0));

        var local = engine.DrawPrize(2, _ => true);
        var input = engine.CreatePrizeVerificationInput(2, new Dictionary<string, int>());

        Assert.True(local.IsSuccess);
        Assert.Equal([second, first], local.Result);
        Assert.Equal(VerificationSamplingMode.InventoryPermutation, input.SamplingMode);
        Assert.Equal(VerificationAlgorithmProfile.LotteryInventoryCount, input.AlgorithmProfile);
        Assert.All(input.Candidates, candidate => Assert.Equal(1_000_000, candidate.WeightMicros));
    }

    [Fact]
    public void CountLotteryVerification_AuditsZeroProbabilityInternalRules()
    {
        var blocked = new Prize { Name = "Blocked", RecordId = Guid.NewGuid(), Count = 1 };
        blocked.AttachedObjects[Guid.Parse(GlobalConstants.BehindSceneAttachedSettings)] = new BehindSceneAttachedSettings
        {
            IsAttachSettingsEnabled = true,
            Probability = 0
        };
        var available = new Prize { Name = "Available", RecordId = Guid.NewGuid(), Count = 1 };
        var config = CreateConfig(new FairDrawSettingsConfig());
        config.LotterySettings = new LotterySettingsConfig { DrawType = LotteryDrawType.Count };

        using var host = CreateHost(config, new TestProfileService(
            new StudentHistory(), new StudentList(), new PrizeList { Prizes = [blocked, available] }));

        var input = CreateEngine(host).CreatePrizeVerificationInput(1, new Dictionary<string, int>());
        using var audit = JsonDocument.Parse(input.AuditPayload);

        Assert.Equal(VerificationAlgorithmProfile.LotteryCountInternalRule, input.AlgorithmProfile);
        Assert.True(audit.RootElement.GetProperty("internalSettingsApplied").GetBoolean());
        Assert.Equal(0, audit.RootElement.GetProperty("internalCandidateCount").GetInt32());
        Assert.Equal(1, audit.RootElement.GetProperty("internalExcludedCandidateCount").GetInt32());
    }

    [Fact]
    public void CountLotteryVerification_CanIgnoreInternalRules()
    {
        var blocked = new Prize { Name = "Blocked", RecordId = Guid.NewGuid(), Count = 1 };
        blocked.AttachedObjects[Guid.Parse(GlobalConstants.BehindSceneAttachedSettings)] = new BehindSceneAttachedSettings
        {
            IsAttachSettingsEnabled = true,
            Probability = 0
        };
        var available = new Prize { Name = "Available", RecordId = Guid.NewGuid(), Count = 1 };
        var config = CreateConfig(new FairDrawSettingsConfig());
        config.LotterySettings = new LotterySettingsConfig { DrawType = LotteryDrawType.Count };

        using var host = CreateHost(config, new TestProfileService(
            new StudentHistory(), new StudentList(), new PrizeList { Prizes = [blocked, available] }));

        var input = CreateEngine(host).CreatePrizeVerificationInput(
            1,
            new Dictionary<string, int>(),
            includeInternalRules: false);

        Assert.Equal(VerificationAlgorithmProfile.LotteryInventoryCount, input.AlgorithmProfile);
        Assert.Equal(2, input.Candidates.Count);
        Assert.All(input.Candidates, candidate => Assert.Equal(1_000_000, candidate.WeightMicros));
        using var audit = JsonDocument.Parse(input.AuditPayload);
        Assert.False(audit.RootElement.GetProperty("internalSettingsApplied").GetBoolean());
        Assert.Equal(0, audit.RootElement.GetProperty("internalExcludedCandidateCount").GetInt32());
    }

    private static MainConfigModel CreateConfig(FairDrawSettingsConfig fairSettings)
    {
        return new MainConfigModel
        {
            FairDrawSettings = fairSettings,
            RollCallSettings = new RollCallSettingsConfig(),
            LotterySettings = new LotterySettingsConfig(),
            DefaultDrawSettings = new DefaultDrawSettingsConfig()
        };
    }

    private static IHost CreateHost(MainConfigModel config, IProfileService profile)
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

    private sealed class ScriptedRandomSource(params int[] values) : IRandomSource
    {
        private readonly Queue<int> _values = new(values);

        public int NextInt32(int maxExclusive)
        {
            var value = _values.Dequeue();
            if (value < 0 || value >= maxExclusive)
                throw new InvalidOperationException("The test random value is outside the requested bound.");
            return value;
        }

        public double NextDouble() => throw new InvalidOperationException("Weighted sampling must not use NextDouble in this test.");
    }

    private sealed class WeightedScriptedRandomSource(params double[] values) : IRandomSource
    {
        private readonly Queue<double> _values = new(values);

        public int NextInt32(int maxExclusive) => throw new InvalidOperationException("This test uses weighted sampling.");

        public double NextDouble() => _values.Dequeue();
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T config) { }
        public override void DeleteConfig<T>(T config) { }
    }

    private sealed class TestProfileService(
        StudentHistory history,
        StudentList students,
        PrizeList? prizes = null,
        PrizeHistory? prizeHistory = null) : IProfileService
    {
        public StudentList? CurrentStudentList { get; } = students;
        public StudentHistory? CurrentStudentHistory { get; } = history;
        public PrizeList? CurrentPrizeList { get; } = prizes ?? new();
        public PrizeHistory? CurrentPrizeHistory { get; } = prizeHistory ?? new();
        public StudentListConfig? StudentListConfig => null;
        public StudentHistoryConfig? StudentHistoryConfig => null;
        public PrizeListConfig? PrizeListConfig => null;
        public PrizeHistoryConfig? PrizeHistoryConfig => null;
        public void LoadStudentProfile(string name, bool saveCurrent = true) { }
        public void LoadPrizeProfile(string name, bool saveCurrent = true) { }
        public void RecordStudentHistory(IReadOnlyList<Student> students, DateTime now, int requestedCount, string drawGroup = "", string drawGender = "", int drawMethod = 0, IReadOnlyDictionary<Student, double>? weights = null, string courseName = "", string? drawRoundId = null) { }
        public void RecordPrizeHistory(IReadOnlyList<Prize> prizes, DateTime now, int requestedCount, int drawMethod = 0, string? drawRoundId = null) { }
        public void ClearCurrentStudentHistory() { }
        public void ClearCurrentPrizeHistory() { }
        public void SaveProfile() { }
    }

}
