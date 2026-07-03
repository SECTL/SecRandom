using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SecRandom.Models;
using SR = SecRandom.Langs.MainPages.History.Resources;

namespace SecRandom.Converters;

/// <summary>
///     将历史记录查看模式的原始键转换为本地化显示名。
///     "overview"/"records" 映射为本地化文案，其余（学生/奖品名称）原样返回。
/// </summary>
public sealed class HistoryModeNameConverter : IValueConverter
{
    public static readonly HistoryModeNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            HistoryMode.Overview => SR.C_ModeOverview,
            HistoryMode.Records => SR.C_ModeRecords,
            var other => other ?? string.Empty
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
