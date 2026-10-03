using System.Globalization;
using System.IO.Compression;
using System.Text;
using MiniExcelLibs;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.HistoryQuery;

/// <summary>
///     历史记录导出：只通过 <see cref="IHistoryQueryService" /> 与 <see cref="IProfileCatalogManager" />
///     读取非破坏性快照，因此导出既不会切换当前活跃档案，也不会改写任何历史文件。
///     行的口径与设置里的点名/抽奖历史查看页保持一致：先列名单里的可抽成员，再补上只在历史里出现过的记录，
///     所以“从未被抽到的人”同样会出现在汇总工作表里。
/// </summary>
internal sealed class HistoryExportService(
    IHistoryQueryService historyQueryService,
    IProfileCatalogManager profileCatalogManager) : IHistoryExportService
{
    private const string BreakCourseMarker = "__break__";
    private const int MaxSheetNameLength = 31;
    private const string DrawTimeFormat = "yyyy-MM-dd HH:mm:ss";
    private const string WeightFormat = "0.00";
    private const string CsvLineBreak = "\r\n";

    private static readonly char[] InvalidSheetNameCharacters = ['\\', '/', '?', '*', '[', ']', ':'];

    public IReadOnlyList<string> GetProfileNames(HistoryExportKind kind)
    {
        var listNames = kind == HistoryExportKind.RollCall
            ? profileCatalogManager.GetStudentListNames()
            : profileCatalogManager.GetPrizeListNames();
        var historyNames = kind == HistoryExportKind.RollCall
            ? historyQueryService.GetStudentHistoryNames()
            : historyQueryService.GetPrizeHistoryNames();

        return listNames
            .Concat(historyNames)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<string> GetSubjectOptions(
        HistoryExportKind kind,
        IReadOnlyList<string> profileNames)
    {
        ArgumentNullException.ThrowIfNull(profileNames);

        var names = profileNames.Count > 0 ? profileNames : GetProfileNames(kind);

        return names
            .Where(profileName => !string.IsNullOrWhiteSpace(profileName))
            .SelectMany(profileName => BuildProfileViews(kind, profileName))
            .SelectMany(view => view.Items)
            .Select(item => item.CourseName)
            .Where(subject => !string.IsNullOrWhiteSpace(subject))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(subject => subject, StringComparer.CurrentCulture)
            .ToArray();
    }

    public Task<HistoryExportResult> ExportAsync(
        Stream output,
        HistoryExportRequest request,
        HistoryExportLabels labels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(labels);

        // 历史读取与表格生成都是同步 IO，换到线程池执行，避免阻塞调用方（设置页）的 UI 线程。
        return Task.Run(() => Export(output, request, labels, cancellationToken), cancellationToken);
    }

    private HistoryExportResult Export(
        Stream output,
        HistoryExportRequest request,
        HistoryExportLabels labels,
        CancellationToken cancellationToken)
    {
        var profiles = new List<ProfileExport>();
        var recordCount = 0;

        foreach (var profileName in ResolveProfileNames(request))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(profileName))
                continue;

            var details = new List<HistoryExportDetail>();
            var summaries = new List<HistoryExportSummary>();

            foreach (var view in BuildProfileViews(request.Kind, profileName))
                AppendView(details, summaries, view, profileName, request, labels);

            if (details.Count == 0 && summaries.Count == 0)
                continue;

            SortDetails(details, request.Sort);
            SortSummaries(summaries);

            profiles.Add(new ProfileExport(profileName, details, summaries));
            recordCount += details.Count;
        }

        // 没有任何可导出内容时不写文件，交由调用方提示用户。
        if (profiles.Count == 0)
            return new HistoryExportResult(0, 0, 0);

        if (profiles.Count == 1)
        {
            WriteProfileFile(output, profiles[0], request, labels);
            return new HistoryExportResult(1, recordCount, 1);
        }

        // 多个名单：一个名单一个文件，统一打包成一个 ZIP。
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var profile in profiles)
            {
                var entry = archive.CreateEntry(
                    BuildEntryName(request, labels, profile.ProfileName),
                    CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                WriteProfileFile(entryStream, profile, request, labels);
            }
        }

        return new HistoryExportResult(profiles.Count, recordCount, profiles.Count);
    }

    private IReadOnlyList<string> ResolveProfileNames(HistoryExportRequest request) =>
        request.ProfileNames is { Count: > 0 } ? request.ProfileNames : GetProfileNames(request.Kind);

    private void AppendView(
        List<HistoryExportDetail> details,
        List<HistoryExportSummary> summaries,
        ProfileRecordView view,
        string profileName,
        HistoryExportRequest request,
        HistoryExportLabels labels)
    {
        var items = view.Items.Where(item => MatchesFilter(item, request.Filter)).ToList();

        foreach (var item in items)
            details.Add(BuildDetail(profileName, view, item, labels));

        summaries.Add(BuildSummary(profileName, view, items, labels));
    }

    private static bool MatchesFilter(HistoryItem item, HistoryExportFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Subject)
            && !string.Equals(item.CourseName, filter.Subject, StringComparison.Ordinal))
            return false;

        if (filter.FromInclusive is { } from && item.DrawTime < from)
            return false;

        if (filter.ToInclusive is { } to && item.DrawTime > to)
            return false;

        return true;
    }

    /// <summary>
    ///     按名单可抽成员 + 历史孤儿记录摊平一个名单的历史。名单缺失或读取失败时退化为纯历史视图，
    ///     因此导出永远不会因为名单文件损坏而少导出历史记录。
    /// </summary>
    private IReadOnlyList<ProfileRecordView> BuildProfileViews(HistoryExportKind kind, string profileName) =>
        kind == HistoryExportKind.RollCall
            ? BuildStudentViews(profileName)
            : BuildPrizeViews(profileName);

    private IReadOnlyList<ProfileRecordView> BuildStudentViews(string profileName)
    {
        var views = new List<ProfileRecordView>();
        HashSet<string> claimedKeys = new(StringComparer.Ordinal);
        var history = historyQueryService.LoadStudentHistory(profileName);
        var list = profileCatalogManager.LoadStudentList(profileName);

        if (list is not null && list.Students.Count > 0)
        {
            var students = list.Students.ToList();
            var uniqueLegacyKeys = ProfileRecordIdentity.BuildUniqueStudentLegacyKeySet(students);

            foreach (var student in students.Where(student => student.IsCandidate))
            {
                var key = ProfileRecordIdentity.EnsureRecordId(student);
                var record = history is null
                    ? null
                    : ProfileRecordIdentity.GetStudentHistory(history, student, uniqueLegacyKeys.Contains);

                claimedKeys.Add(key);
                foreach (var legacyKey in ProfileRecordIdentity.GetLegacyStudentHistoryKeys(student)
                             .Where(uniqueLegacyKeys.Contains))
                    claimedKeys.Add(legacyKey);

                views.Add(new ProfileRecordView(
                    key, true, student.Id, student.Name, student.Gender, student.Group, record?.Histories ?? []));
            }
        }

        AppendOrphanViews(views, claimedKeys, history?.Students);
        return views;
    }

    private IReadOnlyList<ProfileRecordView> BuildPrizeViews(string profileName)
    {
        var views = new List<ProfileRecordView>();
        HashSet<string> claimedKeys = new(StringComparer.Ordinal);
        var history = historyQueryService.LoadPrizeHistory(profileName);
        var list = profileCatalogManager.LoadPrizeList(profileName);

        if (list is not null && list.Prizes.Count > 0)
        {
            var prizes = list.Prizes.ToList();
            var uniqueLegacyKeys = ProfileRecordIdentity.BuildUniquePrizeLegacyKeySet(prizes);

            foreach (var prize in prizes.Where(prize => prize.IsCandidate))
            {
                var key = ProfileRecordIdentity.EnsureRecordId(prize);
                var record = history is null
                    ? null
                    : ProfileRecordIdentity.GetPrizeHistory(history, prize, uniqueLegacyKeys.Contains);

                claimedKeys.Add(key);
                foreach (var legacyKey in ProfileRecordIdentity.GetLegacyPrizeHistoryKeys(prize)
                             .Where(uniqueLegacyKeys.Contains))
                    claimedKeys.Add(legacyKey);

                views.Add(new ProfileRecordView(
                    key, true, prize.Id, prize.Name, string.Empty, string.Empty, record?.Histories ?? []));
            }
        }

        AppendOrphanViews(views, claimedKeys, history?.Prizes);
        return views;
    }

    private static void AppendOrphanViews(
        List<ProfileRecordView> views,
        HashSet<string> claimedKeys,
        IReadOnlyDictionary<string, History>? records)
    {
        if (records is null)
            return;

        foreach (var (key, record) in records)
        {
            if (claimedKeys.Contains(key))
                continue;

            views.Add(new ProfileRecordView(key, false, string.Empty, string.Empty, string.Empty, string.Empty, record.Histories));
        }
    }

    private static HistoryExportDetail BuildDetail(
        string profileName,
        ProfileRecordView view,
        HistoryItem item,
        HistoryExportLabels labels)
    {
        return new HistoryExportDetail(
            view.Key,
            profileName,
            FirstNonBlank(item.RecordName, view.Name, view.Key),
            FirstNonBlank(item.RecordNumber, view.Number),
            FirstNonBlank(item.RecordGender, view.Gender),
            FirstNonBlank(item.RecordGroup, view.Group),
            item.DrawTime,
            item.DrawMethod == (int)DrawType.Random ? labels.MethodRandom : labels.MethodWeight,
            item.DrawNumbers,
            string.IsNullOrWhiteSpace(item.DrawGender) ? labels.AllGenders : item.DrawGender,
            string.IsNullOrWhiteSpace(item.DrawGroup) ? labels.AllGroups : item.DrawGroup,
            item.CourseName == BreakCourseMarker ? labels.BreakSubject : item.CourseName,
            item.DrawMethod == (int)DrawType.Fair
                ? item.Weight.ToString(WeightFormat, CultureInfo.InvariantCulture)
                : string.Empty);
    }

    private static HistoryExportSummary BuildSummary(
        string profileName,
        ProfileRecordView view,
        IReadOnlyList<HistoryItem> items,
        HistoryExportLabels labels)
    {
        var identity = ResolveIdentity(view, items);
        var latest = items.Count > 0 ? items.MaxBy(item => item.DrawTime) : null;
        var latestFair = items
            .Where(item => item.DrawMethod == (int)DrawType.Fair)
            .MaxBy(item => item.DrawTime);

        return new HistoryExportSummary(
            view.Key,
            profileName,
            identity.Number,
            identity.Name,
            identity.Gender,
            identity.Group,
            items.Count,
            latest?.DrawTime,
            latestFair is null ? string.Empty : latestFair.Weight.ToString(WeightFormat, CultureInfo.InvariantCulture));
    }

    /// <summary>
    ///     名单成员用名单上的身份（未抽到也照样出现在汇总表），历史孤儿记录退化为最新一条快照，
    ///     都没有时退回历史键，与查看页的口径一致。
    /// </summary>
    private static (string Number, string Name, string Gender, string Group) ResolveIdentity(
        ProfileRecordView view,
        IReadOnlyList<HistoryItem> items)
    {
        if (view.FromRoster)
            return (view.Number, FirstNonBlank(view.Name, view.Key), view.Gender, view.Group);

        var latest = items.Count > 0 ? items.MaxBy(item => item.DrawTime) : null;
        return (
            FirstNonBlank(latest?.RecordNumber),
            FirstNonBlank(latest?.RecordName, latest?.RecordNumber, view.Key),
            FirstNonBlank(latest?.RecordGender),
            FirstNonBlank(latest?.RecordGroup));
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static void SortDetails(List<HistoryExportDetail> details, HistoryExportSort sort)
    {
        details.Sort((left, right) =>
        {
            var byTime = sort == HistoryExportSort.TimeAscending
                ? left.DrawTime.CompareTo(right.DrawTime)
                : right.DrawTime.CompareTo(left.DrawTime);
            if (byTime != 0)
                return byTime;

            var byProfile = string.CompareOrdinal(left.ProfileName, right.ProfileName);
            return byProfile != 0 ? byProfile : string.CompareOrdinal(left.DisplayName, right.DisplayName);
        });
    }

    private static void SortSummaries(List<HistoryExportSummary> summaries)
    {
        summaries.Sort(static (left, right) =>
        {
            var byProfile = string.CompareOrdinal(left.ProfileName, right.ProfileName);
            if (byProfile != 0)
                return byProfile;

            var byNumber = CompareRecordKeys(left.RecordNumber, right.RecordNumber);
            return byNumber != 0
                ? byNumber
                : string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCulture);
        });
    }

    /// <summary>
    ///     汇总表按学号排序：纯数字学号优先并按数值比较，其余按当前区域文化的名称顺序，
    ///     与名单展示使用的排序口径一致。
    /// </summary>
    private static int CompareRecordKeys(string left, string right)
    {
        var leftIsNumber = int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var leftValue);
        var rightIsNumber = int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rightValue);

        if (leftIsNumber && rightIsNumber)
            return leftValue.CompareTo(rightValue);

        if (leftIsNumber != rightIsNumber)
            return leftIsNumber ? -1 : 1;

        return string.Compare(left, right, StringComparison.CurrentCulture);
    }

    private static string BuildEntryName(
        HistoryExportRequest request,
        HistoryExportLabels labels,
        string profileName)
    {
        var kindName = request.Kind == HistoryExportKind.RollCall ? labels.RollCallFileName : labels.LotteryFileName;
        var extension = request.Format == HistoryExportFormat.Csv ? "csv" : "xlsx";
        return $"{SanitizeFileName(kindName)}-{SanitizeFileName(profileName)}.{extension}";
    }

    private static string SanitizeFileName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
            builder.Append(Path.GetInvalidFileNameChars().Contains(character) ? '_' : character);

        var sanitized = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "history" : sanitized;
    }

    private static void WriteProfileFile(
        Stream output,
        ProfileExport profile,
        HistoryExportRequest request,
        HistoryExportLabels labels)
    {
        var detailRows = profile.Details.Select(entry => entry.ToRow(request.Kind, labels)).ToList();
        var summaryRows = profile.Summaries.Select(entry => entry.ToRow(request.Kind, labels)).ToList();

        if (request.Format == HistoryExportFormat.Csv)
        {
            // CSV 没有工作表概念，只承载抽取明细；汇总表与每人一张表只存在于 XLSX。
            WriteCsv(output, detailRows);
            return;
        }

        var (recordsSheet, summarySheet) = ResolveSheetNames(request.Kind, labels);
        var sheets = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [recordsSheet] = detailRows,
            [summarySheet] = summaryRows
        };

        // 有记录的成员再各占一张工作表，方便直接翻到某个人的全部抽取记录；
        // 一次都没被抽到（或本次筛选没有命中）的成员不单独建表。
        var detailsByRecord = profile.Details
            .GroupBy(detail => detail.RecordKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        HashSet<string> usedSheetNames = new(StringComparer.OrdinalIgnoreCase) { recordsSheet, summarySheet };

        foreach (var summary in profile.Summaries)
        {
            if (!detailsByRecord.TryGetValue(summary.RecordKey, out var rows))
                continue;

            sheets[BuildUniqueSheetName(BuildRecordSheetName(summary), usedSheetNames)] =
                rows.Select(entry => entry.ToRow(request.Kind, labels)).ToList();
        }

        WriteXlsx(output, sheets);
    }

    /// <summary>单个成员的工作表名：有学号时带上学号，避免同名学生互相覆盖。</summary>
    private static string BuildRecordSheetName(HistoryExportSummary summary) =>
        string.IsNullOrWhiteSpace(summary.RecordNumber)
            ? summary.DisplayName
            : $"{summary.RecordNumber} {summary.DisplayName}".Trim();

    private static string BuildUniqueSheetName(string baseName, HashSet<string> usedSheetNames)
    {
        var sanitized = SanitizeSheetName(baseName, "Sheet");
        var candidate = sanitized;
        var suffix = 2;

        while (!usedSheetNames.Add(candidate))
        {
            var tail = $" ({suffix++})";
            var head = sanitized.Length + tail.Length > MaxSheetNameLength
                ? sanitized[..(MaxSheetNameLength - tail.Length)]
                : sanitized;
            candidate = $"{head}{tail}";
        }

        return candidate;
    }

    private static (string Records, string Summary) ResolveSheetNames(
        HistoryExportKind kind,
        HistoryExportLabels labels)
    {
        var records = SanitizeSheetName(
            kind == HistoryExportKind.RollCall ? labels.RollCallRecordsSheet : labels.LotteryRecordsSheet,
            "Records");
        var summary = SanitizeSheetName(
            kind == HistoryExportKind.RollCall ? labels.RollCallSummarySheet : labels.LotterySummarySheet,
            "Summary");

        if (string.Equals(records, summary, StringComparison.OrdinalIgnoreCase))
            summary = SanitizeSheetName($"{summary}2", "Summary2");

        return (records, summary);
    }

    private static string SanitizeSheetName(string name, string fallback)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
            builder.Append(InvalidSheetNameCharacters.Contains(character) ? '_' : character);

        var sanitized = builder.ToString().Trim().Trim('\'');
        if (sanitized.Length > MaxSheetNameLength)
            sanitized = sanitized[..MaxSheetNameLength];

        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static void WriteXlsx(Stream output, Dictionary<string, object> sheets)
    {
        // 先写进内存流再整体拷贝：调用方拿到的可能是不支持定位的存储流（例如 Android SAF）。
        using var buffer = new MemoryStream();
        MiniExcel.SaveAs(buffer, sheets, excelType: ExcelType.XLSX);

        buffer.Position = 0;
        buffer.CopyTo(output);
    }

    private static void WriteCsv(Stream output, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        // Excel 只有在文件带 UTF-8 BOM 时才按 UTF-8 解析 CSV，否则中文表头与姓名都会乱码。
        using var writer = new StreamWriter(
            output,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            bufferSize: 1024,
            leaveOpen: true);

        if (rows.Count == 0)
        {
            writer.Flush();
            return;
        }

        var headers = rows[0].Keys.ToArray();
        writer.Write(string.Join(',', headers.Select(EscapeCsv)));
        writer.Write(CsvLineBreak);

        foreach (var row in rows)
        {
            writer.Write(string.Join(',', headers.Select(header => EscapeCsv(row[header]?.ToString() ?? string.Empty))));
            writer.Write(CsvLineBreak);
        }

        writer.Flush();
    }

    private static string EscapeCsv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private sealed record ProfileExport(
        string ProfileName,
        List<HistoryExportDetail> Details,
        List<HistoryExportSummary> Summaries);

    private sealed record ProfileRecordView(
        string Key,
        bool FromRoster,
        string Number,
        string Name,
        string Gender,
        string Group,
        IReadOnlyList<HistoryItem> Items);

    private sealed record HistoryExportDetail(
        string RecordKey,
        string ProfileName,
        string DisplayName,
        string RecordNumber,
        string Gender,
        string Group,
        DateTime DrawTime,
        string DrawMethod,
        int DrawCount,
        string DrawGender,
        string DrawGroup,
        string Subject,
        string Weight)
    {
        public Dictionary<string, object?> ToRow(HistoryExportKind kind, HistoryExportLabels labels) => new()
        {
            [kind == HistoryExportKind.RollCall ? labels.RollCallProfileName : labels.LotteryProfileName] = ProfileName,
            [labels.DrawTime] = DrawTime.ToString(DrawTimeFormat, CultureInfo.InvariantCulture),
            [labels.RecordNumber] = RecordNumber,
            [labels.RecordName] = DisplayName,
            [labels.Gender] = Gender,
            [labels.Group] = Group,
            [labels.DrawMethod] = DrawMethod,
            [labels.DrawCount] = DrawCount,
            [labels.DrawGender] = DrawGender,
            [labels.DrawGroup] = DrawGroup,
            [labels.Subject] = Subject,
            [labels.Weight] = Weight
        };
    }

    private sealed record HistoryExportSummary(
        string RecordKey,
        string ProfileName,
        string RecordNumber,
        string DisplayName,
        string Gender,
        string Group,
        int TotalCount,
        DateTime? LastDrawTime,
        string Weight)
    {
        public Dictionary<string, object?> ToRow(HistoryExportKind kind, HistoryExportLabels labels) => new()
        {
            [kind == HistoryExportKind.RollCall ? labels.RollCallProfileName : labels.LotteryProfileName] = ProfileName,
            [labels.RecordNumber] = RecordNumber,
            [labels.RecordName] = DisplayName,
            [labels.Gender] = Gender,
            [labels.Group] = Group,
            [labels.TotalCount] = TotalCount,
            [labels.LastDrawTime] = LastDrawTime?.ToString(DrawTimeFormat, CultureInfo.InvariantCulture) ?? string.Empty,
            [labels.Weight] = Weight
        };
    }
}
