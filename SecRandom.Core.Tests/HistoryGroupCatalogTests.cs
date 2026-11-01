using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Profiles;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     Group catalog CRUD over a real profile: naming rules, per-kind isolation, the registered-list ∪ record-value
///     union, rename / delete rewrites, derived record counts, and persistence through the normal per-file save path.
/// </summary>
public sealed class HistoryGroupCatalogTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "history-group-catalog-tests", Guid.NewGuid().ToString("N"));

    public HistoryGroupCatalogTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [Fact]
    public void TryCreate_RegistersOnlyTheRequestedKind()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var catalog = CreateCatalog(profile);

        Assert.True(catalog.TryCreate(HistoryGroupKind.RollCall, "课堂", out var error), error);
        Assert.Equal(string.Empty, error);

        // 点名与抽奖是两个独立的档案文件、两套独立的候选池，所以组名不跨种类共享：
        // 否则抽奖侧的选择器里会冒出一个「只有点名记录」的空组。
        Assert.True(profile.CurrentStudentHistory!.HistoryGroups.ContainsKey("课堂"));
        Assert.False(profile.CurrentPrizeHistory!.HistoryGroups.ContainsKey("课堂"));
        Assert.Equal(["课堂"], catalog.GetGroupNames(HistoryGroupKind.RollCall));
        Assert.Empty(catalog.GetGroupNames(HistoryGroupKind.Lottery));

        Assert.True(catalog.TryCreate(HistoryGroupKind.Lottery, "课堂", out _));
        Assert.True(profile.CurrentPrizeHistory.HistoryGroups.ContainsKey("课堂"));
        Assert.Equal(["课堂"], catalog.GetGroupNames(HistoryGroupKind.Lottery));
    }

    [Fact]
    public void TryCreate_NormalizesNamesAndRejectsInvalidOnes()
    {
        using var provider = CreateProvider();
        var catalog = CreateCatalog(provider.GetRequiredService<IProfileService>());
        const HistoryGroupKind kind = HistoryGroupKind.RollCall;

        Assert.True(catalog.TryCreate(kind, "  课堂  ", out _));
        Assert.Equal(["课堂"], catalog.GetGroupNames(kind));

        Assert.False(catalog.TryCreate(kind, "课堂", out var duplicateError));
        Assert.Equal(HistoryGroupName.ErrorNameExists, duplicateError);

        Assert.False(catalog.TryCreate(kind, "   ", out var emptyError));
        Assert.Equal(HistoryGroupName.ErrorNameEmpty, emptyError);

        Assert.False(catalog.TryCreate(kind, new string('x', HistoryGroupName.MaxLength + 1), out var longError));
        Assert.Equal(HistoryGroupName.ErrorNameTooLong, longError);

        Assert.True(catalog.TryCreate(kind, new string('x', HistoryGroupName.MaxLength), out _));

        // 课程侧 __break__ 哨兵先例：任何以 __ 包裹的名字都会被导出/日志误读，一律拒绝。
        Assert.False(catalog.TryCreate(kind, "__break__", out var breakError));
        Assert.Equal(HistoryGroupName.ErrorNameReserved, breakError);

        Assert.False(catalog.TryCreate(kind, "__x", out var wrappedError));
        Assert.Equal(HistoryGroupName.ErrorNameReserved, wrappedError);
    }

    [Fact]
    public void GetGroupNames_IsTheOrdinalSortedUnionOfRegisteredAndRecordCarriedNames()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var catalog = CreateCatalog(profile);
        const HistoryGroupKind kind = HistoryGroupKind.RollCall;

        Assert.True(catalog.TryCreate(kind, "beta", out _));
        Assert.True(catalog.TryCreate(kind, "alpha", out _));
        // 记录上带着一个从未登记过的名字（降级版本抹掉清单、或外部编辑过档案）：并集里必须仍然可见。
        profile.RecordStudentHistory([new Student { Name = "Lin", RecordId = Guid.NewGuid() }], DateTime.Now, 1,
            historyGroup: "gamma");

        Assert.Equal(["alpha", "beta", "gamma"], catalog.GetGroupNames(kind));
        // 默认组（空串）永不列出。
        Assert.DoesNotContain(string.Empty, catalog.GetGroupNames(kind));
        // 另一种类看不到点名侧的名字。
        Assert.Empty(catalog.GetGroupNames(HistoryGroupKind.Lottery));
    }

    [Fact]
    public void TryRename_RewritesEveryRecordOfTheGroupAndDropsTheOldName()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var catalog = CreateCatalog(profile);
        const HistoryGroupKind kind = HistoryGroupKind.RollCall;
        var first = new Student { Name = "Ada", RecordId = Guid.NewGuid() };
        var second = new Student { Name = "Bob", RecordId = Guid.NewGuid() };
        profile.RecordStudentHistory([first], DateTime.Now, 1, historyGroup: "旧");
        profile.RecordStudentHistory([second], DateTime.Now, 1, historyGroup: "旧");
        profile.RecordStudentHistory([first], DateTime.Now, 1);

        Assert.True(catalog.TryRename(kind, "旧", "新", out var error), error);

        var items = profile.CurrentStudentHistory!.Students.Values.SelectMany(history => history.Histories).ToArray();
        Assert.Equal(2, items.Count(item => item.HistoryGroup == "新"));
        Assert.DoesNotContain(items, item => item.HistoryGroup == "旧");
        // 默认组的记录不受改名影响。
        Assert.Single(items, item => item.HistoryGroup == string.Empty);
        Assert.Equal(["新"], catalog.GetGroupNames(kind));
        Assert.True(profile.CurrentStudentHistory.HistoryGroups.ContainsKey("新"));
    }

    [Fact]
    public void TryRename_RejectsUnchangedUnknownAndDuplicateTargets()
    {
        using var provider = CreateProvider();
        var catalog = CreateCatalog(provider.GetRequiredService<IProfileService>());
        const HistoryGroupKind kind = HistoryGroupKind.RollCall;
        Assert.True(catalog.TryCreate(kind, "课堂", out _));
        Assert.True(catalog.TryCreate(kind, "抽奖", out _));

        Assert.False(catalog.TryRename(kind, "课堂", "课堂", out var unchangedError));
        Assert.Equal(HistoryGroupName.ErrorNameUnchanged, unchangedError);

        Assert.False(catalog.TryRename(kind, "不存在", "新名", out var missingError));
        Assert.Equal(HistoryGroupName.ErrorNameNotFound, missingError);

        Assert.False(catalog.TryRename(kind, "课堂", "抽奖", out var duplicateError));
        Assert.Equal(HistoryGroupName.ErrorNameExists, duplicateError);
    }

    [Fact]
    public void Delete_MovesTheGroupsRecordsBackToTheDefaultGroupAndKeepsEveryRecord()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var catalog = CreateCatalog(profile);
        const HistoryGroupKind kind = HistoryGroupKind.RollCall;
        var first = new Student { Name = "Ada", RecordId = Guid.NewGuid() };
        var second = new Student { Name = "Bob", RecordId = Guid.NewGuid() };
        profile.RecordStudentHistory([first], DateTime.Now, 1, historyGroup: "旧");
        profile.RecordStudentHistory([second], DateTime.Now, 1, historyGroup: "旧");
        var before = profile.CurrentStudentHistory!.Students.Values.SelectMany(history => history.Histories).Count();

        catalog.Delete(kind, "旧");

        var items = profile.CurrentStudentHistory.Students.Values.SelectMany(history => history.Histories).ToArray();
        // 删除组绝不删除记录：全部落回默认组。
        Assert.Equal(before, items.Length);
        Assert.All(items, item => Assert.Equal(string.Empty, item.HistoryGroup));
        Assert.Empty(catalog.GetGroupNames(kind));
    }

    [Fact]
    public void CountRecords_IsDerivedFromTheRecordsThemselves()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var catalog = CreateCatalog(profile);
        const HistoryGroupKind kind = HistoryGroupKind.RollCall;
        var first = new Student { Name = "Ada", RecordId = Guid.NewGuid() };
        var second = new Student { Name = "Bob", RecordId = Guid.NewGuid() };

        profile.RecordStudentHistory([first], DateTime.Now, 1, historyGroup: "旧");
        profile.RecordStudentHistory([second], DateTime.Now, 1, historyGroup: "旧");
        Assert.Equal(2, catalog.CountRecords(kind, "旧"));

        // 直接追加一条历史（不经过任何登记），计数必须立刻反映——记录数不是被存下来的第二份真相。
        profile.RecordStudentHistory([first], DateTime.Now, 1, historyGroup: "旧");
        Assert.Equal(3, catalog.CountRecords(kind, "旧"));

        profile.RecordStudentHistory([second], DateTime.Now, 1);
        Assert.Equal(1, catalog.CountRecords(kind, string.Empty));
        Assert.Equal(0, catalog.CountRecords(kind, "从未出现"));
    }

    [Fact]
    public void Touch_StampsLastUsedAtAndRegistersAnUnknownInUseGroup()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var catalog = CreateCatalog(profile);
        const HistoryGroupKind kind = HistoryGroupKind.RollCall;
        profile.RecordStudentHistory([new Student { Name = "Lin", RecordId = Guid.NewGuid() }], DateTime.Now, 1,
            historyGroup: "自发");

        catalog.Touch(kind, "自发");

        Assert.True(profile.CurrentStudentHistory!.HistoryGroups.TryGetValue("自发", out var info));
        Assert.NotEqual(DateTime.MinValue, info!.LastUsedAt);

        // 没有任何记录、也没有登记的组不该被 Touch 凭空造出来。
        catalog.Touch(kind, "空名");
        Assert.False(profile.CurrentStudentHistory.HistoryGroups.ContainsKey("空名"));
    }

    [Fact]
    public void LotteryKind_OperatesOnlyOnThePrizeHistory()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var catalog = CreateCatalog(profile);
        var prize = new Prize { Name = "Cup", RecordId = Guid.NewGuid() };
        profile.RecordPrizeHistory([prize], DateTime.Now, 1, historyGroup: "旧");

        Assert.True(catalog.TryRename(HistoryGroupKind.Lottery, "旧", "新", out var error), error);

        var prizeItem = profile.CurrentPrizeHistory!.Prizes.Values.SelectMany(history => history.Histories).Single();
        Assert.Equal("新", prizeItem.HistoryGroup);
        Assert.Equal(["新"], catalog.GetGroupNames(HistoryGroupKind.Lottery));
        // 点名侧完全没被触碰。
        Assert.Empty(catalog.GetGroupNames(HistoryGroupKind.RollCall));
        Assert.Empty(profile.CurrentStudentHistory!.HistoryGroups);
    }

    [Fact]
    public void GroupChanges_SurviveReloadFromDisk()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var catalog = CreateCatalog(profile);
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        Assert.True(catalog.TryCreate(HistoryGroupKind.RollCall, "课堂", out _));
        profile.RecordStudentHistory([student], DateTime.Now, 1, historyGroup: "课堂");

        var reloaded = provider.GetRequiredService<IHistoryQueryService>()
            .LoadStudentHistory(profile.StudentHistoryConfig!.Name);

        Assert.NotNull(reloaded);
        Assert.True(reloaded!.HistoryGroups.ContainsKey("课堂"));
        Assert.Equal("课堂", reloaded.Students.Values.SelectMany(history => history.Histories).Single().HistoryGroup);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static IHistoryGroupCatalog CreateCatalog(IProfileService profile) =>
        new HistoryGroupCatalog(NullLogger<HistoryGroupCatalog>.Instance, profile);

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
