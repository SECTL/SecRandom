using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Interfaces;
using SecRandom.Core.Models;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Shared.Models.Profile;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

var report = AuditRunner.Run();
var outputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "fairness-audit"));
Directory.CreateDirectory(outputDir);

var htmlPath = Path.Combine(outputDir, "fairness-audit.html");
File.WriteAllText(htmlPath, report.ToHtml(), Encoding.UTF8);
Console.WriteLine(htmlPath);

var roundReport = RoundFairnessAudit.Run();
var roundHtmlPath = Path.Combine(outputDir, "round-fairness-audit.html");
File.WriteAllText(roundHtmlPath, roundReport.ToHtml(), Encoding.UTF8);
Console.WriteLine(roundHtmlPath);

var cryptoReport = CryptoRandomAudit.Run(outputDir);
var cryptoHtmlPath = Path.Combine(outputDir, "crypto-random-audit.html");
File.WriteAllText(cryptoHtmlPath, cryptoReport.ToHtml(), Encoding.UTF8);
Console.WriteLine(cryptoHtmlPath);

static class AuditRunner
{
    private const int ShortStudentIterations = 6_000;
    private const int ShortPrizeIterations = 6_000;
    private const int StudentIterations = 120_000;
    private const int PrizeIterations = 120_000;

    public static AuditReport Run()
    {
        var config = BuildConfig();

        using var shortHost = BuildHost(config, BuildProfile());

        ProfileRecordIdentityDiagnostics.Reset();
        var shortEngine = CreateEngine(shortHost, new DeterministicRandomSource(20260626));
        var shortStudentSummary = SimulateStudents(shortEngine, (FakeProfileService)shortHost.Services.GetRequiredService<IProfileService>(), ShortStudentIterations, "学生短期抽取结果");
        var shortPrizeSummary = SimulatePrizes(shortEngine, (FakeProfileService)shortHost.Services.GetRequiredService<IProfileService>(), ShortPrizeIterations, "奖品短期抽取结果");

        ProfileRecordIdentityDiagnostics.Reset();
        using var longHost = BuildHost(config, BuildProfile());

        var longEngine = CreateEngine(longHost, new DeterministicRandomSource(20260627));
        var studentSummary = SimulateStudents(longEngine, (FakeProfileService)longHost.Services.GetRequiredService<IProfileService>(), StudentIterations, "学生长期公平性");
        var prizeSummary = SimulatePrizes(longEngine, (FakeProfileService)longHost.Services.GetRequiredService<IProfileService>(), PrizeIterations, "奖品长期公平性");

        return new AuditReport(
            shortStudentSummary,
            shortPrizeSummary,
            studentSummary,
            prizeSummary,
            new HistoryReadStats(
                ProfileRecordIdentityDiagnostics.StudentPrimaryLookups,
                ProfileRecordIdentityDiagnostics.StudentLegacyLookups,
                ProfileRecordIdentityDiagnostics.PrizePrimaryLookups,
                ProfileRecordIdentityDiagnostics.PrizeLegacyLookups));
    }

    private static IHost BuildHost(MainConfigModel config, FakeProfileService profile)
    {
        var configService = new InMemoryConfigService(config);

        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
                services.AddSingleton<IProfileService>(profile);
                services.AddSingleton<ConfigServiceBase>(configService);
                services.AddSingleton<MainConfigHandler>();
            })
            .Build();
    }

    private static DrawEngine CreateEngine(IHost host, IRandomSource randomSource)
    {
        return new DrawEngine(
            host.Services.GetRequiredService<MainConfigHandler>(),
            host.Services.GetRequiredService<IProfileService>(),
            host.Services.GetRequiredService<ILogger<DrawEngine>>(),
            randomSource);
    }

    private static MainConfigModel BuildConfig()
    {
        return new MainConfigModel
        {
            FairDrawSettings = new FairDrawSettingsConfig
            {
                FairDraw = true,
                FairDrawGroup = false,
                FairDrawGender = false,
                ColdStartEnabled = false,
                EnableAvgGapProtection = true,
                ShieldEnabled = false
            },
            RollCallSettings = new RollCallSettingsConfig
            {
                DrawMode = DrawMode.Repeat,
                DrawType = DrawType.Fair,
                HalfRepeat = 1
            },
            LotterySettings = new LotterySettingsConfig
            {
                DrawMode = DrawMode.Repeat,
                DrawType = LotteryDrawType.Pan,
                HalfRepeat = 1
            },
            DefaultDrawSettings = new DefaultDrawSettingsConfig(),
            General = new GeneralSettingsConfig()
        };
    }

    private static FakeProfileService BuildProfile()
    {
        var students = new List<Student>
        {
            new Student { Name = "Alice", Group = "A", Gender = "F" },
            new Student { Name = "Bob", Group = "A", Gender = "M" },
            new Student { Name = "Cindy", Group = "B", Gender = "F" },
            new Student { Name = "David", Group = "B", Gender = "M" },
            new Student { Name = "Ethan", Group = "C", Gender = "M" },
            new Student { Name = "Fiona", Group = "C", Gender = "F" }
        };

        var prizes = new List<Prize>
        {
            new Prize { Name = "Book", Weight = 1, Count = 100_000 },
            new Prize { Name = "Pen", Weight = 1, Count = 100_000 },
            new Prize { Name = "Sticker", Weight = 1, Count = 100_000 }
        };

        var studentList = new StudentList { Students = new ObservableCollection<Student>(students) };
        var prizeList = new PrizeList { Prizes = new ObservableCollection<Prize>(prizes) };
        var studentHistory = new StudentHistory();
        var prizeHistory = new PrizeHistory();

        foreach (var student in students)
        {
            var recordId = ProfileRecordIdentity.EnsureRecordId(student);
            studentHistory.Students[recordId] = new History();
        }

        foreach (var prize in prizes)
        {
            var recordId = ProfileRecordIdentity.EnsureRecordId(prize);
            prizeHistory.Prizes[recordId] = new History();
        }

        return new FakeProfileService(studentList, studentHistory, prizeList, prizeHistory);
    }

    private static AuditSummary SimulateStudents(DrawEngine engine, FakeProfileService profile, int iterations, string title)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
        {
            var result = engine.DrawStudent(1, _ => true);
            if (!result.IsSuccess)
                throw new InvalidOperationException($"student draw failed: {result.Status}");

            var student = result.Result[0];
            counts[student.Name] = counts.GetValueOrDefault(student.Name) + 1;
            BumpStudentHistory(profile.CurrentStudentHistory!, student);
        }

        stopwatch.Stop();
        return new AuditSummary(title, iterations, stopwatch.Elapsed, counts);
    }

    private static AuditSummary SimulatePrizes(DrawEngine engine, FakeProfileService profile, int iterations, string title)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
        {
            var result = engine.DrawPrize(1, _ => true);
            if (!result.IsSuccess)
                throw new InvalidOperationException($"prize draw failed: {result.Status}");

            var prize = result.Result[0];
            counts[prize.Name] = counts.GetValueOrDefault(prize.Name) + 1;
            BumpPrizeHistory(profile.CurrentPrizeHistory!, prize);
        }

        stopwatch.Stop();
        return new AuditSummary(title, iterations, stopwatch.Elapsed, counts);
    }

    private static void BumpStudentHistory(StudentHistory history, Student student)
    {
        var recordId = ProfileRecordIdentity.EnsureRecordId(student);
        var item = history.Students.GetValueOrDefault(recordId) ?? new History();
        item.TotalCount++;
        item.LastDrawnTime = DateTime.UtcNow;
        history.Students[recordId] = item;
        history.TotalStats++;
        history.TotalRounds++;
    }

    private static void BumpPrizeHistory(PrizeHistory history, Prize prize)
    {
        var recordId = ProfileRecordIdentity.EnsureRecordId(prize);
        var item = history.Prizes.GetValueOrDefault(recordId) ?? new History();
        item.TotalCount++;
        item.LastDrawnTime = DateTime.UtcNow;
        history.Prizes[recordId] = item;
        history.TotalStats++;
        history.TotalRounds++;
    }

    public sealed record AuditReport(
        AuditSummary ShortStudentSummary,
        AuditSummary ShortPrizeSummary,
        AuditSummary StudentSummary,
        AuditSummary PrizeSummary,
        HistoryReadStats HistoryReadStats)
    {
        public string ToHtml()
        {
            var sb = new StringBuilder();
            sb.Append("""
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>SecRandom 公平性验证</title>
<style>
body{font-family:system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;background:#f8fafc;color:#0f172a;margin:0;padding:24px}
.wrap{max-width:1200px;margin:0 auto}
.panel{background:#fff;border:1px solid #cbd5e1;border-radius:8px;padding:16px 18px;margin:0 0 16px}
table{border-collapse:collapse;width:100%}
th,td{border-bottom:1px solid #e2e8f0;padding:8px 10px;text-align:left;font-size:14px;vertical-align:middle}
th{background:#f1f5f9}
.muted{color:#64748b}
.bar{height:12px;background:#2563eb;border-radius:999px}
.bar-wrap{background:#e2e8f0;border-radius:999px;height:12px;overflow:hidden}
</style>
</head>
<body>
<div class="wrap">
<h1>SecRandom 公平性验证</h1>
<p class="muted">先看短期样本波动，再看长期分布和历史记录读取压力。</p>
""");

            AppendSummary(sb, ShortStudentSummary);
            AppendSummary(sb, ShortPrizeSummary);
            AppendSummary(sb, StudentSummary);
            AppendSummary(sb, PrizeSummary);

            sb.Append("""
<div class="panel">
<h2>历史记录读取压力</h2>
<table>
<thead><tr><th>类型</th><th>主键读取</th><th>Legacy 读取</th><th>Legacy 占比</th><th>每次抽取读取</th></tr></thead>
<tbody>
""");
            AppendHistoryRow(sb, "学生", HistoryReadStats.StudentPrimaryLookups, HistoryReadStats.StudentLegacyLookups, StudentSummary.Total);
            AppendHistoryRow(sb, "奖品", HistoryReadStats.PrizePrimaryLookups, HistoryReadStats.PrizeLegacyLookups, PrizeSummary.Total);
            sb.Append("</tbody></table></div></div></body></html>");
            return sb.ToString();
        }

        private static void AppendSummary(StringBuilder sb, AuditSummary summary)
        {
            var expected = summary.Total / (double)summary.Draws.Count;
            var maxDeviation = summary.Draws.Values.Max(v => Math.Abs(v - expected) / expected);
            var chiSquare = summary.Draws.Values.Sum(v => Math.Pow(v - expected, 2) / expected);

            sb.Append("""
<div class="panel">
""");
            sb.Append($"<h2>{WebUtility.HtmlEncode(summary.Title)}</h2>");
            sb.Append($"<p class=\"muted\">总次数 {summary.Total:n0}，耗时 {summary.Elapsed.TotalSeconds:n2} 秒，期望值 {expected:n2}，最大相对偏差 {maxDeviation:P2}，卡方 {chiSquare:n2}。</p>");
            sb.Append("<table><thead><tr><th>候选</th><th>次数</th><th>占比</th><th>可视化</th></tr></thead><tbody>");

            foreach (var pair in summary.Draws.OrderByDescending(x => x.Value))
            {
                var ratio = pair.Value / (double)summary.Total;
                var width = Math.Min(100.0, ratio * 600.0);
                sb.Append("<tr>");
                sb.Append($"<td>{WebUtility.HtmlEncode(pair.Key)}</td>");
                sb.Append($"<td>{pair.Value:n0}</td>");
                sb.Append($"<td>{ratio:P2}</td>");
                sb.Append($"<td><div class=\"bar-wrap\"><div class=\"bar\" style=\"width:{width:0.##}%\"></div></div></td>");
                sb.Append("</tr>");
            }

            sb.Append("</tbody></table></div>");
        }

        private static void AppendHistoryRow(StringBuilder sb, string label, long primary, long legacy, int totalDraws)
        {
            var totalReads = primary + legacy;
            var legacyRatio = totalReads == 0 ? 0 : legacy / (double)totalReads;
            var readsPerDraw = totalDraws == 0 ? 0 : totalReads / (double)totalDraws;
            sb.Append($"<tr><td>{WebUtility.HtmlEncode(label)}</td><td>{primary:n0}</td><td>{legacy:n0}</td><td>{legacyRatio:P2}</td><td>{readsPerDraw:n2}</td></tr>");
        }
    }

    public sealed record AuditSummary(string Title, int Total, TimeSpan Elapsed, IReadOnlyDictionary<string, int> Draws);

    public sealed record HistoryReadStats(long StudentPrimaryLookups, long StudentLegacyLookups, long PrizePrimaryLookups, long PrizeLegacyLookups);

    private sealed class FakeProfileService : IProfileService
    {
        public FakeProfileService(StudentList studentList, StudentHistory studentHistory, PrizeList prizeList, PrizeHistory prizeHistory)
        {
            CurrentStudentList = studentList;
            CurrentStudentHistory = studentHistory;
            CurrentPrizeList = prizeList;
            CurrentPrizeHistory = prizeHistory;
        }

        public StudentList? CurrentStudentList { get; }
        public StudentHistory? CurrentStudentHistory { get; }
        public PrizeList? CurrentPrizeList { get; }
        public PrizeHistory? CurrentPrizeHistory { get; }
        public StudentListConfig? StudentListConfig => null;
        public StudentHistoryConfig? StudentHistoryConfig => null;
        public PrizeListConfig? PrizeListConfig => null;
        public PrizeHistoryConfig? PrizeHistoryConfig => null;
        public void LoadStudentProfile(string name, bool saveCurrent = true) { }
        public void LoadPrizeProfile(string name, bool saveCurrent = true) { }
        public void RecordStudentHistory(
            IReadOnlyList<Student> students,
            DateTime now,
            int requestedCount,
            string drawGroup = "",
            string drawGender = "",
            int drawMethod = 0,
            IReadOnlyDictionary<Student, double>? weights = null,
            string courseName = "") { }
        public void RecordPrizeHistory(IReadOnlyList<Prize> prizes, DateTime now, int requestedCount) { }
        public void ClearCurrentStudentHistory() { }
        public void ClearCurrentPrizeHistory() { }
        public void SaveProfile() { }
    }

    private sealed class InMemoryConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => fallback is MainConfigModel ? (T)(object)config : fallback;
        public override void SaveConfig<T>(T config) { }
        public override void DeleteConfig<T>(T config) { }
    }

    private sealed class DeterministicRandomSource(int seed) : IRandomSource
    {
        private readonly Random _random = new(seed);

        public int NextInt32(int maxExclusive) => _random.Next(maxExclusive);
        public double NextDouble() => _random.NextDouble();
    }
}
