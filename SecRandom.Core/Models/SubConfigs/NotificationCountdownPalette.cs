using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace SecRandom.Core.Models.SubConfigs;

/// <summary>
///     内置通知浮窗倒计时描边的配色规则。
/// </summary>
/// <remarks>
///     描边是沿窗口四周的一条闭合路径：进度参数 0 在右上角，沿**对角**走向左下角，1 落在左下角。
///     因此四条边各占对角线参数的一半（上/右为 0–0.5，下/左为 0.5–1），四个角的颜色天然一致，
///     整圈没有首尾接缝。色标顺序就是「右上角 → 左下角」的颜色顺序，用户加几个色标就按顺序插值。
/// </remarks>
public static class NotificationCountdownPalette
{
    public const int MinimumStops = 2;
    public const int MaximumStops = 6;

    public static Color DefaultStart => Color.Parse("#2E8BFF");

    public static Color DefaultEnd => Color.Parse("#EF4444");

    public static List<Color> CreateDefaultColors() => [DefaultStart, DefaultEnd];

    /// <summary>补齐色标数量、去掉透明度，保证至少两个色标。</summary>
    public static List<Color> NormalizeColors(IReadOnlyList<Color>? colors)
    {
        var source = colors is { Count: > 0 } ? colors : CreateDefaultColors();
        var normalized = new List<Color>(MaximumStops);
        for (var index = 0; index < Math.Min(source.Count, MaximumStops); index++)
            normalized.Add(ToOpaque(source[index]));

        while (normalized.Count < MinimumStops)
            normalized.Add(ToOpaque(normalized[^1]));

        return normalized;
    }

    /// <summary>描边永远不透明：透明度属于整个浮窗（<see cref="NotificationChannelSettings.Transparency" />）。</summary>
    public static Color ToOpaque(Color color) => Color.FromArgb(255, color.R, color.G, color.B);

    /// <summary>position 是整圈归一化位置：0 = 右上角，1 = 左下角。</summary>
    public static Color Sample(IReadOnlyList<Color> colors, double position)
    {
        if (colors.Count == 0)
            return DefaultStart;

        if (colors.Count == 1)
            return ToOpaque(colors[0]);

        var scaled = Math.Clamp(position, 0, 1) * (colors.Count - 1);
        var index = Math.Min((int)scaled, colors.Count - 2);
        var remainder = scaled - index;
        return Lerp(colors[index], colors[index + 1], remainder);
    }

    /// <summary>把整圈色标裁到一条边的 [start,end] 区间，返回该边自己的 0–1 色标。</summary>
    public static IReadOnlyList<(double Offset, Color Color)> MapEdgeStops(
        IReadOnlyList<Color> colors,
        double start,
        double end)
    {
        var normalized = NormalizeColors(colors);
        var span = end - start;
        var stops = new List<(double, Color)>(normalized.Count + 1)
        {
            (0d, Sample(normalized, start))
        };

        for (var index = 1; index < normalized.Count - 1; index++)
        {
            var global = (double)index / (normalized.Count - 1);
            if (global <= start || global >= end)
                continue;

            stops.Add(((global - start) / span, normalized[index]));
        }

        stops.Add((1d, Sample(normalized, end)));
        return stops;
    }

    private static Color Lerp(Color from, Color to, double amount) => Color.FromArgb(
        (byte)Math.Round(from.A + (to.A - from.A) * amount),
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));
}