using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services;
using SecRandom.Core.Services.HistoryStore;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     Write-side behaviour of the history-group dimension: a committed draw stamps
///     <see cref="HistoryItem.HistoryGroup" /> on every record of the round, an empty group keeps the pre-feature
///     shape, a failed commit leaves no marker behind, and temporary records stay in one list-wide bucket.
/// </summary>
public sealed class HistoryGroupCommitTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "history-group-commit-tests", Guid.NewGuid().ToString("N"));

    public HistoryGroupCommitTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [Fact]
    public void StudentCommit_StampsHistoryGroupAndSharesOneDrawRoundId()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var first = new Student { Name = "Ada", RecordId = Guid.NewGuid() };
        var second = new Student { Name = "Bob", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(first);
        profile.CurrentStudentList!.Students.Add(second);
        profile.SaveProfile();

        var roundId = provider.GetRequiredService<IDrawCommitService>().CommitStudentDraw(new StudentDrawCommit(
            [first, second],
            DateTime.Now,
            2,
            profile.StudentListConfig!.Name,
            HistoryGroup: "期末抽奖"));

        var items = profile.CurrentStudentHistory!.Students.Values.SelectMany(history => history.Histories).ToArray();

        Assert.Equal(2, items.Length);
        Assert.All(items, item => Assert.Equal("期末抽奖", item.HistoryGroup));
        Assert.All(items, item => Assert.Equal(roundId, item.DrawRoundId));
        // 组维度与课程维度正交：只打组时课程保持空串。
        Assert.All(items, item => Assert.Equal(string.Empty, item.CourseName));
    }

    [Fact]
    public void StudentCommit_WithoutHistoryGroup_KeepsThePreFeatureShape()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(student);
        profile.SaveProfile();

        provider.GetRequiredService<IDrawCommitService>().CommitStudentDraw(new StudentDrawCommit(
            [student],
            DateTime.Now,
            1,
            profile.StudentListConfig!.Name));

        var item = profile.CurrentStudentHistory!.Students.Values.SelectMany(history => history.Histories).Single();
        Assert.Equal(string.Empty, item.HistoryGroup);
        Assert.Equal(string.Empty, item.CourseName);
        Assert.False(string.IsNullOrWhiteSpace(item.DrawRoundId));
    }

    [Fact]
    public void StudentCommit_WithCourseAndGroup_StampsBothAxes()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(student);
        profile.SaveProfile();

        provider.GetRequiredService<IDrawCommitService>().CommitStudentDraw(new StudentDrawCommit(
            [student],
            DateTime.Now,
            1,
            profile.StudentListConfig!.Name,
            CourseName: "数学",
            HistoryGroup: "课堂"));

        var item = profile.CurrentStudentHistory!.Students.Values.SelectMany(history => history.Histories).Single();
        Assert.Equal("数学", item.CourseName);
        Assert.Equal("课堂", item.HistoryGroup);
    }

    [Fact]
    public void StudentCommit_RollsBackHistoryGroupMarkersWhenHistorySaveFails()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(student);
        profile.SaveProfile();

        // 用同名目录阻断历史文件写入，让第二步（历史提交）在临时记录写入之后失败。
        var historyPath = ProfileHistoryPaths.DatabasePath(profile.CurrentStudentHistory!.Name, HistoryGroupKind.RollCall);
        File.Delete(historyPath);
        Directory.CreateDirectory(historyPath);

        var commits = provider.GetRequiredService<IDrawCommitService>();
        Assert.ThrowsAny<Exception>(() => commits.CommitStudentDraw(new StudentDrawCommit(
            [student],
            DateTime.Now,
            1,
            profile.StudentListConfig!.Name,
            HistoryGroup: "期末抽奖")));

        // 回滚后不能留下任何组标记：否则下一次（默认组）抽取会看到一条不属于它的记录。
        Assert.Empty(profile.CurrentStudentHistory!.Students);
        Assert.Equal(0, profile.CurrentStudentHistory.TotalRounds);
        Assert.Empty(provider.GetRequiredService<IDrawTemporaryRecordService>()
            .GetStudentCounts(profile.StudentListConfig!.Name, null, null));
    }

    [Fact]
    public void TemporaryRecords_AreCountedPerList_GroupArgumentsIgnored()
    {
        // v4 之后临时记录按 list 单桶计数：historyGroup/gender 不再参与分桶，
        // 两次不同组的提交会累加到同一个计数上（旧版是「每组各算 1」）。
        // 本条断言在新契约下重写，语义变更由 Lead 批准（他人重构中断期适配）。
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(student);
        profile.SaveProfile();

        var commits = provider.GetRequiredService<IDrawCommitService>();
        var listName = profile.StudentListConfig!.Name;
        var key = student.RecordId.ToString("D");
        commits.CommitStudentDraw(new StudentDrawCommit([student], DateTime.Now, 1, listName, HistoryGroup: "期末抽奖"));
        commits.CommitStudentDraw(new StudentDrawCommit([student], DateTime.Now, 1, listName, HistoryGroup: "课堂"));

        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();

        // 同一个 list 下累计 2 次；无论用哪个组/性别参数去读，都是同一个桶。
        Assert.Equal(2, temporary.GetStudentCounts(listName, null, "期末抽奖")[key]);
        Assert.Equal(2, temporary.GetStudentCounts(listName, null, "课堂")[key]);
        Assert.Equal(2, temporary.GetStudentCounts(listName, null, null)[key]);
        Assert.Equal(2, temporary.GetStudentCounts(listName, string.Empty, string.Empty)[key]);
    }

    [Fact]
    public void StudentCommit_PersistsHistoryGroupAcrossReload()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(student);
        profile.SaveProfile();

        provider.GetRequiredService<IDrawCommitService>().CommitStudentDraw(new StudentDrawCommit(
            [student],
            DateTime.Now,
            1,
            profile.StudentListConfig!.Name,
            HistoryGroup: "期末抽奖"));

        var listName = profile.StudentHistoryConfig!.Name;
        var reloaded = provider.GetRequiredService<IHistoryQueryService>().LoadStudentHistory(listName);

        Assert.NotNull(reloaded);
        var item = reloaded!.Students.Values.SelectMany(history => history.Histories).Single();
        Assert.Equal("期末抽奖", item.HistoryGroup);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        return services.BuildServiceProvider();
    }

    private static void ConfigureDataRootForTests(string dataRoot)
    {
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);
    }

    private static void ResetDataRootForTests()
    {
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);
    }

    private static MethodInfo GetUtilsMethod(string name)
    {
        return typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException($"Utils.{name} was not found.");
    }
}
