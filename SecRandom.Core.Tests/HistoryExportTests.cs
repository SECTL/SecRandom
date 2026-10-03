using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiniExcelLibs;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public sealed class HistoryExportTests : IDisposable
{
    private static readonly HistoryExportLabels Labels = new()
    {
        RollCallProfileName = "名单",
        LotteryProfileName = "奖池",
        RollCallFileName = "点名历史",
        LotteryFileName = "抽奖历史",
        DrawTime = "抽取时间",
        RecordNumber = "学号",
        RecordName = "姓名",
        Gender = "性别",
        Group = "小组",
        DrawMethod = "抽取方式",
        DrawCount = "抽取人数",
        DrawGender = "性别限制",
        DrawGroup = "小组限制",
        Subject = "课程",
        Weight = "权重",
        TotalCount = "累计次数",
        LastDrawTime = "最近抽取时间",
        MethodRandom = "随机抽取",
        MethodWeight = "公平抽取",
        AllGenders = "不限性别",
        AllGroups = "不限小组",
        BreakSubject = "课间",
        RollCallRecordsSheet = "点名记录",
        RollCallSummarySheet = "点名汇总",
        LotteryRecordsSheet = "抽奖记录",
        LotterySummarySheet = "抽奖汇总"
    };

    private readonly string _dataRoot =
        Path.Combine(Path.GetTempPath(), "SecRandom", "history-export-tests", Guid.NewGuid().ToString("N"));

    public HistoryExportTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [Fact]
    public async Task ExportSingleProfile_WritesOneWorkbookAndKeepsNeverDrawnMembers()
    {
        using var provider = CreateProvider();
        SeedRollCallList(provider, "class-a", withHistory: true);

        var (result, stream) = await ExportAsync(
            provider,
            Request(HistoryExportKind.RollCall, ["class-a"], HistoryExportFormat.Xlsx));
        using var _ = stream;

        Assert.Equal(1, result.ProfileCount);
        Assert.Equal(3, result.RecordCount);
        Assert.Equal(1, result.FileCount);
        // 有记录的成员各占一张工作表；一次都没抽到的 Carol 不单独建表。
        Assert.Equal(["点名记录", "点名汇总", "1 Alice", "2 Bob"], GetSheetNames(stream));

        var aliceSheet = ReadRows(stream, "1 Alice");
        Assert.Equal(2, aliceSheet.Count);
        Assert.All(aliceSheet, row => Assert.Equal("Alice", row["姓名"]));
        Assert.Single(ReadRows(stream, "2 Bob"));

        var details = ReadRows(stream, "点名记录");
        Assert.Equal(3, details.Count);
        // 明细按抽取时间倒序：最新的一条是 11:00 的公平抽取。
        Assert.Equal("2026-08-30 11:00:00", details[0]["抽取时间"]);
        Assert.Equal("1", details[0]["学号"]);
        Assert.Equal("Alice", details[0]["姓名"]);
        Assert.Equal("公平抽取", details[0]["抽取方式"]);
        Assert.Equal("数学", details[0]["课程"]);
        Assert.Equal("2.50", details[0]["权重"]);
        Assert.Equal("随机抽取", details[1]["抽取方式"]);
        Assert.Equal(2, Convert.ToInt32(details[1]["抽取人数"], CultureInfo.InvariantCulture));
        Assert.Equal("男", details[1]["性别限制"]);
        Assert.Equal("二组", details[1]["小组限制"]);
        Assert.True(string.IsNullOrEmpty(details[1]["权重"]?.ToString()));

        // 汇总表按学号排序，从未被抽到的 Carol 也要在表里，次数为 0。
        var summaries = ReadRows(stream, "点名汇总");
        Assert.Equal(["Alice", "Bob", "Carol"], summaries.Select(row => row["姓名"]).ToArray());
        Assert.Equal(2, Convert.ToInt32(summaries[0]["累计次数"], CultureInfo.InvariantCulture));
        Assert.Equal("2.50", summaries[0]["权重"]);
        Assert.Equal(1, Convert.ToInt32(summaries[1]["累计次数"], CultureInfo.InvariantCulture));
        Assert.Equal(0, Convert.ToInt32(summaries[2]["累计次数"], CultureInfo.InvariantCulture));
        Assert.True(string.IsNullOrEmpty(summaries[2]["最近抽取时间"]?.ToString()));
    }

    [Fact]
    public async Task ExportListWithoutHistory_WritesEmptyDetailSheetAndZeroSummary()
    {
        using var provider = CreateProvider();
        SeedRollCallList(provider, "class-new", withHistory: false);

        var (result, stream) = await ExportAsync(
            provider,
            Request(HistoryExportKind.RollCall, ["class-new"], HistoryExportFormat.Xlsx));
        using var _ = stream;

        Assert.Equal(1, result.ProfileCount);
        Assert.Equal(0, result.RecordCount);
        // 一个人都没抽到，就没有任何单独的成员工作表。
        Assert.Equal(["点名记录", "点名汇总"], GetSheetNames(stream));
        Assert.Empty(ReadRows(stream, "点名记录"));

        var summaries = ReadRows(stream, "点名汇总");
        Assert.Equal(["Alice", "Bob", "Carol"], summaries.Select(row => row["姓名"]).ToArray());
        Assert.All(summaries, row => Assert.Equal(0, Convert.ToInt32(row["累计次数"], CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task ExportMultipleProfiles_WritesZipWithOneWorkbookPerProfile()
    {
        using var provider = CreateProvider();
        SeedRollCallList(provider, "class-a", withHistory: true);
        SeedRollCallList(provider, "class-b", withHistory: true);

        var (result, stream) = await ExportAsync(
            provider,
            Request(HistoryExportKind.RollCall, ["class-a", "class-b"], HistoryExportFormat.Xlsx));
        using var _ = stream;

        Assert.Equal(2, result.ProfileCount);
        Assert.Equal(6, result.RecordCount);
        Assert.Equal(2, result.FileCount);

        using var archive = OpenArchive(stream);
        Assert.Equal(
            ["点名历史-class-a.xlsx", "点名历史-class-b.xlsx"],
            archive.Entries.Select(entry => entry.FullName).ToArray());

        foreach (var entry in archive.Entries)
        {
            using var entryStream = CopyToMemory(entry);
            Assert.Equal(["点名记录", "点名汇总", "1 Alice", "2 Bob"], GetSheetNames(entryStream));
            Assert.Equal(3, ReadRows(entryStream, "点名记录").Count);
            Assert.Equal(3, ReadRows(entryStream, "点名汇总").Count);
        }
    }

    [Fact]
    public async Task ExportMultipleProfilesAsCsv_WritesZipOfBomPrefixedCsvFiles()
    {
        using var provider = CreateProvider();
        SeedRollCallList(provider, "class-a", withHistory: true);
        SeedRollCallList(provider, "class-b", withHistory: true);

        var (result, stream) = await ExportAsync(
            provider,
            Request(HistoryExportKind.RollCall, ["class-a", "class-b"], HistoryExportFormat.Csv));
        using var _ = stream;

        Assert.Equal(2, result.FileCount);

        using var archive = OpenArchive(stream);
        Assert.Equal(
            ["点名历史-class-a.csv", "点名历史-class-b.csv"],
            archive.Entries.Select(entry => entry.FullName).ToArray());

        foreach (var entry in archive.Entries)
        {
            using var entryStream = CopyToMemory(entry);
            var bytes = entryStream.ToArray();
            Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);

            var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(4, lines.Length);
            Assert.StartsWith("\"名单\",\"抽取时间\"", lines[0]);
            Assert.DoesNotContain("点名汇总", text);
        }
    }

    [Fact]
    public async Task ExportFilter_BySubjectAndTimeRange_NarrowsDetailButKeepsEveryMemberInSummary()
    {
        using var provider = CreateProvider();
        SeedRollCallList(provider, "class-a", withHistory: true);

        var subjects = provider.GetRequiredService<IHistoryExportService>()
            .GetSubjectOptions(HistoryExportKind.RollCall, ["class-a"]);
        Assert.Contains("数学", subjects);

        var (bySubject, subjectStream) = await ExportAsync(
            provider,
            Request(HistoryExportKind.RollCall, ["class-a"], HistoryExportFormat.Xlsx, new HistoryExportFilter(Subject: "数学")));
        using (subjectStream)
        {
            Assert.Equal(1, bySubject.RecordCount);
            var detail = Assert.Single(ReadRows(subjectStream, "点名记录"));
            Assert.Equal("数学", detail["课程"]);

            // 导出不提供“只导某个人”，筛选后名单里的每个人仍然在汇总表里，没有匹配记录的次数为 0。
            var summaries = ReadRows(subjectStream, "点名汇总");
            Assert.Equal(["Alice", "Bob", "Carol"], summaries.Select(row => row["姓名"]).ToArray());
            Assert.Equal([1, 0, 0], summaries.Select(row => Convert.ToInt32(row["累计次数"], CultureInfo.InvariantCulture)).ToArray());
            // 筛选后只有命中的成员才有单独的工作表。
            Assert.Equal(["点名记录", "点名汇总", "1 Alice"], GetSheetNames(subjectStream));
        }

        var (byTime, timeStream) = await ExportAsync(
            provider,
            Request(
                HistoryExportKind.RollCall,
                ["class-a"],
                HistoryExportFormat.Xlsx,
                new HistoryExportFilter(
                    FromInclusive: new DateTime(2026, 8, 30, 9, 30, 0),
                    ToInclusive: new DateTime(2026, 8, 30, 10, 30, 0))));
        using (timeStream)
        {
            Assert.Equal(1, byTime.RecordCount);
            Assert.Equal("2026-08-30 10:00:00", Assert.Single(ReadRows(timeStream, "点名记录"))["抽取时间"]);
        }
    }

    [Fact]
    public async Task ExportSort_AscendingWritesOldestRecordFirst()
    {
        using var provider = CreateProvider();
        SeedRollCallList(provider, "class-a", withHistory: true);

        var (result, stream) = await ExportAsync(
            provider,
            Request(
                HistoryExportKind.RollCall,
                ["class-a"],
                HistoryExportFormat.Xlsx,
                new HistoryExportFilter(),
                HistoryExportSort.TimeAscending));
        using var _ = stream;

        var details = ReadRows(stream, "点名记录");
        Assert.Equal(3, result.RecordCount);
        Assert.Equal("2026-08-30 09:00:00", details[0]["抽取时间"]);
        Assert.Equal("2026-08-30 11:00:00", details[^1]["抽取时间"]);
    }

    [Fact]
    public async Task ExportLottery_CombinesEveryPoolAndUsesPrizePoolColumns()
    {
        using var provider = CreateProvider();
        SeedPrizePool(provider, "pool-a");
        SeedPrizePool(provider, "pool-b");

        var (result, stream) = await ExportAsync(
            provider,
            Request(HistoryExportKind.Lottery, [], HistoryExportFormat.Xlsx));
        using var _ = stream;

        Assert.Equal(2, result.ProfileCount);
        Assert.Equal(2, result.RecordCount);
        Assert.Equal(2, result.FileCount);

        using var archive = OpenArchive(stream);
        Assert.Equal(
            ["抽奖历史-pool-a.xlsx", "抽奖历史-pool-b.xlsx"],
            archive.Entries.Select(entry => entry.FullName).ToArray());

        using var entryStream = CopyToMemory(archive.Entries[0]);
        Assert.Equal(["抽奖记录", "抽奖汇总", "A1 Book"], GetSheetNames(entryStream));
        Assert.Equal("pool-a", Assert.Single(ReadRows(entryStream, "抽奖记录"))["奖池"]);
        Assert.Equal("pool-a", Assert.Single(ReadRows(entryStream, "抽奖汇总"))["奖池"]);
        Assert.Equal("Book", Assert.Single(ReadRows(entryStream, "A1 Book"))["姓名"]);
    }

    [Fact]
    public async Task Export_ReturnsZeroAndWritesNothing_WhenNothingToExport()
    {
        using var provider = CreateProvider();
        var service = provider.GetRequiredService<IHistoryExportService>();

        using var stream = new MemoryStream();
        var result = await service.ExportAsync(
            stream,
            Request(HistoryExportKind.RollCall, [], HistoryExportFormat.Xlsx),
            Labels,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ProfileCount);
        Assert.Equal(0, result.RecordCount);
        Assert.Equal(0, result.FileCount);
        Assert.Equal(0, stream.Length);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static HistoryExportRequest Request(
        HistoryExportKind kind,
        IReadOnlyList<string> profileNames,
        HistoryExportFormat format,
        HistoryExportFilter? filter = null,
        HistoryExportSort sort = HistoryExportSort.TimeDescending) =>
        new(kind, profileNames, filter ?? new HistoryExportFilter(), format, sort);

    private static async Task<(HistoryExportResult Result, MemoryStream Stream)> ExportAsync(
        ServiceProvider provider,
        HistoryExportRequest request)
    {
        var service = provider.GetRequiredService<IHistoryExportService>();
        var stream = new MemoryStream();
        var result = await service.ExportAsync(stream, request, Labels, TestContext.Current.CancellationToken);
        return (result, stream);
    }

    private static ZipArchive OpenArchive(MemoryStream stream)
    {
        stream.Position = 0;
        return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
    }

    private static MemoryStream CopyToMemory(ZipArchiveEntry entry)
    {
        var buffer = new MemoryStream();
        using (var source = entry.Open())
            source.CopyTo(buffer);

        buffer.Position = 0;
        return buffer;
    }

    private static string[] GetSheetNames(MemoryStream stream)
    {
        // MiniExcel 只提供按路径读取工作表名的 API，测试里落一个临时文件再清理。
        var path = Path.Combine(Path.GetTempPath(), $"SecRandom-history-export-{Guid.NewGuid():N}.xlsx");
        try
        {
            stream.Position = 0;
            using (var file = File.Create(path))
                stream.CopyTo(file);

            return MiniExcel.GetSheetNames(path).ToArray();
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static List<IDictionary<string, object?>> ReadRows(MemoryStream stream, string sheetName)
    {
        stream.Position = 0;
        return MiniExcel
            .Query(stream, useHeaderRow: true, sheetName: sheetName, excelType: ExcelType.XLSX)
            .Cast<IDictionary<string, object?>>()
            .ToList();
    }

    private static void SeedRollCallList(ServiceProvider provider, string listName, bool withHistory)
    {
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        Assert.True(manager.CreateStudentList(listName));

        var profile = provider.GetRequiredService<IProfileService>();
        profile.LoadStudentProfile(listName);

        var alice = new Student { Name = "Alice", Id = "1", Gender = "女", Group = "一组", RecordId = Guid.NewGuid() };
        var bob = new Student { Name = "Bob", Id = "2", Gender = "男", Group = "二组", RecordId = Guid.NewGuid() };
        var carol = new Student { Name = "Carol", Id = "3", Gender = "女", Group = "一组", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(alice);
        profile.CurrentStudentList!.Students.Add(bob);
        profile.CurrentStudentList!.Students.Add(carol);
        profile.SaveProfile();

        if (!withHistory)
            return;

        profile.RecordStudentHistory([alice], new DateTime(2026, 8, 30, 9, 0, 0), 1);
        profile.RecordStudentHistory([bob], new DateTime(2026, 8, 30, 10, 0, 0), 2, drawGroup: "二组", drawGender: "男");
        profile.RecordStudentHistory(
            [alice],
            new DateTime(2026, 8, 30, 11, 0, 0),
            1,
            drawMethod: (int)DrawType.Fair,
            weights: new Dictionary<Student, double> { [alice] = 2.5 },
            courseName: "数学");
    }

    private static void SeedPrizePool(ServiceProvider provider, string poolName)
    {
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        Assert.True(manager.CreatePrizeList(poolName));

        var profile = provider.GetRequiredService<IProfileService>();
        profile.LoadPrizeProfile(poolName);

        var book = new Prize { Name = "Book", Id = "A1", Count = 1, RecordId = Guid.NewGuid() };
        profile.CurrentPrizeList!.Prizes.Add(book);
        profile.SaveProfile();

        profile.RecordPrizeHistory([book], new DateTime(2026, 8, 30, 8, 0, 0), 1);
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
