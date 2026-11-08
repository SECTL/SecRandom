using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Profiles;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     「移到另一组」：<see cref="IProfileCatalogManager.MoveStudentHistoryGroup" /> /
///     <see cref="IProfileCatalogManager.MovePrizeHistoryGroup" /> 只改
///     <see cref="HistoryItem.HistoryGroup" /> 这一个标记——不追加记录、不造 DrawRoundId、不动权重与计数，
///     命名非法时拒绝并且一个字节都不写，没有命中任何记录时返回 0 且不写盘。
/// </summary>
public sealed class HistoryGroupMoveTests : IDisposable
{
    private readonly string _dataRoot =
        Path.Combine(Path.GetTempPath(), "SecRandom", "history-group-move-tests", Guid.NewGuid().ToString("N"));

    public HistoryGroupMoveTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [Fact]
    public void MoveStudentHistoryGroup_ChangesOnlyTheMarker()
    {
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        var profile = provider.GetRequiredService<IProfileService>();
        var (alice, _) = SeedRollCallProfile(provider, manager, profile);

        var history = profile.CurrentStudentHistory!;
        var profileName = profile.StudentHistoryConfig!.Name;
        var key = ProfileRecordIdentity.EnsureRecordId(alice);
        var before = Snapshot(history, key);

        var moved = manager.MoveStudentHistoryGroup(profileName, [key], "课堂");

        Assert.Equal(1, moved);
        var item = history.Students[key].Histories.Single();
        Assert.Equal("课堂", item.HistoryGroup);

        // 改标记不是一次抽取：记录条数、累计次数、最近抽取时间、权重全部原样。
        Assert.Equal(before.RecordCount, history.Students.Count);
        Assert.Equal(before.HistoryCount, history.Students[key].Histories.Count);
        Assert.Equal(before.TotalCount, history.Students[key].TotalCount);
        Assert.Equal(before.LastDrawnTime, history.Students[key].LastDrawnTime);
        Assert.Equal(before.Weight, item.Weight);
        Assert.Equal(before.DrawRoundId, item.DrawRoundId);

        // 目标组要在登记表里存在（否则选择器里看不到这个组）。
        Assert.True(history.HistoryGroups.ContainsKey("课堂"));

        // 落盘了：重新从磁盘读出来的那一份也带着新标记。
        var reloaded = provider.GetRequiredService<IHistoryQueryService>().LoadStudentHistory(profileName);
        Assert.NotNull(reloaded);
        Assert.Equal("课堂", reloaded!.Students[key].Histories.Single().HistoryGroup);
        Assert.True(reloaded.HistoryGroups.ContainsKey("课堂"));
    }

    [Fact]
    public void MoveStudentHistoryGroup_ToEmptyTargetReturnsToTheDefaultGroup()
    {
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        var profile = provider.GetRequiredService<IProfileService>();
        var (alice, _) = SeedRollCallProfile(provider, manager, profile);

        var history = profile.CurrentStudentHistory!;
        var profileName = profile.StudentHistoryConfig!.Name;
        var key = ProfileRecordIdentity.EnsureRecordId(alice);

        Assert.Equal(1, manager.MoveStudentHistoryGroup(profileName, [key], "课堂"));
        Assert.Equal("课堂", history.Students[key].Histories.Single().HistoryGroup);

        // 空串就是默认组：它必须先短路掉命名校验，否则「移回默认组」永远非法。
        Assert.Equal(1, manager.MoveStudentHistoryGroup(profileName, [key], string.Empty));
        Assert.Equal(string.Empty, history.Students[key].Histories.Single().HistoryGroup);

        var reloaded = provider.GetRequiredService<IHistoryQueryService>().LoadStudentHistory(profileName);
        Assert.Equal(string.Empty, reloaded!.Students[key].Histories.Single().HistoryGroup);
    }

    [Fact]
    public void MoveStudentHistoryGroup_RejectsInvalidGroupNamesWithoutWriting()
    {
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        var profile = provider.GetRequiredService<IProfileService>();
        var (alice, _) = SeedRollCallProfile(provider, manager, profile);

        var history = profile.CurrentStudentHistory!;
        var profileName = profile.StudentHistoryConfig!.Name;
        var key = ProfileRecordIdentity.EnsureRecordId(alice);
        var path = Utils.GetFilePath("history", "roll_call_history", $"{profileName}{Utils.HistoryDatabaseExtension}");
        var bytesBefore = File.ReadAllBytes(path);

        AssertMoveRejected(manager, profileName, [key], new string('x', HistoryGroupName.MaxLength + 1),
            HistoryGroupName.ErrorNameTooLong);
        AssertMoveRejected(manager, profileName, [key], "课\t堂",
            HistoryGroupName.ErrorNameControlChars);
        // 课程联动用 __break__ 当课间哨兵，任何 __ 包裹的名字都会被导出/日志误读，一律拒绝。
        AssertMoveRejected(manager, profileName, [key], HistoryGroupName.BreakCourseMarker,
            HistoryGroupName.ErrorNameReserved);

        // 拒绝就是拒绝：记录标记没变、没有凭空登记出组、历史数据库一个字节都没动。
        Assert.Equal(string.Empty, history.Students[key].Histories.Single().HistoryGroup);
        Assert.Empty(history.HistoryGroups);
        Assert.Equal(bytesBefore, File.ReadAllBytes(path));
    }

    [Fact]
    public void MoveStudentHistoryGroup_ReturnsZeroAndWritesNothing_WhenNoRecordMatches()
    {
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        var profile = provider.GetRequiredService<IProfileService>();
        SeedRollCallProfile(provider, manager, profile);

        var history = profile.CurrentStudentHistory!;
        var profileName = profile.StudentHistoryConfig!.Name;
        var path = Utils.GetFilePath("history", "roll_call_history", $"{profileName}{Utils.HistoryDatabaseExtension}");
        var bytesBefore = File.ReadAllBytes(path);
        var lastWriteBefore = File.GetLastWriteTimeUtc(path);
        var writtenAt = DateTime.UtcNow;

        // 不存在的 RecordId（以及空集合）都不该被视为「需要落盘」的改动。
        Assert.Equal(0, manager.MoveStudentHistoryGroup(profileName, [Guid.NewGuid().ToString("N")], "课堂"));
        Assert.Equal(0, manager.MoveStudentHistoryGroup(profileName, [], "课堂"));

        Assert.Equal(bytesBefore, File.ReadAllBytes(path));
        Assert.Equal(lastWriteBefore, File.GetLastWriteTimeUtc(path));
        // 没有命中就不可能登记出目标组。
        Assert.Empty(history.HistoryGroups);
        Assert.All(
            history.Students.Values.SelectMany(record => record.Histories),
            item => Assert.Equal(string.Empty, item.HistoryGroup));
        Assert.True(DateTime.UtcNow >= writtenAt);
    }

    [Fact]
    public void MoveStudentHistoryGroup_OnANonActiveProfileWritesThatFileAndLeavesTheActiveOneAlone()
    {
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        var profile = provider.GetRequiredService<IProfileService>();
        SeedRollCallProfile(provider, manager, profile);

        // class-b 只是「另一个档案」：记录建好后切回 class-a，它就只剩磁盘上的文件。
        Assert.True(manager.CreateStudentList("class-b"));
        profile.LoadStudentProfile("class-b");
        var carol = new Student { Name = "Carol", Id = "3", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(carol);
        profile.SaveProfile();
        profile.RecordStudentHistory([carol], new DateTime(2026, 8, 30, 11, 0, 0), 1);
        profile.LoadStudentProfile("class-a");

        var activeHistory = profile.CurrentStudentHistory!;
        var moved = manager.MoveStudentHistoryGroup(
            "class-b",
            [ProfileRecordIdentity.EnsureRecordId(carol)],
            "课堂");

        Assert.Equal(1, moved);
        // 活动档案一个标记都没动，也没有多出一个只有名字的空组（把组记到错的档案上是这里最容易犯的错）。
        Assert.Same(activeHistory, profile.CurrentStudentHistory);
        Assert.Empty(activeHistory.HistoryGroups);
        Assert.All(
            activeHistory.Students.Values.SelectMany(record => record.Histories),
            item => Assert.Equal(string.Empty, item.HistoryGroup));

        var reloaded = provider.GetRequiredService<IHistoryQueryService>().LoadStudentHistory("class-b");
        Assert.NotNull(reloaded);
        Assert.Equal("课堂", reloaded!.Students.Values.SelectMany(record => record.Histories).Single().HistoryGroup);
        Assert.True(reloaded.HistoryGroups.ContainsKey("课堂"));
    }

    [Fact]
    public void MoveStudentHistoryGroup_ThrowsWhenTheHistoryFileIsMissing()
    {
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        Assert.True(manager.CreateStudentList("class-empty"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.MoveStudentHistoryGroup("class-empty", [Guid.NewGuid().ToString("N")], "课堂"));

        Assert.Equal(HistoryGroupName.ErrorProfileUnavailable, exception.Message);
        // 档案不存在时绝不顺手写出一个空的历史文件。
        Assert.False(File.Exists(Utils.GetFilePath("history", "roll_call_history", "class-empty.json")));
        Assert.False(File.Exists(Utils.GetFilePath("history", "roll_call_history", $"class-empty{Utils.HistoryDatabaseExtension}")));
    }

    [Fact]
    public void MovePrizeHistoryGroup_ChangesOnlyTheMarker()
    {
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        var profile = provider.GetRequiredService<IProfileService>();
        Assert.True(manager.CreatePrizeList("pool-a"));
        profile.LoadPrizeProfile("pool-a");

        var book = new Prize { Name = "Book", Id = "A1", Count = 1, RecordId = Guid.NewGuid() };
        var cup = new Prize { Name = "Cup", Id = "A2", Count = 1, RecordId = Guid.NewGuid() };
        profile.CurrentPrizeList!.Prizes.Add(book);
        profile.CurrentPrizeList!.Prizes.Add(cup);
        profile.SaveProfile();
        profile.RecordPrizeHistory([book], new DateTime(2026, 8, 30, 8, 0, 0), 1);
        profile.RecordPrizeHistory([cup], new DateTime(2026, 8, 30, 9, 0, 0), 1);

        var history = profile.CurrentPrizeHistory!;
        var profileName = profile.PrizeHistoryConfig!.Name;
        var key = ProfileRecordIdentity.EnsureRecordId(book);
        var before = Snapshot(history.Prizes[key]);

        var moved = manager.MovePrizeHistoryGroup(profileName, [key], "抽奖组");

        Assert.Equal(1, moved);
        var item = history.Prizes[key].Histories.Single();
        Assert.Equal("抽奖组", item.HistoryGroup);
        Assert.Equal(before.HistoryCount, history.Prizes[key].Histories.Count);
        Assert.Equal(before.TotalCount, history.Prizes[key].TotalCount);
        Assert.Equal(before.LastDrawnTime, history.Prizes[key].LastDrawnTime);
        Assert.Equal(before.Weight, item.Weight);
        // 另一个奖品的记录不受影响。
        Assert.Equal(string.Empty, history.Prizes[ProfileRecordIdentity.EnsureRecordId(cup)].Histories.Single().HistoryGroup);
        Assert.True(history.HistoryGroups.ContainsKey("抽奖组"));

        var reloaded = provider.GetRequiredService<IHistoryQueryService>().LoadPrizeHistory(profileName);
        Assert.NotNull(reloaded);
        Assert.Equal("抽奖组", reloaded!.Prizes[key].Histories.Single().HistoryGroup);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    /// <summary>一条点名记录的前后对照值：改标记不该让其中任何一个数字变化。</summary>
    private sealed record RecordSnapshot(
        int RecordCount,
        int HistoryCount,
        int TotalCount,
        DateTime LastDrawnTime,
        double Weight,
        string DrawRoundId);

    private static RecordSnapshot Snapshot(StudentHistory history, string key)
    {
        var record = history.Students[key];
        var item = record.Histories.Single();
        return new RecordSnapshot(history.Students.Count, record.Histories.Count, record.TotalCount,
            record.LastDrawnTime, item.Weight, item.DrawRoundId);
    }

    private static RecordSnapshot Snapshot(History record)
    {
        var item = record.Histories.Single();
        return new RecordSnapshot(1, record.Histories.Count, record.TotalCount,
            record.LastDrawnTime, item.Weight, item.DrawRoundId);
    }

    private static void AssertMoveRejected(
        IProfileCatalogManager manager,
        string profileName,
        IReadOnlyCollection<string> recordIds,
        string targetGroup,
        string expectedError)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            manager.MoveStudentHistoryGroup(profileName, recordIds, targetGroup));
        // ArgumentException.Message 会带上 " (Parameter '...')"，用 StartsWith 只校验错误码本身。
        Assert.StartsWith(expectedError, exception.Message);
        Assert.Equal(nameof(targetGroup), exception.ParamName);
    }

    /// <summary>
    ///     class-a 成为活动档案：Alice 有一条带权重的记录，Bob 有一条默认组的记录（用来验证移动只影响命中的记录）。
    /// </summary>
    private static (Student Alice, Student Bob) SeedRollCallProfile(
        ServiceProvider provider,
        IProfileCatalogManager manager,
        IProfileService profile)
    {
        Assert.True(manager.CreateStudentList("class-a"));
        profile.LoadStudentProfile("class-a");

        var alice = new Student { Name = "Alice", Id = "1", RecordId = Guid.NewGuid() };
        var bob = new Student { Name = "Bob", Id = "2", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(alice);
        profile.CurrentStudentList!.Students.Add(bob);
        profile.SaveProfile();

        profile.RecordStudentHistory(
            [alice],
            new DateTime(2026, 8, 30, 9, 0, 0),
            1,
            weights: new Dictionary<Student, double> { [alice] = 2.5 });
        profile.RecordStudentHistory([bob], new DateTime(2026, 8, 30, 10, 0, 0), 1);

        Assert.Same(profile.CurrentStudentHistory, provider.GetRequiredService<IProfileService>().CurrentStudentHistory);
        return (alice, bob);
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
