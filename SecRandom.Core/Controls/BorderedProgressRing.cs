using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SecRandom.Core.Services.FloatingWindow;

namespace SecRandom.Core.Controls;

/// <summary>
///     贴着控件四周画的圆角矩形进度环：轨迹是控件轮廓，进度沿轮廓从 <see cref="StartColor" />
///     推进并逐渐过渡到 <see cref="EndColor" />。
/// </summary>
/// <remarks>
///     <para>
///         悬浮窗的闪抽按钮是方角控件，需求要求"贴在那个方形的边缘，不要圆形"，所以这里不做圆弧环，
///         而是把圆角矩形轮廓按弧长等分成 <see cref="SegmentCount" /> 段逐段上色：头部永远是最新的颜色、
///         起点永远是最初的颜色，整圈填满时才呈现完整渐变。等分点里既有四条直边也有四个圆角，
///         因此进度看起来是沿控件边缘匀速走的，而不会在转角处突然加速。
///     </para>
///     <para>
///         两个端点色由宿主传入而不是写死：窗口把它设成当前主题强调色的一组深浅，进度环就跟着主题走。
///     </para>
/// </remarks>
public sealed class BorderedProgressRing : Control
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<BorderedProgressRing, double>(nameof(Progress));

    /// <summary>控件轮廓的圆角半径（DIP）。0 就是直角矩形。</summary>
    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<BorderedProgressRing, double>(nameof(CornerRadius), 8d);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<BorderedProgressRing, double>(nameof(StrokeThickness), 3d);

    /// <summary>未完成部分的底色。为 <c>null</c> 时不画轨迹，只画进度。</summary>
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<BorderedProgressRing, IBrush?>(nameof(TrackBrush));

    /// <summary>进度起点颜色（默认是闪抽长按的初始蓝）。</summary>
    public static readonly StyledProperty<Color> StartColorProperty =
        AvaloniaProperty.Register<BorderedProgressRing, Color>(
            nameof(StartColor),
            QuickDrawLongPressGesture.RingStartColor);

    /// <summary>进度头部颜色（默认是闪抽长按的完成绿）。</summary>
    public static readonly StyledProperty<Color> EndColorProperty =
        AvaloniaProperty.Register<BorderedProgressRing, Color>(
            nameof(EndColor),
            QuickDrawLongPressGesture.RingEndColor);

    private const int SegmentCount = 64;

    static BorderedProgressRing()
    {
        AffectsRender<BorderedProgressRing>(
            ProgressProperty,
            CornerRadiusProperty,
            StrokeThicknessProperty,
            TrackBrushProperty,
            StartColorProperty,
            EndColorProperty);
    }

    /// <summary>完成比例，0 到 1。</summary>
    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>进度起点颜色。</summary>
    public Color StartColor
    {
        get => GetValue(StartColorProperty);
        set => SetValue(StartColorProperty, value);
    }

    /// <summary>进度头部颜色。</summary>
    public Color EndColor
    {
        get => GetValue(EndColorProperty);
        set => SetValue(EndColorProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        var thickness = Math.Max(1, StrokeThickness);
        var inset = (thickness / 2) + 0.5;
        var rect = new Rect(inset, inset, width - (inset * 2), height - (inset * 2));
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var radius = Math.Clamp(CornerRadius, 0, Math.Min(rect.Width, rect.Height) / 2);
        var points = new Point[SegmentCount + 1];
        for (var index = 0; index <= SegmentCount; index++)
            points[index] = ResolvePoint(index / (double)SegmentCount, rect, radius);

        if (TrackBrush is { } trackBrush)
        {
            var trackPen = new Pen(trackBrush, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            for (var index = 0; index < SegmentCount; index++)
                context.DrawLine(trackPen, points[index], points[index + 1]);
        }

        var progress = Math.Clamp(Progress, 0, 1);
        if (progress <= 0)
            return;

        var filled = Math.Max(1, (int)Math.Round(progress * SegmentCount));
        var startColor = StartColor;
        var endColor = EndColor;
        for (var index = 0; index < filled; index++)
        {
            var color = QuickDrawLongPressGesture.LerpColor(startColor, endColor, (index + 0.5) / filled);
            var brush = new SolidColorBrush(color);
            var pen = new Pen(brush, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawLine(pen, points[index], points[index + 1]);
        }
    }

    /// <summary>沿圆角矩形轮廓（顺时针，从左上角之后的顶边起点算）取弧长比例处的点。</summary>
    private static Point ResolvePoint(double fraction, Rect rect, double radius)
    {
        var clamped = Math.Clamp(fraction, 0, 1);
        if (radius <= 0.5)
        {
            var plainLength = 2 * (rect.Width + rect.Height);
            var plainDistance = clamped * plainLength;
            if (plainDistance <= rect.Width)
                return new Point(rect.X + plainDistance, rect.Y);
            plainDistance -= rect.Width;
            if (plainDistance <= rect.Height)
                return new Point(rect.Right, rect.Y + plainDistance);
            plainDistance -= rect.Height;
            if (plainDistance <= rect.Width)
                return new Point(rect.Right - plainDistance, rect.Bottom);

            plainDistance -= rect.Width;
            return new Point(rect.X, rect.Bottom - plainDistance);
        }

        var straightWidth = rect.Width - (radius * 2);
        var straightHeight = rect.Height - (radius * 2);
        var quarterArc = Math.PI * radius / 2;
        var total = (2 * (straightWidth + straightHeight)) + (4 * quarterArc);
        var distance = clamped * total;

        if (distance <= straightWidth)
            return new Point(rect.X + radius + distance, rect.Y);
        distance -= straightWidth;

        if (distance <= quarterArc)
            return OnArc(rect.Right - radius, rect.Y + radius, -Math.PI / 2 + (distance / radius), radius);
        distance -= quarterArc;

        if (distance <= straightHeight)
            return new Point(rect.Right, rect.Y + radius + distance);
        distance -= straightHeight;

        if (distance <= quarterArc)
            return OnArc(rect.Right - radius, rect.Bottom - radius, distance / radius, radius);
        distance -= quarterArc;

        if (distance <= straightWidth)
            return new Point(rect.Right - radius - distance, rect.Bottom);
        distance -= straightWidth;

        if (distance <= quarterArc)
            return OnArc(rect.X + radius, rect.Bottom - radius, (Math.PI / 2) + (distance / radius), radius);
        distance -= quarterArc;

        if (distance <= straightHeight)
            return new Point(rect.X, rect.Bottom - radius - distance);
        distance -= straightHeight;

        return OnArc(rect.X + radius, rect.Y + radius, Math.PI + (distance / radius), radius);
    }

    private static Point OnArc(double centerX, double centerY, double angle, double radius)
    {
        return new Point(centerX + (radius * Math.Cos(angle)), centerY + (radius * Math.Sin(angle)));
    }
}