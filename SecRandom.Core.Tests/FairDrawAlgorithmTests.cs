using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
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
        IAppHost.Host = host;
        var engine = new DrawEngine();

        var localResult = engine.DrawPreparedStudents(1, [low, average, high], DrawSettingsType.RollCall);
        var verificationInput = engine.CreateStudentVerificationInput(1, [low, average, high], DrawSettingsType.RollCall);

        Assert.True(localResult.IsSuccess);
        Assert.DoesNotContain(high, localResult.Result);
        Assert.DoesNotContain(verificationInput.Candidates, candidate => candidate.RecordId == high.RecordId);
        using var audit = JsonDocument.Parse(verificationInput.AuditPayload);
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
        IAppHost.Host = host;

        var input = new DrawEngine().CreateStudentVerificationInput(1, [groupA, groupB], DrawSettingsType.RollCall, "数学");

        Assert.True(input.Candidates.Single(candidate => candidate.RecordId == groupA.RecordId).WeightMicros
                    > input.Candidates.Single(candidate => candidate.RecordId == groupB.RecordId).WeightMicros);
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

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T config) { }
        public override void DeleteConfig<T>(T config) { }
    }

    private sealed class TestProfileService(StudentHistory history, StudentList students) : IProfileService
    {
        public StudentList? CurrentStudentList { get; } = students;
        public StudentHistory? CurrentStudentHistory { get; } = history;
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
