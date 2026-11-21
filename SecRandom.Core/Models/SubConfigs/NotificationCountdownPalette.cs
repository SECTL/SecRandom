using System;
using Avalonia.Media;

namespace SecRandom.Core.Models.SubConfigs;

/// <summary>
///     内置通知浮窗倒计时描边的配色规则。
/// </summary>
/// <remarks>
///     倒计时描边只有两种颜色：起始色与终止色（渐变模式下两者之间线性过渡，纯色模式下两者相同）。
///     颜色跟着剩余时间走，而不是沿窗口铺开：满圈时整圈都是起始色（蓝），时间走完时整圈都是终止色（红），
///     中间每一帧都是两者之间的插值，所以读起来是「颜色慢慢变过去」。
///     整圈同一时刻只有一种颜色，环上就不会出现终止色撞起始色的接缝。
/// </remarks>
public static class NotificationCountdownPalette
{
    public static Color DefaultStart => Color.Parse("#2E8BFF");

    public static Color DefaultEnd => Color.Parse("#EF4444");

    /// <summary>描边永远不透明：透明度属于整个浮窗（<see cref="NotificationChannelSettings.Transparency" />）。</summary>
    public static Color ToOpaque(Color color) => Color.FromArgb(255, color.R, color.G, color.B);

    /// <summary>position 是窗口横向归一化位置：0 = 左端（起始色），1 = 右端（终止色）。</summary>
    public static Color Sample(Color start, Color end, double position)
    {
        var amount = Math.Clamp(position, 0, 1);
        return Lerp(ToOpaque(start), ToOpaque(end), amount);
    }

    private static Color Lerp(Color from, Color to, double amount) => Color.FromArgb(
        (byte)Math.Round(from.A + (to.A - from.A) * amount),
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));
}