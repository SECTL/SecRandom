using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Interfaces;
using SecRandom.Core.Models;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services.Draw.Exceptions;
using SecRandom.Core.Services.Verification;
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
        var kernel = new ManagedVerificationKernel();

        using var shortHost = BuildHost(config, BuildProfile());
        var shortProfile = (FakeProfileService)shortHost.Services.GetRequiredService<IProfileService>();
        var shortEngine = CreateEngine(shortHost, new DeterministicRandomSource(20260626));

        ProfileRecordIdentityDiagnostics.Reset();
        var shortStudentSummary = SimulateStudents(
            shortEngine,
            kernel,
            shortProfile,
            new DeterministicSeedSource("SecRandom.FairnessAudit/student/short"),
            ShortStudentIterations,
            "学生短期抽取结果");
        var shortPrizeSummary = SimulatePrizes(
            shortEngine,
            kernel,
            shortProfile,
            new DeterministicSeedSource("SecRandom.FairnessAudit/prize/short"),
            ShortPrizeIterations,
            "奖品短期抽取结果");

        using var longHost = BuildHost(config, BuildProfile());
        var longProfile = (FakeProfileService)longHost.Services.GetRequiredService<IProfileService>();
        var longEngine = CreateEngine(longHost, new DeterministicRandomSource(20260627));

        ProfileRecordIdentityDiagnostics.Reset();
        var studentSummary = SimulateStudents(
            longEngine,
            kernel,
            longProfile,
            new DeterministicSeedSource("SecRandom.FairnessAudit/student/long"),
            StudentIterations,
            "学生长期公平性");
        var prizeSummary = SimulatePrizes(
            longEngine,
            kernel,
            longProfile,
            new DeterministicSeedSource("SecRandom.FairnessAudit/prize/long"),
            PrizeIterations,
            "奖品长期公平性");

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
                // The algorithm id and the draw type must agree, exactly as the settings page keeps them:
                // leaving the "builtin.inventory" default next to a Pan draw type would expand every prize by
                // its remaining stock (300k tickets here) and measure a pool no Pan draw would ever freeze.
                AlgorithmId = "builtin.weighted",
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

    /// <summary>
    ///     Replays the production student path: the same frozen request the app builds
    ///     (<see cref="DrawEngine.CreateStudentVerificationInput(int, IReadOnlyCollection{Student}, DrawSettingsType, string)"/>)
    ///     is sampled by the production kernel, and the drawn member is written back into history afterwards so the
    ///     fairness weights keep the same feedback loop the app has.
    /// </summary>
    private static AuditSummary SimulateStudents(
        DrawEngine engine,
        IVerificationKernel kernel,
        FakeProfileService profile,
        DeterministicSeedSource seeds,
        int iterations,
        string title)
    {
        var students = profile.CurrentStudentList?.Students.ToList()
                       ?? throw new InvalidOperationException("审计名单缺少学生列表。");
        var studentsByRecordId = students.ToDictionary(student => student.RecordId);
        if (studentsByRecordId.Count != students.Count)
            throw new InvalidOperationException("审计名单存在重复的 RecordId，无法把中奖记录映射回具体学生。");

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var seed = new byte[32];
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
        {
            var input = CreateStudentVerificationInput(engine, students, i);
            seeds.FillNextSeed(seed);
            var winners = kernel.Draw(input, seed).Winners;
            if (winners.Count != input.Count)
                throw new InvalidOperationException($"采样器返回 {winners.Count} 个中奖记录，与请求数量 {input.Count} 不一致。");

            foreach (var winner in winners)
            {
                if (!studentsByRecordId.TryGetValue(winner.RecordId, out var student))
                    throw new InvalidOperationException($"采样器返回了冻结名单之外的学生记录 {winner.RecordId:D}。");

                counts[student.Name] = counts.GetValueOrDefault(student.Name) + 1;
                BumpStudentHistory(profile.CurrentStudentHistory!, student);
            }
        }

        stopwatch.Stop();
        AssertCountsMatchIterations(counts, iterations, "学生");
        return new AuditSummary(title, iterations, stopwatch.Elapsed, counts);
    }

    /// <summary>
    ///     Replays the production prize path over a frozen <see cref="DrawEngine.CreatePrizeVerificationInput(int, IReadOnlyDictionary{string, int}, bool, string)"/>
    ///     request with an empty temporary-record set (no per-round inventory carried over).
    /// </summary>
    private static AuditSummary SimulatePrizes(
        DrawEngine engine,
        IVerificationKernel kernel,
        FakeProfileService profile,
        DeterministicSeedSource seeds,
        int iterations,
        string title)
    {
        var prizes = profile.CurrentPrizeList?.Prizes.ToList()
                     ?? throw new InvalidOperationException("审计奖池缺少奖品列表。");
        var prizesByRecordId = prizes.ToDictionary(prize => prize.RecordId);
        if (prizesByRecordId.Count != prizes.Count)
            throw new InvalidOperationException("审计奖池存在重复的 RecordId，无法把中奖记录映射回具体奖品。");

        var temporaryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var seed = new byte[32];
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
        {
            var input = CreatePrizeVerificationInput(engine, temporaryCounts, i);
            seeds.FillNextSeed(seed);
            var winners = kernel.Draw(input, seed).Winners;
            if (winners.Count != input.Count)
                throw new InvalidOperationException($"采样器返回 {winners.Count} 个中奖记录，与请求数量 {input.Count} 不一致。");

            foreach (var winner in winners)
            {
                if (!prizesByRecordId.TryGetValue(winner.RecordId, out var prize))
                    throw new InvalidOperationException($"采样器返回了冻结奖池之外的奖品记录 {winner.RecordId:D}。");

                counts[prize.Name] = counts.GetValueOrDefault(prize.Name) + 1;
                BumpPrizeHistory(profile.CurrentPrizeHistory!, prize);
            }
        }

        stopwatch.Stop();
        AssertCountsMatchIterations(counts, iterations, "奖品");
        return new AuditSummary(title, iterations, stopwatch.Elapsed, counts);
    }

    /// <summary>
    ///     <see cref="NoEligibleCandidatesException"/> means the frozen pool carried no drawable weight at all. That is a
    ///     normal "nothing to draw" outcome in the app, but in an audit it means the measured run proved nothing, so it
    ///     must fail loudly instead of being counted as a successful draw.
    /// </summary>
    private static VerificationDrawInput CreateStudentVerificationInput(
        DrawEngine engine,
        IReadOnlyList<Student> students,
        int iteration)
    {
        try
        {
            return engine.CreateStudentVerificationInput(1, students, DrawSettingsType.RollCall, courseName: "");
        }
        catch (NoEligibleCandidatesException exception)
        {
            throw new InvalidOperationException(
                $"第 {iteration + 1} 次学生抽取没有可抽取的权重，审计结果无效。", exception);
        }
    }

    private static VerificationDrawInput CreatePrizeVerificationInput(
        DrawEngine engine,
        IReadOnlyDictionary<string, int> temporaryCounts,
        int iteration)
    {
        try
        {
            return engine.CreatePrizeVerificationInput(1, temporaryCounts, includeInternalRules: true);
        }
        catch (NoEligibleCandidatesException exception)
        {
            throw new InvalidOperationException(
                $"第 {iteration + 1} 次奖品抽取没有可抽取的权重，审计结果无效。", exception);
        }
    }

    private static void AssertCountsMatchIterations(IReadOnlyDictionary<string, int> counts, int iterations, string label)
    {
        var total = counts.Values.Sum();
        if (total != iterations)
            throw new InvalidOperationException($"{label}计数合计 {total}，与迭代次数 {iterations} 不一致。");
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
<p class="muted">测量对象是生产采样路径：DrawEngine.CreateStudentVerificationInput / CreatePrizeVerificationInput 冻结出与真实抽取相同的请求，再交给 ManagedVerificationKernel 采样；每次抽取后按应用相同的方式回写历史，形成同样的历史反馈闭环。</p>
<p class="muted">本次配置：点名每轮抽 1 人（RollCall，公平权重，允许重复）；奖池每轮抽 1 份（Pan，奖盘加权无放回，临时记录为空）。每次抽取使用由 SHA-256(标签 + 计数器) 派生的新 32 字节种子，因此结果可复现且不会复用种子。</p>
<div class="panel" style="border-color:#f59e0b;background:#fffbeb">
<h2>能证明什么 / 不能证明什么</h2>
<p><b>能证明：</b>在固定名单与固定规则下，生产采样器输出的分布与期望一致——各候选没有系统性偏好，历史加权闭环没有被破坏，单次抽取不会重复命中同一记录。</p>
<p><b>不能证明：</b>不证明本机二进制未被替换，不证明真实名单的真实性与完整性，不证明抽取证明文件的存储与保全，也不证明抽取前不存在人为挑选结果。</p>
<p>注意：点名开启平均间隔保护后，候选池在采样前就被按历史次数收窄，因此长期计数会几乎完全相等（卡方接近 0）。这首先说明的是候选门控在起作用，并不单独证明采样器内部的权重分配。</p>
</div>
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
            sb.Append("</tbody></table>");
            sb.Append("<p class=\"muted\">奖品列为 0 是正常的：冻结奖池只读取临时记录（temporaryCounts），不会通过 RecordId 查询持久化历史；学生列则每次抽取会按候选人数读取历史。</p>");
            sb.Append("</div></div></body></html>");
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
            string courseName = "",
            string? drawRoundId = null) { }
        public void RecordPrizeHistory(
            IReadOnlyList<Prize> prizes,
            DateTime now,
            int requestedCount,
            int drawMethod = 0,
            string? drawRoundId = null) { }
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

    /// <summary>
    ///     Reproducible but varying 32-byte kernel seeds: every draw uses SHA-256 over a fixed label and a monotonic
    ///     counter, so a rerun replays exactly the same sequence while never reusing a seed across draws.
    /// </summary>
    private sealed class DeterministicSeedSource(string label)
    {
        private ulong _counter;

        public void FillNextSeed(Span<byte> destination)
        {
            Span<byte> material = stackalloc byte[64];
            material.Clear();
            var written = Encoding.ASCII.GetBytes(label, material);
            BinaryPrimitives.WriteUInt64LittleEndian(material[written..], ++_counter);
            SHA256.HashData(material[..(written + sizeof(ulong))], destination);
        }
    }
}
