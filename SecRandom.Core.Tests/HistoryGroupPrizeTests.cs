using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     Prize-side behaviour of the history-group dimension. #294 的动机原文就是抽奖（「在抽取奖品时他的概率又会降低」），
///     所以奖品侧必须与学生侧对齐：写入打组、读侧按组取权重池，而临时记录（抽奖的重复闸门）保持与组无关。
/// </summary>
public sealed class HistoryGroupPrizeTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "history-group-prize-tests", Guid.NewGuid().ToString("N"));

    public HistoryGroupPrizeTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [Fact]
    public void PrizeCommit_StampsHistoryGroupOnPrizeAndAssignedStudent()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var prize = new Prize { Name = "Cup", RecordId = Guid.NewGuid() };
        var student = new Student { Name = "Ada", RecordId = Guid.NewGuid() };
        profile.CurrentPrizeList!.Prizes.Add(prize);
        profile.CurrentStudentList!.Students.Add(student);
        profile.SaveProfile();

        provider.GetRequiredService<IDrawCommitService>().CommitLotteryDraw(new LotteryDrawCommit(
            [prize],
            DateTime.Now,
            1,
            profile.PrizeListConfig!.Name,
            AssignedStudents: [student],
            StudentListName: profile.StudentListConfig!.Name,
            PrizeDrawMethod: (int)LotteryDrawType.Count,
            StudentDrawMethod: (int)DrawType.Fair,
            CourseName: "数学",
            HistoryGroup: "期末抽奖"));

        var prizeItem = profile.CurrentPrizeHistory!.Prizes.Values.SelectMany(history => history.Histories).Single();
        var studentItem = profile.CurrentStudentHistory!.Students.Values.SelectMany(history => history.Histories).Single();

        Assert.Equal("期末抽奖", prizeItem.HistoryGroup);
        Assert.Equal("数学", prizeItem.CourseName);
        // 奖品与配发的学生记录共享同一轮次、同一组。
        Assert.Equal(prizeItem.DrawRoundId, studentItem.DrawRoundId);
        Assert.Equal("期末抽奖", studentItem.HistoryGroup);
        Assert.Equal("数学", studentItem.CourseName);
    }

    [Fact]
    public void PrizeCommit_WithoutHistoryGroup_KeepsThePreFeatureShape()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var prize = new Prize { Name = "Notebook", RecordId = Guid.NewGuid() };
        profile.CurrentPrizeList!.Prizes.Add(prize);
        profile.SaveProfile();

        provider.GetRequiredService<IDrawCommitService>().CommitLotteryDraw(new LotteryDrawCommit(
            [prize],
            DateTime.Now,
            1,
            profile.PrizeListConfig!.Name,
            PrizeDrawMethod: (int)LotteryDrawType.Count));

        var item = profile.CurrentPrizeHistory!.Prizes.Values.SelectMany(history => history.Histories).Single();
        Assert.Equal(string.Empty, item.HistoryGroup);
        Assert.Equal(string.Empty, item.CourseName);
    }

    [Fact]
    public void PrizeDraw_ScopesThePersistentRepeatLimitToTheSelectedGroup()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var prize = new Prize { Name = "Cup", RecordId = Guid.NewGuid() };
        profile.CurrentPrizeList!.Prizes.Add(prize);
        profile.SaveProfile();
        // 默认 LotterySettings：DrawMode.NoRepeat（阈值 1）——先在「期末抽奖」组抽中过一次。
        profile.RecordPrizeHistory([prize], DateTime.Now, 1, historyGroup: "期末抽奖");

        var engine = CreateEngine(provider, profile);

        // 组内已抽过 → 被重复上限挡下；换到「课堂」组 → 组内 0 条，可以抽。
        Assert.Equal(DrawStatus.RepeatLimitExhausted, engine.DrawPrize(1, _ => true, "期末抽奖").Status);
        Assert.True(engine.DrawPrize(1, _ => true, "课堂").IsSuccess);
        // 不分组（功能关闭）视角看到的是全量历史，所以依然被挡下。
        Assert.Equal(DrawStatus.RepeatLimitExhausted, engine.DrawPrize(1, _ => true, string.Empty).Status);
    }

    [Fact]
    public void PrizeDrawWithTemporaryCounts_IgnoresHistoryGroup()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var prize = new Prize { Name = "Cup", RecordId = Guid.NewGuid() };
        profile.CurrentPrizeList!.Prizes.Add(prize);
        profile.SaveProfile();
        profile.RecordPrizeHistory([prize], DateTime.Now, 1, historyGroup: "期末抽奖");

        var engine = CreateEngine(provider, profile);

        // 临时记录路径只用临时计数决定重复上限（DrawEngine.cs:333-334 的显式约定）：
        // 持久历史里已有一条，但临时计数为空 → 仍然可以抽。组参数不得改变这一点。
        var result = engine.DrawPrizeWithTemporaryCounts(1, _ => true, new Dictionary<string, int>(), "期末抽奖");

        Assert.True(result.IsSuccess);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static DrawEngine CreateEngine(ServiceProvider provider, IProfileService profile)
    {
        return new DrawEngine(
            provider.GetRequiredService<MainConfigHandler>(),
            profile,
            provider.GetRequiredService<ILogger<DrawEngine>>());
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
