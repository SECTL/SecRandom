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
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     Read-side scope semantics of the history-group dimension: which records a draw's weight pool is built
///     from. Assertions are structural — exact record counts, exact timestamps, object identity — never sampled
///     probabilities. The group axis is expected to behave exactly like the linkage-course axis it was modelled
///     on (<c>DrawEngine.Filter.cs</c>), with the two axes combining by intersection.
/// </summary>
public sealed class HistoryGroupScopeTests
{
    private static readonly DateTime FirstGroupADraw = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SecondGroupADraw = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime GroupBDraw = new(2026, 3, 3, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void GroupScope_CountsOnlyThatGroupsRecordsAndReportsItsOwnLastDrawTime()
    {
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        var config = CreateConfig();
        var history = CreateStudentHistory(student, [
            (string.Empty, "A", FirstGroupADraw),
            (string.Empty, "A", SecondGroupADraw),
            (string.Empty, "B", GroupBDraw)
        ]);

        using var host = CreateHost(config, new TestProfileService(history, new StudentList { Students = [student] }));
        var engine = CreateEngine(host);

        var scopedToA = engine.PrepareStudentsForDraw(1, [student], CreatePolicy(config), string.Empty, "A")
            .HistoryCache[student];
        var scopedToB = engine.PrepareStudentsForDraw(1, [student], CreatePolicy(config), string.Empty, "B")
            .HistoryCache[student];

        Assert.Equal(2, scopedToA.TotalCount);
        Assert.Equal(2, scopedToA.Histories.Count);
        Assert.Equal(SecondGroupADraw, scopedToA.LastDrawnTime);
        Assert.All(scopedToA.Histories, item => Assert.Equal("A", item.HistoryGroup));

        Assert.Equal(1, scopedToB.TotalCount);
        Assert.Equal(GroupBDraw, scopedToB.LastDrawnTime);
    }

    [Fact]
    public void BothAxesEmpty_ReturnsTheLiveHistoryInstanceInsteadOfRebuildingIt()
    {
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        var config = CreateConfig();
        var history = CreateStudentHistory(student, [
            (string.Empty, "A", FirstGroupADraw),
            (string.Empty, "B", GroupBDraw)
        ]);
        var live = history.Students[RecordKey(student)];

        using var host = CreateHost(config, new TestProfileService(history, new StudentList { Students = [student] }));
        var engine = CreateEngine(host);

        var unscoped = engine.PrepareStudentsForDraw(1, [student], CreatePolicy(config), string.Empty, string.Empty)
            .HistoryCache[student];

        // 功能关闭（或这次抽取不带任何范围）必须与加组之前逐字节同义：同一个对象，计 2 条，不做副本。
        Assert.Same(live, unscoped);
        Assert.Equal(2, unscoped.TotalCount);
        Assert.Equal(GroupBDraw, unscoped.LastDrawnTime);
    }

    [Fact]
    public void CourseAndGroup_CombineByIntersection()
    {
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        var config = CreateConfig();
        var history = CreateStudentHistory(student, [
            ("数学", "A", FirstGroupADraw),
            ("数学", "B", SecondGroupADraw),
            ("语文", "A", GroupBDraw),
            (string.Empty, string.Empty, GroupBDraw)
        ]);

        using var host = CreateHost(config, new TestProfileService(history, new StudentList { Students = [student] }));
        var engine = CreateEngine(host);
        var policy = CreatePolicy(config);

        Assert.Equal(1, engine.PrepareStudentsForDraw(1, [student], policy, "数学", "A").HistoryCache[student].TotalCount);
        Assert.Equal(2, engine.PrepareStudentsForDraw(1, [student], policy, "数学", string.Empty).HistoryCache[student].TotalCount);
        Assert.Equal(2, engine.PrepareStudentsForDraw(1, [student], policy, string.Empty, "A").HistoryCache[student].TotalCount);
        Assert.Equal(4, engine.PrepareStudentsForDraw(1, [student], policy, string.Empty, string.Empty).HistoryCache[student].TotalCount);
    }

    [Fact]
    public void GroupWithoutAnyMatchingRecord_YieldsZeroCountAndMinDrawTime()
    {
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        var config = CreateConfig();
        var history = CreateStudentHistory(student, [("数学", "A", FirstGroupADraw)]);

        using var host = CreateHost(config, new TestProfileService(history, new StudentList { Students = [student] }));
        var engine = CreateEngine(host);

        var empty = engine.PrepareStudentsForDraw(1, [student], CreatePolicy(config), string.Empty, "空组")
            .HistoryCache[student];

        // 新上下文等于人人平起平坐：计 0 条，且没有「最后一次抽取时间」。
        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(empty.Histories);
        Assert.Equal(DateTime.MinValue, empty.LastDrawnTime);
    }

    [Fact]
    public void EmptyGroupValue_LeavesTheGroupAxisUnfiltered()
    {
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        var config = CreateConfig();
        var history = CreateStudentHistory(student, [
            (string.Empty, "A", FirstGroupADraw),
            (string.Empty, "B", GroupBDraw)
        ]);

        using var host = CreateHost(config, new TestProfileService(history, new StudentList { Students = [student] }));

        var unscoped = CreateEngine(host)
            .PrepareStudentsForDraw(1, [student], CreatePolicy(config), string.Empty, string.Empty)
            .HistoryCache[student];

        // 这是设计意图，不是缺陷：参数空字符串 = 「该轴不参与过滤」，所以已打标记录仍被计入，
        // 正是主页面下拉选「不使用记录组」时的全局权重抽取（#294 要能随时退回的那个状态）。
        // docs/history-group-plan.md §2.2 里「空串 = 默认组」说的是**记录侧**的值
        // （HistoryItem.HistoryGroup == "" 表示这条记录在默认组），不是参数侧；
        // MVP 不提供「只算默认组」这种模式（组名不允许为空，「默认组」不是可选组），
        // 所以参数侧不需要 string? 的 null/"" 双语义 —— 不要把它当 bug「修」成三态。
        Assert.Equal(2, unscoped.TotalCount);
    }

    [Fact]
    public void VerificationAuditPayload_ReportsTheGroupScopedHistoryCount()
    {
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        var config = CreateConfig();
        var history = CreateStudentHistory(student, [
            (string.Empty, "A", FirstGroupADraw),
            (string.Empty, "A", SecondGroupADraw),
            (string.Empty, "B", GroupBDraw)
        ]);

        using var host = CreateHost(config, new TestProfileService(history, new StudentList { Students = [student] }));
        var input = CreateEngine(host)
            .CreateStudentVerificationInput(1, [student], DrawSettingsType.RollCall, string.Empty, "A");

        using var audit = JsonDocument.Parse(input.AuditPayload);
        var candidate = audit.RootElement.GetProperty("candidates").EnumerateArray().Single();

        // 证明输入里的逐候选 historyCount 取自过滤后的 cache，所以它也是组范围的公开观测点。
        Assert.Equal(2, candidate.GetProperty("historyCount").GetInt32());
        Assert.Equal(
            SecondGroupADraw,
            candidate.GetProperty("lastDrawnUtc").GetDateTime());
    }

    private static string RecordKey(Student student) => student.RecordId.ToString("D");

    private static StudentHistory CreateStudentHistory(
        Student student,
        IReadOnlyList<(string Course, string Group, DateTime DrawTime)> records)
    {
        var history = new StudentHistory();
        history.Students[RecordKey(student)] = new History
        {
            TotalCount = records.Count,
            LastDrawnTime = records.Count == 0 ? DateTime.MinValue : records.Max(record => record.DrawTime),
            Histories = new ObservableCollection<HistoryItem>(records.Select(record => new HistoryItem
            {
                RecordId = RecordKey(student),
                RecordName = student.Name,
                CourseName = record.Course,
                HistoryGroup = record.Group,
                DrawTime = record.DrawTime
            }))
        };
        return history;
    }

    private static StudentDrawExecutionPolicy CreatePolicy(MainConfigModel config) =>
        StudentDrawExecutionPolicy.DesktopConfigured(DrawType.Fair, config.FairDrawSettings, "builtin.fair");

    private static MainConfigModel CreateConfig()
    {
        return new MainConfigModel
        {
            FairDrawSettings = new FairDrawSettingsConfig
            {
                FairDraw = true,
                FairDrawGroup = false,
                FairDrawGender = false,
                FairDrawTime = false,
                ColdStartEnabled = false,
                // 关闭平均间隔保护，避免候选被「高于平均」规则提前剔除而混淆本文件的计数断言。
                EnableAvgGapProtection = false,
                MinWeight = 0,
                MaxWeight = 10
            },
            // 重复模式 = 无重复上限，范围测试只关心 cache，不关心重复过滤。
            RollCallSettings = new RollCallSettingsConfig { DrawMode = DrawMode.Repeat, DrawType = DrawType.Fair },
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
                services.AddSingleton(profile);
                services.AddSingleton<ConfigServiceBase>(new TestConfigService(config));
                services.AddSingleton<MainConfigHandler>();
            })
            .Build();
    }

    private static DrawEngine CreateEngine(IHost host)
    {
        return new DrawEngine(
            host.Services.GetRequiredService<MainConfigHandler>(),
            host.Services.GetRequiredService<IProfileService>(),
            host.Services.GetRequiredService<ILogger<DrawEngine>>());
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;

        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;

        public override void SaveConfig<T>(T value)
        {
        }

        public override void DeleteConfig<T>(T value)
        {
        }
    }

    private sealed class TestProfileService(StudentHistory history, StudentList students, PrizeList? prizes = null,
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

        public void LoadStudentProfile(string name, bool saveCurrent = true)
        {
        }

        public void LoadPrizeProfile(string name, bool saveCurrent = true)
        {
        }

        public void RecordStudentHistory(IReadOnlyList<Student> students, DateTime now, int requestedCount,
            string drawGroup = "", string drawGender = "", int drawMethod = 0,
            IReadOnlyDictionary<Student, double>? weights = null, string courseName = "", string? drawRoundId = null,
            string historyGroup = "")
        {
        }

        public void RecordPrizeHistory(IReadOnlyList<Prize> prizes, DateTime now, int requestedCount, int drawMethod = 0,
            string? drawRoundId = null, string courseName = "", string historyGroup = "")
        {
        }

        public void ClearCurrentStudentHistory()
        {
        }

        public void ClearCurrentPrizeHistory()
        {
        }

        public void SaveProfile()
        {
        }
    }
}
