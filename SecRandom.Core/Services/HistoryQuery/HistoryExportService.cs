using System.Globalization;
using System.Text;
using MiniExcelLibs;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.HistoryQuery;

/// <summary>
///     历史记录导出：只通过 <see cref="IHistoryQueryService" /> 读取非破坏性快照，
///     因此导出既不会切换当前活跃档案，也不会改写任何历史文件。
/// </summary>
internal sealed class HistoryExportService(IHistoryQueryService historyQueryService) : IHistoryExportService
{
    private const string BreakCourseMarker = "__break__";
    private const int MaxSheetNameLength = 31;
    private const string DrawTimeFormat = "yyyy-MM-dd HH:mm:ss";
    private const string WeightFormat = "0.00";
    private const string CsvLineBreak = "\r\n";

    private static readonly char[] InvalidSheetNameCharacters = ['\\', '/', '?', '*', '[', ']', ':'];

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
        var details = new List<HistoryExportDetail>();
        var summaries = new List<HistoryExportSummary>();
        var profileCount = 0;

        foreach (var profileName in ResolveProfileNames(request))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var records = LoadRecords(request.Kind, profileName);
            if (records is null || records.Count == 0)
                continue;

            profileCount++;
            foreach (var (key, record) in records)
            {
                summaries.Add(BuildSummary(profileName, key, record, labels));
                foreach (var item in record.Histories)
                    details.Add(BuildDetail(profileName, key, item, labels));
            }
        }

        // 没有明细就没有导出的意义：不写文件，交由调用方提示用户。
        if (details.Count == 0)
            return new HistoryExportResult(0, 0);

        SortDetails(details);
        SortSummaries(summaries);

        var detailRows = details.Select(entry => entry.ToRow(request.Kind, labels)).ToList();
        var summaryRows = summaries.Select(entry => entry.ToRow(request.Kind, labels)).ToList();

        if (request.Format == HistoryExportFormat.Csv)
        {
            // CSV 没有工作表概念，只承载抽取明细；汇总表只存在于 XLSX。
            WriteCsv(output, detailRows);
        }
        else
        {
            var (recordsSheet, summarySheet) = ResolveSheetNames(request.Kind, labels);
            WriteXlsx(output, recordsSheet, summarySheet, detailRows, summaryRows);
        }

        return new HistoryExportResult(profileCount, details.Count);
    }

    private IReadOnlyList<string> ResolveProfileNames(HistoryExportRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ProfileName))
            return [request.ProfileName];

        return request.Kind == HistoryExportKind.RollCall
            ? historyQueryService.GetStudentHistoryNames()
            : historyQueryService.GetPrizeHistoryNames();
    }

    private IReadOnlyDictionary<string, History>? LoadRecords(HistoryExportKind kind, string profileName) =>
        kind == HistoryExportKind.RollCall
            ? historyQueryService.LoadStudentHistory(profileName)?.Students
            : historyQueryService.LoadPrizeHistory(profileName)?.Prizes;

    private static HistoryExportDetail BuildDetail(
        string profileName,
        string key,
        HistoryItem item,
        HistoryExportLabels labels)
    {
        return new HistoryExportDetail(
            profileName,
            DisplayName(item.RecordName, item.RecordNumber, key),
            item.RecordNumber,
            item.RecordGender,
            item.RecordGroup,
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
        string key,
        History record,
        HistoryExportLabels labels)
    {
        var latest = record.Histories.MaxBy(item => item.DrawTime);
        var lastDrawTime = latest?.DrawTime ?? record.LastDrawnTime;

        return new HistoryExportSummary(
            profileName,
            latest is null ? key : DisplayName(latest.RecordName, latest.RecordNumber, key),
            latest?.RecordNumber ?? string.Empty,
            latest?.RecordGender ?? string.Empty,
            latest?.RecordGroup ?? string.Empty,
            record.TotalCount,
            lastDrawTime > DateTime.MinValue ? (DateTime?)lastDrawTime : null,
            FormatLatestWeight(record));
    }

    /// <summary>
    ///     历史快照里可能缺少姓名（旧记录的键就是学号/姓名本身），回落顺序与
    ///     <c>HistoryQueryService.DisplayName</c> 保持一致，避免导出出现空姓名单元格。
    /// </summary>
    private static string DisplayName(string name, string number, string fallback) =>
        !string.IsNullOrWhiteSpace(name) ? name : !string.IsNullOrWhiteSpace(number) ? number : fallback;

    private static string FormatLatestWeight(History record)
    {
        for (var index = record.Histories.Count - 1; index >= 0; index--)
        {
            var item = record.Histories[index];
            if (item.DrawMethod == (int)DrawType.Fair)
                return item.Weight.ToString(WeightFormat, CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private static void SortDetails(List<HistoryExportDetail> details)
    {
        details.Sort(static (left, right) =>
        {
            var byTime = right.DrawTime.CompareTo(left.DrawTime);
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

    private static void WriteXlsx(
        Stream output,
        string recordsSheet,
        string summarySheet,
        IReadOnlyList<Dictionary<string, object?>> detailRows,
        IReadOnlyList<Dictionary<string, object?>> summaryRows)
    {
        // 先写进内存流再整体拷贝：调用方拿到的可能是不支持定位的存储流（例如 Android SAF）。
        using var buffer = new MemoryStream();
        MiniExcel.SaveAs(
            buffer,
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [recordsSheet] = detailRows,
                [summarySheet] = summaryRows
            },
            excelType: ExcelType.XLSX);

        buffer.Position = 0;
        buffer.CopyTo(output);
    }

    private static void WriteCsv(Stream output, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var headers = rows[0].Keys.ToArray();

        // Excel 只有在文件带 UTF-8 BOM 时才按 UTF-8 解析 CSV，否则中文表头与姓名都会乱码。
        using var writer = new StreamWriter(
            output,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            bufferSize: 1024,
            leaveOpen: true);

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

    private sealed record HistoryExportDetail(
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
        string ProfileName,
        string DisplayName,
        string RecordNumber,
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
