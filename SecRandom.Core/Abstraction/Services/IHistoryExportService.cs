namespace SecRandom.Core.Abstraction.Services;

/// <summary>
///     历史记录导出的一侧：点名历史或抽奖历史。
/// </summary>
public enum HistoryExportKind
{
    RollCall,
    Lottery
}

/// <summary>
///     历史记录导出的文件格式。
/// </summary>
public enum HistoryExportFormat
{
    Xlsx,
    Csv
}

/// <summary>
///     一次导出请求。<see cref="ProfileName" /> 为空表示导出该类型下的全部名单/奖池。
/// </summary>
public sealed record HistoryExportRequest(
    HistoryExportKind Kind,
    string? ProfileName,
    HistoryExportFormat Format);

/// <summary>
///     导出结果统计：实际写出的名单/奖池数量与抽取记录条数。
/// </summary>
public sealed record HistoryExportResult(int ProfileCount, int RecordCount);

/// <summary>
///     导出文件中的用户可见文案。Core 只持有通用本地化资源，导出用的列头、单元格取值与工作表名
///     由调用方按当前语言提供，导出服务本身只负责投影与写文件。
/// </summary>
public sealed record HistoryExportLabels
{
    public required string RollCallProfileName { get; init; }
    public required string LotteryProfileName { get; init; }
    public required string DrawTime { get; init; }
    public required string RecordNumber { get; init; }
    public required string RecordName { get; init; }
    public required string Gender { get; init; }
    public required string Group { get; init; }
    public required string DrawMethod { get; init; }
    public required string DrawCount { get; init; }
    public required string DrawGender { get; init; }
    public required string DrawGroup { get; init; }
    public required string Subject { get; init; }
    public required string Weight { get; init; }
    public required string TotalCount { get; init; }
    public required string LastDrawTime { get; init; }
    public required string MethodRandom { get; init; }
    public required string MethodWeight { get; init; }
    public required string AllGenders { get; init; }
    public required string AllGroups { get; init; }
    public required string BreakSubject { get; init; }
    public required string RollCallRecordsSheet { get; init; }
    public required string RollCallSummarySheet { get; init; }
    public required string LotteryRecordsSheet { get; init; }
    public required string LotterySummarySheet { get; init; }
}

/// <summary>
///     把点名/抽奖历史导出成表格文件。
/// </summary>
public interface IHistoryExportService
{
    /// <summary>
    ///     把指定类型的历史记录写入 <paramref name="output" />。
    ///     全程只读取非破坏性快照，不切换当前活跃档案，也不改写任何历史文件；
    ///     没有可导出记录时不写入任何内容，返回零计数由调用方提示。
    /// </summary>
    Task<HistoryExportResult> ExportAsync(
        Stream output,
        HistoryExportRequest request,
        HistoryExportLabels labels,
        CancellationToken cancellationToken = default);
}
