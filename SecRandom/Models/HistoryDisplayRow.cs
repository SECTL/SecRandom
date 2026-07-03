using System;

namespace SecRandom.Models;

/// <summary>
///     历史记录页面查看模式常量。
///     用字符串标识而非数字，便于绑定与调试。
///     个人统计模式下 SelectedMode 直接是学生/奖品名称，不使用此常量。
/// </summary>
public static class HistoryMode
{
    public const string Overview = "overview"; // 总览：按人/奖品汇总被抽次数
    public const string Records = "records";    // 抽取记录：逐条抽取事件
}

/// <summary>
///     历史记录 DataGrid 的平铺行对象。
///     不同查看模式复用同一模型，由页面按模式控制列可见性。
/// </summary>
public sealed class HistoryDisplayRow
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public int TotalCount { get; init; }
    public string DrawTime { get; init; } = string.Empty;
    public string DrawMethod { get; init; } = string.Empty;
    public int DrawNumbers { get; init; }
    public string Weight { get; init; } = string.Empty;
    public DateTime SortTime { get; init; } = DateTime.MinValue;
}
