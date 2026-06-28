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

public static class RoundFairnessAudit
{
    private const int RoundCount = 100;
    private const int StudentCount = 50;
    private const int MinRoundDrawCount = 5;
    private const int MaxRoundDrawCount = 10;

    public static RoundFairnessReport Run()
    {
        var config = BuildConfig();
        using var host = BuildHost(config, BuildProfile());
        IAppHost.Host = host;

        ProfileRecordIdentityDiagnostics.Reset();

        var profile = (AuditProfileService)host.Services.GetRequiredService<IProfileService>();
        var engine = new DrawEngine(new DeterministicRandomSource(20260628));
        var result = Simulate(engine, profile);

        return result with
        {
            HistoryReadStats = new HistoryReadStats(
                ProfileRecordIdentityDiagnostics.StudentPrimaryLookups,
                ProfileRecordIdentityDiagnostics.StudentLegacyLookups,
                ProfileRecordIdentityDiagnostics.PrizePrimaryLookups,
                ProfileRecordIdentityDiagnostics.PrizeLegacyLookups)
        };
    }

    private static RoundFairnessReport Simulate(DrawEngine engine, AuditProfileService profile)
    {
        var stopwatch = Stopwatch.StartNew();
        var random = new Random(20260629);
        var students = profile.CurrentStudentList?.Students.ToList() ?? [];
        var studentByRecordId = students.ToDictionary(ProfileRecordIdentity.EnsureRecordId, s => s, StringComparer.Ordinal);

        var counts = studentByRecordId.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var maxGapByStudent = studentByRecordId.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var lastSeenRound = studentByRecordId.Keys.ToDictionary(id => id, _ => -1, StringComparer.Ordinal);
        var groupCounts = studentByRecordId.Values
            .Select(s => s.Group)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(group => group, _ => 0, StringComparer.Ordinal);
        var genderCounts = studentByRecordId.Values
            .Select(s => s.Gender)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(gender => gender, _ => 0, StringComparer.Ordinal);

        var roundSnapshots = new List<RoundSnapshot>(RoundCount);
        var drawSizes = new List<int>(RoundCount);
        var duplicateViolations = 0;
        var selectedTotal = 0;

        for (var round = 0; round < RoundCount; round++)
        {
            var drawCount = random.Next(MinRoundDrawCount, MaxRoundDrawCount + 1);
            drawSizes.Add(drawCount);

            var drawResult = engine.DrawStudent(drawCount, _ => true);
            if (!drawResult.IsSuccess)
                throw new InvalidOperationException($"round draw failed: {drawResult.Status}");

            var seenThisRound = new HashSet<string>(StringComparer.Ordinal);
            var winners = new List<string>(drawCount);

            foreach (var student in drawResult.Result)
            {
                var recordId = ProfileRecordIdentity.EnsureRecordId(student);
                if (!seenThisRound.Add(recordId))
                    duplicateViolations++;

                winners.Add(student.Name);
                counts[recordId]++;
                groupCounts[student.Group] = groupCounts.GetValueOrDefault(student.Group) + 1;
                genderCounts[student.Gender] = genderCounts.GetValueOrDefault(student.Gender) + 1;

                if (lastSeenRound[recordId] >= 0)
                {
                    var gap = round - lastSeenRound[recordId] - 1;
                    if (gap > maxGapByStudent[recordId])
                        maxGapByStudent[recordId] = gap;
                }

                lastSeenRound[recordId] = round;
                BumpStudentHistory(profile.CurrentStudentHistory!, student, DateTime.Now);
                selectedTotal++;
            }

            profile.CurrentStudentHistory!.TotalRounds++;

            roundSnapshots.Add(new RoundSnapshot(round + 1, drawCount, winners));
        }

        stopwatch.Stop();

        var expectedPerStudent = selectedTotal / (double)StudentCount;
        var studentRows = students
            .Select(student =>
            {
                var recordId = ProfileRecordIdentity.EnsureRecordId(student);
                var count = counts[recordId];
                var expected = expectedPerStudent;
                var deviation = expected == 0 ? 0 : Math.Abs(count - expected) / expected;
                return new StudentFairnessRow(
                    student.Name,
                    student.Group,
                    student.Gender,
                    count,
                    expected,
                    deviation,
                    maxGapByStudent[recordId],
                    lastSeenRound[recordId] + 1);
            })
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ToList();

        var expectedPerGroup = selectedTotal / (double)groupCounts.Count;
        var groupRows = groupCounts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                var deviation = expectedPerGroup == 0 ? 0 : Math.Abs(pair.Value - expectedPerGroup) / expectedPerGroup;
                return new BucketFairnessRow(pair.Key, pair.Value, expectedPerGroup, deviation);
            })
            .ToList();

        var expectedPerGender = selectedTotal / (double)genderCounts.Count;
        var genderRows = genderCounts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                var deviation = expectedPerGender == 0 ? 0 : Math.Abs(pair.Value - expectedPerGender) / expectedPerGender;
                return new BucketFairnessRow(pair.Key, pair.Value, expectedPerGender, deviation);
            })
            .ToList();

        return new RoundFairnessReport(
            RoundCount,
            students.Count,
            selectedTotal,
            stopwatch.Elapsed,
            duplicateViolations,
            expectedPerStudent,
            studentRows.Max(row => row.Deviation),
            groupRows.Max(row => row.Deviation),
            genderRows.Max(row => row.Deviation),
            roundSnapshots,
            drawSizes,
            studentRows,
            groupRows,
        genderRows,
        new HistoryReadStats(0, 0, 0, 0));
    }

    private static IHost BuildHost(MainConfigModel config, AuditProfileService profile)
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

    private static MainConfigModel BuildConfig()
    {
        return new MainConfigModel
        {
            FairDrawSettings = new FairDrawSettingsConfig
            {
                FairDraw = true,
                FairDrawGroup = true,
                FairDrawGender = true,
                FairDrawTime = true,
                EnableAvgGapProtection = true,
                ShieldEnabled = false,
                ColdStartEnabled = true
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

    private static AuditProfileService BuildProfile()
    {
        var students = new List<Student>(StudentCount);
        for (var i = 0; i < StudentCount; i++)
        {
            var group = $"G{(i / 10) + 1}";
            var gender = i % 2 == 0 ? "F" : "M";
            students.Add(new Student
            {
                Name = $"Student{i + 1:00}",
                Group = group,
                Gender = gender
            });
        }

        var studentList = new StudentList { Students = new ObservableCollection<Student>(students) };
        var studentHistory = new StudentHistory();

        var startTime = DateTime.Now.AddDays(-29);
        var groupKeys = students.Select(s => s.Group).Distinct(StringComparer.Ordinal).ToList();
        var genderKeys = students.Select(s => s.Gender).Distinct(StringComparer.Ordinal).ToList();

        foreach (var group in groupKeys)
            studentHistory.GroupStats[group] = 0;

        foreach (var gender in genderKeys)
            studentHistory.GenderStatus[gender] = 0;

        for (var i = 0; i < students.Count; i++)
        {
            var student = students[i];
            var recordId = ProfileRecordIdentity.EnsureRecordId(student);
            studentHistory.Students[recordId] = new History
            {
                LastDrawnTime = startTime.AddDays(-(i % 30))
            };
        }

        return new AuditProfileService(studentList, studentHistory);
    }

    private static void BumpStudentHistory(StudentHistory history, Student student, DateTime drawnTime)
    {
        var recordId = ProfileRecordIdentity.EnsureRecordId(student);
        var item = history.Students.GetValueOrDefault(recordId) ?? new History();
        item.TotalCount++;
        item.LastDrawnTime = drawnTime;
        history.Students[recordId] = item;

        history.TotalStats++;
        history.GroupStats[student.Group] = history.GroupStats.GetValueOrDefault(student.Group) + 1;
        history.GenderStatus[student.Gender] = history.GenderStatus.GetValueOrDefault(student.Gender) + 1;
    }

    public sealed record RoundFairnessReport(
        int Rounds,
        int StudentCount,
        int TotalDraws,
        TimeSpan Elapsed,
        int DuplicateViolations,
        double ExpectedPerStudent,
        double MaxStudentDeviation,
        double MaxGroupDeviation,
        double MaxGenderDeviation,
        IReadOnlyList<RoundSnapshot> RoundSnapshots,
        IReadOnlyList<int> RoundSizes,
        IReadOnlyList<StudentFairnessRow> StudentRows,
        IReadOnlyList<BucketFairnessRow> GroupRows,
        IReadOnlyList<BucketFairnessRow> GenderRows,
        HistoryReadStats HistoryReadStats)
    {
        public string ToHtml()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html>");
            sb.AppendLine("<html lang=\"zh-CN\">");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"utf-8\">");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.AppendLine("<title>SecRandom 100 轮公平抽取审计</title>");
            sb.AppendLine("""
<style>
body{font-family:system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;background:#f8fafc;color:#0f172a;margin:0;padding:24px}
.wrap{max-width:1320px;margin:0 auto}
.panel{background:#fff;border:1px solid #cbd5e1;border-radius:8px;padding:16px 18px;margin:0 0 16px}
table{border-collapse:collapse;width:100%}
th,td{border-bottom:1px solid #e2e8f0;padding:8px 10px;text-align:left;font-size:14px;vertical-align:top}
th{background:#f1f5f9}
.muted{color:#64748b}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:12px}
.metric{background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:12px 14px}
.metric b{display:block;font-size:24px;line-height:1.1;margin-top:4px}
.bar{height:12px;background:#2563eb;border-radius:999px}
.bar-wrap{background:#e2e8f0;border-radius:999px;height:12px;overflow:hidden}
.good{color:#166534}
.warn{color:#9a3412}
.round-list{font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;word-break:break-word}
</style>
</head>
<body>
<div class="wrap">
<h1>SecRandom 100 轮公平抽取审计</h1>
<p class="muted">50 名学生，100 轮，每轮抽 5 到 10 人。单轮内无重复，轮后只累积历史权重，不保留当轮名单。</p>
""");

            AppendMetrics(sb);
            AppendShortTerm(sb);
            AppendStudentRows(sb);
            AppendBucketRows(sb, "小组分布", GroupRows);
            AppendBucketRows(sb, "性别分布", GenderRows);
            AppendHistoryPressure(sb);

            sb.AppendLine("</div></body></html>");
            return sb.ToString();
        }

        private void AppendMetrics(StringBuilder sb)
        {
            var averagePerRound = TotalDraws / (double)Rounds;
            var averageRoundSize = RoundSizes.Average();
            var minRoundSize = RoundSizes.Min();
            var maxRoundSize = RoundSizes.Max();

            sb.AppendLine("<div class=\"panel\">");
            sb.AppendLine("<h2>总览</h2>");
            sb.AppendLine("<div class=\"grid\">");
            Metric(sb, "轮数", Rounds.ToString("n0"));
            Metric(sb, "总抽取人数", TotalDraws.ToString("n0"));
            Metric(sb, "平均每轮", averagePerRound.ToString("n2"));
            Metric(sb, "轮大小范围", $"{minRoundSize:n0} - {maxRoundSize:n0}");
            Metric(sb, "平均轮大小", averageRoundSize.ToString("n2"));
            Metric(sb, "平均到每人", ExpectedPerStudent.ToString("n2"));
            Metric(sb, "最大个人偏差", MaxStudentDeviation.ToString("P2"));
            Metric(sb, "重复违规", DuplicateViolations.ToString("n0"));
            sb.AppendLine("</div>");

            var verdict = DuplicateViolations == 0 && MaxStudentDeviation <= 0.45 && MaxGroupDeviation <= 0.25 && MaxGenderDeviation <= 0.25
                ? "通过"
                : "需观察";
            sb.AppendLine($"<p class=\"{(verdict == "通过" ? "good" : "warn")}\">判定：{verdict}。组别最大偏差 {MaxGroupDeviation:P2}，性别最大偏差 {MaxGenderDeviation:P2}。</p>");
            sb.AppendLine("</div>");
        }

        private void AppendShortTerm(StringBuilder sb)
        {
            sb.AppendLine("<div class=\"panel\">");
            sb.AppendLine("<h2>短期抽取结果</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>轮次</th><th>人数</th><th>名单</th></tr></thead>");
            sb.AppendLine("<tbody>");

            foreach (var snapshot in RoundSnapshots.Take(10))
            {
                sb.AppendLine("<tr>");
                sb.AppendLine($"<td>{snapshot.Round:n0}</td>");
                sb.AppendLine($"<td>{snapshot.Count:n0}</td>");
                sb.AppendLine($"<td class=\"round-list\">{WebUtility.HtmlEncode(string.Join("、", snapshot.Winners))}</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</div>");
        }

        private void AppendStudentRows(StringBuilder sb)
        {
            sb.AppendLine("<div class=\"panel\">");
            sb.AppendLine("<h2>长期公平性</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>学生</th><th>小组</th><th>性别</th><th>次数</th><th>期望</th><th>偏差</th><th>最长间隔</th><th>最近轮次</th><th>分布</th></tr></thead>");
            sb.AppendLine("<tbody>");

            var maxCount = StudentRows.Max(row => row.Count);
            foreach (var row in StudentRows)
            {
                var width = maxCount == 0 ? 0 : row.Count / (double)maxCount * 100.0;
                sb.AppendLine("<tr>");
                sb.AppendLine($"<td>{WebUtility.HtmlEncode(row.Name)}</td>");
                sb.AppendLine($"<td>{WebUtility.HtmlEncode(row.Group)}</td>");
                sb.AppendLine($"<td>{WebUtility.HtmlEncode(row.Gender)}</td>");
                sb.AppendLine($"<td>{row.Count:n0}</td>");
                sb.AppendLine($"<td>{row.Expected:n2}</td>");
                sb.AppendLine($"<td>{row.Deviation:P2}</td>");
                sb.AppendLine($"<td>{row.MaxGap:n0}</td>");
                sb.AppendLine($"<td>{row.LastSeenRound:n0}</td>");
                sb.AppendLine($"<td><div class=\"bar-wrap\"><div class=\"bar\" style=\"width:{width:0.##}%\"></div></div></td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</div>");
        }

        private void AppendBucketRows(StringBuilder sb, string title, IReadOnlyList<BucketFairnessRow> rows)
        {
            sb.AppendLine("<div class=\"panel\">");
            sb.AppendLine($"<h2>{WebUtility.HtmlEncode(title)}</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>分组</th><th>次数</th><th>期望</th><th>偏差</th><th>分布</th></tr></thead>");
            sb.AppendLine("<tbody>");

            var maxCount = rows.Max(row => row.Count);
            foreach (var row in rows)
            {
                var width = maxCount == 0 ? 0 : row.Count / (double)maxCount * 100.0;
                sb.AppendLine("<tr>");
                sb.AppendLine($"<td>{WebUtility.HtmlEncode(row.Label)}</td>");
                sb.AppendLine($"<td>{row.Count:n0}</td>");
                sb.AppendLine($"<td>{row.Expected:n2}</td>");
                sb.AppendLine($"<td>{row.Deviation:P2}</td>");
                sb.AppendLine($"<td><div class=\"bar-wrap\"><div class=\"bar\" style=\"width:{width:0.##}%\"></div></div></td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</div>");
        }

        private void AppendHistoryPressure(StringBuilder sb)
        {
            var totalReads = HistoryReadStats.StudentPrimaryLookups + HistoryReadStats.StudentLegacyLookups;
            var readsPerDraw = TotalDraws == 0 ? 0 : totalReads / (double)TotalDraws;
            var legacyRatio = totalReads == 0 ? 0 : HistoryReadStats.StudentLegacyLookups / (double)totalReads;

            sb.AppendLine("<div class=\"panel\">");
            sb.AppendLine("<h2>历史记录读取压力</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>类型</th><th>主键读取</th><th>Legacy 读取</th><th>Legacy 占比</th><th>每次抽取读取</th></tr></thead>");
            sb.AppendLine("<tbody>");
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>学生</td><td>{HistoryReadStats.StudentPrimaryLookups:n0}</td><td>{HistoryReadStats.StudentLegacyLookups:n0}</td><td>{legacyRatio:P2}</td><td>{readsPerDraw:n2}</td>");
            sb.AppendLine("</tr>");
            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</div>");
        }

        private static void Metric(StringBuilder sb, string label, string value)
        {
            sb.AppendLine("<div class=\"metric\">");
            sb.AppendLine($"<span class=\"muted\">{WebUtility.HtmlEncode(label)}</span>");
            sb.AppendLine($"<b>{WebUtility.HtmlEncode(value)}</b>");
            sb.AppendLine("</div>");
        }
    }

    public sealed record RoundSnapshot(int Round, int Count, IReadOnlyList<string> Winners);

    public sealed record StudentFairnessRow(
        string Name,
        string Group,
        string Gender,
        int Count,
        double Expected,
        double Deviation,
        int MaxGap,
        int LastSeenRound);

    public sealed record BucketFairnessRow(string Label, int Count, double Expected, double Deviation);

    public sealed record HistoryReadStats(
        long StudentPrimaryLookups,
        long StudentLegacyLookups,
        long PrizePrimaryLookups,
        long PrizeLegacyLookups);

    private sealed class AuditProfileService : IProfileService
    {
        public AuditProfileService(StudentList studentList, StudentHistory studentHistory)
        {
            CurrentStudentList = studentList;
            CurrentStudentHistory = studentHistory;
        }

        public StudentList? CurrentStudentList { get; }
        public StudentHistory? CurrentStudentHistory { get; }
        public PrizeList? CurrentPrizeList { get; }
        public PrizeHistory? CurrentPrizeHistory { get; }
        public StudentListConfig? StudentListConfig => null;
        public StudentHistoryConfig? StudentHistoryConfig => null;
        public PrizeListConfig? PrizeListConfig => null;
        public PrizeHistoryConfig? PrizeHistoryConfig => null;
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
