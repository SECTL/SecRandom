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
/// ClassIsland 联动的状态映射与刷新语义。
/// <para>
/// ClassIsland 只在当前时间落在上课/课间时间点内时才把 IsLessonConfirmed 置为 true，
/// 因此放学（AfterSchool）与开课前（None）必然是 false，但它们同样是数据源明确给出的非上课时段，
/// 不能被当作“状态不可信”而放行（issue #278）。
/// </para>
/// <para>
/// 刷新时必须始终写入新快照、只在语义发生变化时通知订阅者：倒计时字段每秒都在变，
/// 按全量相等判断会自激刷新（issue #274），而完全丢弃这些字段又会让课前解禁、课后禁用延迟
/// 与课前重置读到陈旧的倒计时。
/// </para>
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
        var harness = new LinkageHarness();
        harness.Lessons.CurrentState = TimeState.AfterSchool;
        await harness.RefreshAsync();

        Assert.True(harness.Snapshot.IsAvailable, $"error={harness.Snapshot.Error}");
        Assert.Equal(CourseTimeState.Breaking, harness.Snapshot.State);
        Assert.True(harness.Restricted);
    }

    [Fact]
    public async Task BeforeFirstClass_IsConfirmedNonClassTime()
    {
        var harness = new LinkageHarness();
        harness.Lessons.CurrentState = TimeState.None;
        harness.Lessons.OnClassLeftTime = TimeSpan.FromMinutes(40);
        await harness.RefreshAsync();

        Assert.True(harness.Snapshot.IsAvailable, $"error={harness.Snapshot.Error}");
        Assert.Equal(TimeSpan.FromMinutes(40), harness.Snapshot.TimeUntilNextCourse);
        Assert.True(harness.Restricted);
    }

    [Fact]
    public async Task BeforeFirstClass_InsidePreClassEnableWindow_IsPermitted()
    {
        var harness = new LinkageHarness(preClassEnableSeconds: 300);
        harness.Lessons.CurrentState = TimeState.None;
        harness.Lessons.OnClassLeftTime = TimeSpan.FromSeconds(10);
        await harness.RefreshAsync();

        Assert.True(harness.Snapshot.IsAvailable, $"error={harness.Snapshot.Error}");
        Assert.False(harness.Restricted);
    }

    [Fact]
    public async Task AfterSchool_InsidePostClassDisableDelay_IsPermitted()
    {
        var now = DateTime.Now;
        var harness = new LinkageHarness(postClassDelaySeconds: 600);
        harness.Lessons.CurrentState = TimeState.OnClass;
        harness.Lessons.CurrentTimeLayoutItem = ClassTime(now, TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(5));
        harness.Lessons.OnBreakingTimeLeftTime = TimeSpan.FromMinutes(5);
        await harness.RefreshAsync();

        harness.Lessons.CurrentState = TimeState.AfterSchool;
        harness.Lessons.CurrentTimeLayoutItem = TimeLayoutItem.Empty;
        harness.Lessons.OnBreakingTimeLeftTime = TimeSpan.Zero;
        await harness.RefreshAsync();

        Assert.True(harness.Snapshot.IsAvailable, $"error={harness.Snapshot.Error}");
        Assert.False(harness.Restricted);
    }

    [Fact]
    public async Task OnClass_IsNotRestricted()
    {
        var harness = new LinkageHarness();
        harness.Lessons.CurrentState = TimeState.OnClass;
        await harness.RefreshAsync();

        Assert.True(harness.Snapshot.IsAvailable, $"error={harness.Snapshot.Error}");
        Assert.Equal(CourseTimeState.OnClass, harness.Snapshot.State);
        Assert.False(harness.Restricted);
    }

    [Fact]
    public async Task Break_OutsidePreClassEnableWindow_IsRestricted()
    {
        var harness = new LinkageHarness();
        harness.Lessons.CurrentState = TimeState.Breaking;
        harness.Lessons.OnClassLeftTime = TimeSpan.FromMinutes(40);
        await harness.RefreshAsync();

        Assert.True(harness.Snapshot.IsAvailable, $"error={harness.Snapshot.Error}");
        Assert.True(harness.Restricted);
    }

    [Fact]
    public async Task ScheduleNotLoaded_StaysPermissive()
    {
        var harness = new LinkageHarness();
        harness.Lessons.IsClassPlanLoaded = false;
        harness.Lessons.CurrentState = TimeState.None;
        await harness.RefreshAsync();

        Assert.False(harness.Snapshot.IsAvailable);
        Assert.False(harness.Restricted);
    }

    [Fact]
    public async Task Refresh_WhenOnlyCountdownChanges_UpdatesSnapshotWithoutNotifying()
    {
        var harness = new LinkageHarness();
        harness.Lessons.CurrentState = TimeState.Breaking;
        harness.Lessons.OnClassLeftTime = TimeSpan.FromMinutes(40);
        await harness.RefreshAsync();
        var notifications = harness.StateChangedCount;

        harness.Lessons.OnClassLeftTime = TimeSpan.FromMinutes(10);
        await harness.RefreshAsync();

        Assert.Equal(notifications, harness.StateChangedCount);
        Assert.Equal(TimeSpan.FromMinutes(10), harness.Snapshot.TimeUntilNextCourse);
    }

    [Fact]
    public async Task Refresh_WhenPreClassEnableWindowOpens_UnrestrictsWithoutNotifying()
    {
        var harness = new LinkageHarness(preClassEnableSeconds: 300);
        harness.Lessons.CurrentState = TimeState.Breaking;
        harness.Lessons.OnClassLeftTime = TimeSpan.FromMinutes(40);
        await harness.RefreshAsync();
        Assert.True(harness.Restricted);
        var notifications = harness.StateChangedCount;

        harness.Lessons.OnClassLeftTime = TimeSpan.FromSeconds(10);
        await harness.RefreshAsync();

        Assert.False(harness.Restricted);
        Assert.Equal(notifications, harness.StateChangedCount);
    }

    private static TimeLayoutItem ClassTime(DateTime now, TimeSpan startedAgo, TimeSpan endedAgo) => new()
    {
        TimeType = 0,
        StartTime = now.TimeOfDay - startedAgo,
        EndTime = now.TimeOfDay - endedAgo
    };

    private sealed class LinkageHarness
    {
        public LinkageHarness(
            int preClassEnableSeconds = 0,
            int postClassDelaySeconds = 0,
            bool instantDrawDisable = true)
        {
            var config = new MainConfigModel();
            config.LinkageSettings.DataSource = LinkageDataSource.ClassIsland;
            config.LinkageSettings.InstantDrawDisable = instantDrawDisable;
            config.LinkageSettings.PreClassEnableTime = preClassEnableSeconds;
            config.LinkageSettings.PostClassDisableDelay = postClassDelaySeconds;

            var handler = new MainConfigHandler(
                NullLogger<MainConfigHandler>.Instance,
                new TestConfigService(config));
            var store = new FakeCsesScheduleStore();
            var connection = new ClassIslandIpcConnection(NullLogger<ClassIslandIpcConnection>.Instance);
            // 预先注入假的课程服务：真实实现只在尚未连接时才去建立 IPC，这样就能在没有
            // ClassIsland 的环境里驱动完整的状态映射；字段改名时这里会直接失败，提醒同步测试。
            var field = typeof(ClassIslandIpcConnection).GetField(
                            "_lessonsService", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException("ClassIslandIpcConnection._lessonsService 字段已改名。");
            field.SetValue(connection, Lessons);

            var source = new ClassIslandScheduleSource(connection, NullLogger<ClassIslandScheduleSource>.Instance);
            Service = new CourseLinkageService(
                handler,
                store,
                new CsesScheduleSource(store),
                source,
                NullLogger<CourseLinkageService>.Instance);
            Service.StateChanged += (_, _) => StateChangedCount++;
        }

        public FakeLessons Lessons { get; } = new();
        public CourseLinkageService Service { get; }
        public int StateChangedCount { get; private set; }
        public CourseScheduleSnapshot Snapshot => Service.Snapshot;
        public bool Restricted => Service.IsConfirmedNonClassTime;

        public Task RefreshAsync() => Service.RefreshAsync();
    }

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
