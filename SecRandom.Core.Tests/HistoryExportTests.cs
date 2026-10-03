using System.Globalization;
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
    public async Task ExportXlsx_WritesDetailAndSummarySheets()
    {
        using var provider = CreateProvider();
        SeedStudentHistory(provider, "class-a");

        var (result, stream) = await ExportAsync(
            provider,
            new HistoryExportRequest(HistoryExportKind.RollCall, "class-a", HistoryExportFormat.Xlsx));
        using var _ = stream;

        Assert.Equal(1, result.ProfileCount);
        Assert.Equal(3, result.RecordCount);

        Assert.Equal(["点名记录", "点名汇总"], GetSheetNames(stream));

        var details = ReadRows(stream, "点名记录");
        Assert.Equal(3, details.Count);
        // 明细按抽取时间倒序：最新的一条是 11:00 的公平抽取。
        Assert.Equal("2026-08-30 11:00:00", details[0]["抽取时间"]);
        Assert.Equal("class-a", details[0]["名单"]);
        Assert.Equal("1", details[0]["学号"]);
        Assert.Equal("Alice", details[0]["姓名"]);
        Assert.Equal("女", details[0]["性别"]);
        Assert.Equal("一组", details[0]["小组"]);
        Assert.Equal("公平抽取", details[0]["抽取方式"]);
        Assert.Equal(1, Convert.ToInt32(details[0]["抽取人数"], CultureInfo.InvariantCulture));
        Assert.Equal("不限性别", details[0]["性别限制"]);
        Assert.Equal("不限小组", details[0]["小组限制"]);
        Assert.Equal("数学", details[0]["课程"]);
        Assert.Equal("2.50", details[0]["权重"]);

        // 随机抽取不写权重，并保留当次的小组/性别限制。
        Assert.Equal("2026-08-30 10:00:00", details[1]["抽取时间"]);
        Assert.Equal("Bob", details[1]["姓名"]);
        Assert.Equal("随机抽取", details[1]["抽取方式"]);
        Assert.Equal(2, Convert.ToInt32(details[1]["抽取人数"], CultureInfo.InvariantCulture));
        Assert.Equal("男", details[1]["性别限制"]);
        Assert.Equal("二组", details[1]["小组限制"]);
        // 空单元格读回来是 null（MiniExcel 不区分空串与空单元格）。
        Assert.True(string.IsNullOrEmpty(details[1]["权重"]?.ToString()));

        var summaries = ReadRows(stream, "点名汇总");
        Assert.Equal(2, summaries.Count);
        Assert.Equal("Alice", summaries[0]["姓名"]);
        Assert.Equal(2, Convert.ToInt32(summaries[0]["累计次数"], CultureInfo.InvariantCulture));
        Assert.Equal("2026-08-30 11:00:00", summaries[0]["最近抽取时间"]);
        Assert.Equal("2.50", summaries[0]["权重"]);
        Assert.Equal("Bob", summaries[1]["姓名"]);
        Assert.Equal(1, Convert.ToInt32(summaries[1]["累计次数"], CultureInfo.InvariantCulture));
        Assert.Equal("2026-08-30 10:00:00", summaries[1]["最近抽取时间"]);
        Assert.True(string.IsNullOrEmpty(summaries[1]["权重"]?.ToString()));
    }

    [Fact]
    public async Task ExportCsv_WritesUtf8BomAndDetailRowsOnly()
    {
        using var provider = CreateProvider();
        SeedStudentHistory(provider, "class-a");

        var (result, stream) = await ExportAsync(
            provider,
            new HistoryExportRequest(HistoryExportKind.RollCall, "class-a", HistoryExportFormat.Csv));
        using var _ = stream;

        Assert.Equal(3, result.RecordCount);

        var bytes = stream.ToArray();
        Assert.True(bytes.Length > 3);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);

        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("\"名单\",\"抽取时间\",\"学号\",\"姓名\"", lines[0]);
        Assert.Contains("\"Alice\"", lines[1]);
        Assert.Contains("\"公平抽取\"", lines[1]);
        Assert.DoesNotContain("点名汇总", text);
    }

    [Fact]
    public async Task ExportAllProfiles_CombinesEveryHistoryFile()
    {
        using var provider = CreateProvider();
        SeedStudentHistory(provider, "class-a");
        SeedPrizeHistory(provider, "pool-a");
        SeedPrizeHistory(provider, "pool-b");

        var (result, stream) = await ExportAsync(
            provider,
            new HistoryExportRequest(HistoryExportKind.Lottery, null, HistoryExportFormat.Xlsx));
        using var _ = stream;

        Assert.Equal(2, result.ProfileCount);
        Assert.Equal(2, result.RecordCount);
        Assert.Equal(["抽奖记录", "抽奖汇总"], GetSheetNames(stream));

        var details = ReadRows(stream, "抽奖记录");
        Assert.Equal(["pool-a", "pool-b"], details.Select(row => row["奖池"]).ToArray());
        Assert.All(details, row => Assert.Equal("Book", row["姓名"]));

        // 汇总表同样要用奖池口径的列头，而不是点名历史的“名单”。
        var summaries = ReadRows(stream, "抽奖汇总");
        Assert.Equal(["pool-a", "pool-b"], summaries.Select(row => row["奖池"]).ToArray());
        Assert.DoesNotContain("名单", summaries[0].Keys);
    }

    [Fact]
    public async Task Export_ReturnsZeroAndWritesNothing_WhenNoHistoryExists()
    {
        using var provider = CreateProvider();
        var service = provider.GetRequiredService<IHistoryExportService>();

        using var stream = new MemoryStream();
        var result = await service.ExportAsync(
            stream,
            new HistoryExportRequest(HistoryExportKind.RollCall, null, HistoryExportFormat.Xlsx),
            Labels,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ProfileCount);
        Assert.Equal(0, result.RecordCount);
        Assert.Equal(0, stream.Length);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static async Task<(HistoryExportResult Result, MemoryStream Stream)> ExportAsync(
        ServiceProvider provider,
        HistoryExportRequest request)
    {
        var service = provider.GetRequiredService<IHistoryExportService>();
        var stream = new MemoryStream();
        var result = await service.ExportAsync(stream, request, Labels, TestContext.Current.CancellationToken);
        return (result, stream);
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

    private static void SeedStudentHistory(ServiceProvider provider, string listName)
    {
        var manager = provider.GetRequiredService<IProfileCatalogManager>();
        Assert.True(manager.CreateStudentList(listName));

        var profile = provider.GetRequiredService<IProfileService>();
        profile.LoadStudentProfile(listName);

        var alice = new Student
        {
            Name = "Alice", Id = "1", Gender = "女", Group = "一组", RecordId = Guid.NewGuid()
        };
        var bob = new Student
        {
            Name = "Bob", Id = "2", Gender = "男", Group = "二组", RecordId = Guid.NewGuid()
        };
        profile.CurrentStudentList!.Students.Add(alice);
        profile.CurrentStudentList!.Students.Add(bob);
        profile.SaveProfile();

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

    private static void SeedPrizeHistory(ServiceProvider provider, string poolName)
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
