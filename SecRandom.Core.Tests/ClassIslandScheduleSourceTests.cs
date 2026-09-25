using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Shared.Enums;
using ClassIsland.Shared.IPC.Abstractions.Services;
using ClassIsland.Shared.Models.Profile;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Models.Linkage;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Linkage;

namespace SecRandom.Core.Tests;

/// <summary>
/// ClassIsland 联动状态映射。ClassIsland 只在当前时间落在上课/课间时间点内时才把
/// IsLessonConfirmed 置为 true，因此放学（AfterSchool）与开课前（None）必然是 false，
/// 但它们同样是数据源明确给出的非上课时段，不能被当作“状态不可信”而放行（issue #278）。
/// </summary>
public sealed class ClassIslandScheduleSourceTests
{
    [Theory]
    [InlineData(TimeState.OnClass, CourseTimeState.OnClass)]
    [InlineData(TimeState.Breaking, CourseTimeState.Breaking)]
    [InlineData(TimeState.AfterSchool, CourseTimeState.Breaking)]
    [InlineData(TimeState.PrepareOnClass, CourseTimeState.Breaking)]
    [InlineData(TimeState.None, CourseTimeState.Breaking)]
    public void MapCurrentState_TreatsEveryNonClassStateAsConfirmedBreak(
        TimeState state,
        CourseTimeState expected)
    {
        Assert.Equal(expected, ClassIslandScheduleSource.MapCurrentState(state));
    }

    [Fact]
    public async Task AfterSchool_IsConfirmedNonClassTime()
    {
        var result = await EvaluateAsync(lessons => lessons.CurrentState = TimeState.AfterSchool);

        Assert.True(result.Snapshot.IsAvailable, $"error={result.Snapshot.Error}");
        Assert.Equal(CourseTimeState.Breaking, result.Snapshot.State);
        Assert.True(result.Restricted);
    }

    [Fact]
    public async Task BeforeFirstClass_IsConfirmedNonClassTime()
    {
        var result = await EvaluateAsync(lessons =>
        {
            lessons.CurrentState = TimeState.None;
            lessons.OnClassLeftTime = TimeSpan.FromMinutes(40);
        });

        Assert.True(result.Snapshot.IsAvailable, $"error={result.Snapshot.Error}");
        Assert.Equal(TimeSpan.FromMinutes(40), result.Snapshot.TimeUntilNextCourse);
        Assert.True(result.Restricted);
    }

    [Fact]
    public async Task BeforeFirstClass_InsidePreClassEnableWindow_IsPermitted()
    {
        var result = await EvaluateAsync(
            lessons =>
            {
                lessons.CurrentState = TimeState.None;
                lessons.OnClassLeftTime = TimeSpan.FromSeconds(10);
            },
            preClassEnableSeconds: 300);

        Assert.True(result.Snapshot.IsAvailable, $"error={result.Snapshot.Error}");
        Assert.False(result.Restricted);
    }

    [Fact]
    public async Task AfterSchool_InsidePostClassDisableDelay_IsPermitted()
    {
        var now = DateTime.Now;
        var result = await EvaluateAsync(
            lessons =>
            {
                lessons.CurrentState = TimeState.OnClass;
                lessons.CurrentTimeLayoutItem = ClassTime(now, TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
                lessons.OnBreakingTimeLeftTime = TimeSpan.FromMinutes(5);
            },
            postClassDelaySeconds: 600,
            beforeSecondRefresh: lessons =>
            {
                lessons.CurrentState = TimeState.AfterSchool;
                lessons.CurrentTimeLayoutItem = TimeLayoutItem.Empty;
                lessons.OnBreakingTimeLeftTime = TimeSpan.Zero;
            });

        Assert.True(result.Snapshot.IsAvailable, $"error={result.Snapshot.Error}");
        Assert.False(result.Restricted);
    }

    [Fact]
    public async Task OnClass_IsNotRestricted()
    {
        var result = await EvaluateAsync(lessons => lessons.CurrentState = TimeState.OnClass);

        Assert.True(result.Snapshot.IsAvailable, $"error={result.Snapshot.Error}");
        Assert.Equal(CourseTimeState.OnClass, result.Snapshot.State);
        Assert.False(result.Restricted);
    }

    [Fact]
    public async Task Break_OutsidePreClassEnableWindow_IsRestricted()
    {
        var result = await EvaluateAsync(lessons =>
        {
            lessons.CurrentState = TimeState.Breaking;
            lessons.OnClassLeftTime = TimeSpan.FromMinutes(40);
        });

        Assert.True(result.Snapshot.IsAvailable, $"error={result.Snapshot.Error}");
        Assert.True(result.Restricted);
    }

    [Fact]
    public async Task ScheduleNotLoaded_StaysPermissive()
    {
        var result = await EvaluateAsync(lessons =>
        {
            lessons.IsClassPlanLoaded = false;
            lessons.CurrentState = TimeState.None;
        });

        Assert.False(result.Snapshot.IsAvailable);
        Assert.False(result.Restricted);
    }

    private static TimeLayoutItem ClassTime(DateTime now, TimeSpan startedAgo, TimeSpan endedAgo) => new()
    {
        TimeType = 0,
        StartTime = now.TimeOfDay - startedAgo,
        EndTime = now.TimeOfDay - endedAgo
    };

    private static async Task<LinkageResult> EvaluateAsync(
        Action<FakeLessons> configure,
        int preClassEnableSeconds = 0,
        int postClassDelaySeconds = 0,
        Action<FakeLessons>? beforeSecondRefresh = null)
    {
        var config = new MainConfigModel();
        config.LinkageSettings.DataSource = LinkageDataSource.ClassIsland;
        config.LinkageSettings.InstantDrawDisable = true;
        config.LinkageSettings.PreClassEnableTime = preClassEnableSeconds;
        config.LinkageSettings.PostClassDisableDelay = postClassDelaySeconds;

        var handler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(config));
        var store = new FakeCsesScheduleStore();
        var classIsland = new ClassIslandScheduleSource(NullLogger<ClassIslandScheduleSource>.Instance);
        var lessons = new FakeLessons();
        configure(lessons);
        // ClassIslandScheduleSource 只在首次读取时建立 IPC 连接，预先注入假实现即可在无 ClassIsland 的
        // 环境中验证状态映射；字段改名时这里的异常会直接失败，提醒同步测试。
        var field = typeof(ClassIslandScheduleSource).GetField(
                        "_lessons", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("ClassIslandScheduleSource._lessons 字段已改名。");
        field.SetValue(classIsland, lessons);

        var service = new CourseLinkageService(
            handler,
            store,
            new CsesScheduleSource(store),
            classIsland,
            NullLogger<CourseLinkageService>.Instance);

        await service.RefreshAsync();
        if (beforeSecondRefresh is not null)
        {
            beforeSecondRefresh(lessons);
            await service.RefreshAsync();
        }

        return new LinkageResult(service.Snapshot, service.IsConfirmedNonClassTime);
    }

    private sealed record LinkageResult(CourseScheduleSnapshot Snapshot, bool Restricted);

    /// <summary>
    /// 复刻 ClassIsland LessonsService.ProcessLessons() 的上报结果：只有落在上课/课间时间点内时
    /// IsLessonConfirmed 才为 true。
    /// </summary>
    private sealed class FakeLessons : IPublicLessonsService
    {
        public bool IsTimerRunning { get; set; } = true;
        public ClassPlan? CurrentClassPlan { get; set; }
        public int CurrentSelectedIndex { get; set; }
        public Subject NextClassSubject { get; set; } = null!;
        public TimeLayoutItem NextBreakingTimeLayoutItem { get; set; } = TimeLayoutItem.Empty;
        public TimeLayoutItem NextClassTimeLayoutItem { get; set; } = TimeLayoutItem.Empty;
        public TimeSpan OnClassLeftTime { get; set; }
        public TimeSpan OnBreakingTimeLeftTime { get; set; }
        public TimeState CurrentState { get; set; } = TimeState.None;
        public TimeLayoutItem CurrentTimeLayoutItem { get; set; } = TimeLayoutItem.Empty;
        public Subject? CurrentSubject { get; set; }
        public bool IsClassPlanEnabled { get; set; } = true;
        public bool IsClassPlanLoaded { get; set; } = true;

        public bool IsLessonConfirmed
        {
            get => CurrentState is TimeState.OnClass or TimeState.Breaking;
            set { }
        }

        public ClassPlan? GetClassPlanByDate(DateTime date) => null;
    }

    private sealed class FakeCsesScheduleStore : ICsesScheduleStore
    {
        public string SchedulePath => "unused";
        public event EventHandler? ScheduleChanged { add { } remove { } }
        public CsesSchedule? Load() => null;
        public Task<CsesSchedule> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public void Clear() { }
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) { }
        public override void DeleteConfig<T>(T value) { }
    }
}
