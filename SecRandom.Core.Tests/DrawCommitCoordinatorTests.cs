using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Ipc;
using SecRandom.Services.Profiles;
using SecRandom.Shared;
using SecRandom.Shared.Models.Ipc;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public sealed class DrawCommitCoordinatorTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "draw-commit-tests", Guid.NewGuid().ToString("N"));

    public DrawCommitCoordinatorTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [Fact]
    public void LotteryCommit_SharesOneDrawRoundIdBetweenPrizeAndAssignedStudentHistory()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var prize = new Prize { Name = "Cup", RecordId = Guid.NewGuid() };
        var student = new Student { Name = "Ada", RecordId = Guid.NewGuid() };
        profile.CurrentPrizeList!.Prizes.Add(prize);
        profile.CurrentStudentList!.Students.Add(student);
        profile.SaveProfile();

        var commits = provider.GetRequiredService<IDrawCommitService>();
        var roundId = commits.CommitLotteryDraw(new LotteryDrawCommit(
            [prize],
            DateTime.Now,
            1,
            profile.PrizeListConfig!.Name,
            AssignedStudents: [student],
            StudentListName: profile.StudentListConfig!.Name,
            PrizeDrawMethod: (int)LotteryDrawType.Count,
            StudentDrawMethod: (int)DrawType.Fair));

        var prizeItem = profile.CurrentPrizeHistory!.Prizes.Values.SelectMany(history => history.Histories).Single();
        var studentItem = profile.CurrentStudentHistory!.Students.Values.SelectMany(history => history.Histories).Single();

        Assert.False(string.IsNullOrWhiteSpace(roundId));
        Assert.Equal(roundId, prizeItem.DrawRoundId);
        Assert.Equal(roundId, studentItem.DrawRoundId);
        Assert.Equal((int)LotteryDrawType.Count, prizeItem.DrawMethod);
        Assert.Equal((int)DrawType.Fair, studentItem.DrawMethod);

        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        Assert.Equal(1, temporary.GetPrizeCounts(profile.PrizeListConfig!.Name)[prize.RecordId.ToString("D")]);
        Assert.Equal(1, temporary.GetStudentCounts(profile.StudentListConfig!.Name, string.Empty, string.Empty)[student.RecordId.ToString("D")]);
    }

    [Fact]
    public void LotteryCommit_WithoutStudentAssignment_WritesOnlyPrizeHistory()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var prize = new Prize { Name = "Notebook", RecordId = Guid.NewGuid() };
        profile.CurrentPrizeList!.Prizes.Add(prize);
        profile.SaveProfile();

        var roundId = provider.GetRequiredService<IDrawCommitService>().CommitLotteryDraw(new LotteryDrawCommit(
            [prize],
            DateTime.Now,
            1,
            profile.PrizeListConfig!.Name,
            PrizeDrawMethod: (int)LotteryDrawType.Count));

        var prizeItem = profile.CurrentPrizeHistory!.Prizes.Values.SelectMany(history => history.Histories).Single();
        Assert.Equal(roundId, prizeItem.DrawRoundId);
        Assert.Empty(profile.CurrentStudentHistory!.Students);
        Assert.Empty(provider.GetRequiredService<IDrawTemporaryRecordService>().GetStudentCounts(
            profile.StudentListConfig!.Name, string.Empty, string.Empty));
    }

    [Fact]
    public void StudentCommit_RollsBackTemporaryRecordsAndHistoryWhenHistorySaveFails()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(student);
        profile.SaveProfile();

        // 用同名目录阻断历史文件写入，让第二步（历史提交）在临时记录写入之后失败。
        var historyPath = profile.CurrentStudentHistory!.ConfigFilePath;
        File.Delete(historyPath);
        Directory.CreateDirectory(historyPath);

        var commits = provider.GetRequiredService<IDrawCommitService>();
        Assert.ThrowsAny<Exception>(() => commits.CommitStudentDraw(new StudentDrawCommit(
            [student],
            DateTime.Now,
            1,
            profile.StudentListConfig!.Name)));

        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        Assert.Empty(temporary.GetStudentCounts(profile.StudentListConfig!.Name, string.Empty, string.Empty));
        Assert.Equal(0, profile.CurrentStudentHistory!.TotalRounds);
        Assert.Equal(0, profile.CurrentStudentHistory.TotalStats);
        Assert.Empty(profile.CurrentStudentHistory.Students);
    }

    [Fact]
    public async Task ProtocolRouter_GroupsHistoryEntriesBySharedDrawRoundId()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var studentA = new Student { Name = "Ann", Id = "01", RecordId = Guid.NewGuid() };
        var studentB = new Student { Name = "Bob", Id = "02", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(studentA);
        profile.CurrentStudentList!.Students.Add(studentB);
        profile.SaveProfile();

        var commits = provider.GetRequiredService<IDrawCommitService>();
        var listName = profile.StudentListConfig!.Name;
        commits.CommitStudentDraw(new StudentDrawCommit([studentA, studentB], DateTime.Now, 2, listName));
        commits.CommitStudentDraw(new StudentDrawCommit([studentA], DateTime.Now, 1, listName));

        // data/ 查询走 Task.Run 而非 UI 线程，VM/安全服务在该路径上不会被触碰。
        var router = new ProtocolCommandRouter(
            provider.GetRequiredService<MainConfigHandler>(),
            null!,
            null!,
            null!,
            new ProfileQueryService(),
            null!,
            provider.GetRequiredService<IFeatureAvailabilityService>());

        var response = await router.HandleIpcAsync(
            new IpcRequestEnvelope(1, "request", new IpcRequestPayload($"data/roll_call_history?name={listName}")),
            CancellationToken.None);

        Assert.True(response.Success);
        var entries = Assert.IsType<List<IpcHistoryEntryDto>>(response.Result!.Data);
        Assert.Equal(2, entries.Count);
        var paired = entries.Single(entry => entry.Students!.Count == 2);
        Assert.Equal(["01", "02"], paired.Students!.Select(record => record.Id).OrderBy(id => id).ToArray());
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
