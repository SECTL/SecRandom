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
///     历史记录导出的文件格式。多名单导出时两者都打包成 ZIP。
/// </summary>
public enum HistoryExportFormat
{
    Xlsx,
    Csv
}

/// <summary>
///     抽取记录的排列顺序。
/// </summary>
public enum HistoryExportSort
{
    TimeDescending,
    TimeAscending
}

/// <summary>
///     导出筛选条件。<see cref="Subject" /> 为空表示全部课程，时间为空表示不限时间。
///     导出始终包含所选名单/奖池里的全部人员/奖品，不支持只导某一个人。
/// </summary>
public sealed record HistoryExportFilter(
    string? Subject = null,
    DateTime? FromInclusive = null,
    DateTime? ToInclusive = null);

/// <summary>
///     一次导出请求。<see cref="ProfileNames" /> 为空表示导出该类型下的全部名单/奖池；
///     选中多个名单时结果会打包成一个 ZIP，每个名单一个文件。
/// </summary>
public sealed record HistoryExportRequest(
    HistoryExportKind Kind,
    IReadOnlyList<string> ProfileNames,
    HistoryExportFilter Filter,
    HistoryExportFormat Format,
    HistoryExportSort Sort = HistoryExportSort.TimeDescending);

/// <summary>
///     导出结果统计：名单/奖池数量、抽取记录条数、写出的文件条数。
/// </summary>
public sealed record HistoryExportResult(int ProfileCount, int RecordCount, int FileCount);

/// <summary>
///     导出文件中的用户可见文案。Core 只持有通用本地化资源，导出用的列头、单元格取值、工作表名
///     与压缩包内的文件名由调用方按当前语言提供，导出服务本身只负责投影与写文件。
/// </summary>
public sealed record HistoryExportLabels
{
    public required string RollCallProfileName { get; init; }
    public required string LotteryProfileName { get; init; }
    /// <summary>压缩包内点名历史文件的名称前缀。</summary>
    public required string RollCallFileName { get; init; }
    /// <summary>压缩包内抽奖历史文件的名称前缀。</summary>
    public required string LotteryFileName { get; init; }
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
    ///     该类型下可供导出的名单/奖池（已存在的名单与已有历史的集合，按名称排序）。
    /// </summary>
    IReadOnlyList<string> GetProfileNames(HistoryExportKind kind);

    /// <summary>
    ///     所选名单/奖池里出现过的课程名（去重并按当前区域文化排序），用于导出页的课程下拉。
    /// </summary>
    IReadOnlyList<string> GetSubjectOptions(
        HistoryExportKind kind,
        IReadOnlyList<string> profileNames);

    /// <summary>
    ///     把历史记录写入 <paramref name="output" />：单个名单写一个文件，多个名单写一个 ZIP。
    ///     全程只读取非破坏性快照，不切换当前活跃档案，也不改写任何历史文件；
    ///     没有可导出内容时不写入任何内容，返回零计数由调用方提示。
    /// </summary>
    Task<HistoryExportResult> ExportAsync(
        Stream output,
        HistoryExportRequest request,
        HistoryExportLabels labels,
        CancellationToken cancellationToken = default);
}
