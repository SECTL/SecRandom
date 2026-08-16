using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SecRandom.Controls;

public sealed class AnalogClockFace : Control
{
    static AnalogClockFace()
    {
        AffectsRender<AnalogClockFace>(
            TimeProperty,
            FaceBrushProperty,
            TickBrushProperty,
            HandBrushProperty,
            SecondHandBrushProperty);
    }

    public static readonly StyledProperty<DateTime> TimeProperty =
        AvaloniaProperty.Register<AnalogClockFace, DateTime>(nameof(Time));

    public static readonly StyledProperty<IBrush?> FaceBrushProperty =
        AvaloniaProperty.Register<AnalogClockFace, IBrush?>(nameof(FaceBrush));

    public static readonly StyledProperty<IBrush?> TickBrushProperty =
        AvaloniaProperty.Register<AnalogClockFace, IBrush?>(nameof(TickBrush));

    public static readonly StyledProperty<IBrush?> HandBrushProperty =
        AvaloniaProperty.Register<AnalogClockFace, IBrush?>(nameof(HandBrush));

    public static readonly StyledProperty<IBrush?> SecondHandBrushProperty =
        AvaloniaProperty.Register<AnalogClockFace, IBrush?>(nameof(SecondHandBrush));

    public DateTime Time
    {
        get => GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    public IBrush? FaceBrush
    {
        get => GetValue(FaceBrushProperty);
        set => SetValue(FaceBrushProperty, value);
    }

    public IBrush? TickBrush
    {
        get => GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public IBrush? HandBrush
    {
        get => GetValue(HandBrushProperty);
        set => SetValue(HandBrushProperty, value);
    }

    public IBrush? SecondHandBrush
    {
        get => GetValue(SecondHandBrushProperty);
        set => SetValue(SecondHandBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var diameter = Math.Min(Bounds.Width, Bounds.Height);
        if (diameter <= 0)
            return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Max(0, diameter / 2 - 10);
        var ticks = TickBrush ?? Brushes.Gray;
        var hands = HandBrush ?? Brushes.Black;
        var seconds = SecondHandBrush ?? Brushes.IndianRed;

        context.DrawEllipse(FaceBrush, new Pen(ticks, 1), center, radius, radius);
        for (var index = 0; index < 60; index++)
        {
            var angle = index * Math.PI / 30 - Math.PI / 2;
            var hourMark = index % 5 == 0;
            var outer = PointAt(center, radius - 8, angle);
            var inner = PointAt(center, radius - (hourMark ? 22 : 15), angle);
            context.DrawLine(new Pen(ticks, hourMark ? 2 : 1), outer, inner);
        }

        var hourAngle = (Time.Hour % 12 + Time.Minute / 60d + Time.Second / 3600d) * Math.PI / 6 - Math.PI / 2;
        var minuteAngle = (Time.Minute + Time.Second / 60d) * Math.PI / 30 - Math.PI / 2;
        var secondAngle = (Time.Second + Time.Millisecond / 1000d) * Math.PI / 30 - Math.PI / 2;
        DrawHand(context, hands, center, radius * 0.48, hourAngle, 5);
        DrawHand(context, hands, center, radius * 0.7, minuteAngle, 3);
        DrawHand(context, seconds, center, radius * 0.76, secondAngle, 1.5);
        context.DrawEllipse(seconds, null, center, 4, 4);
    }

    private static Point PointAt(Point center, double length, double angle) =>
        new(center.X + Math.Cos(angle) * length, center.Y + Math.Sin(angle) * length);

    private static void DrawHand(DrawingContext context, IBrush brush, Point center, double length, double angle, double thickness) =>
        context.DrawLine(new Pen(brush, thickness), center, PointAt(center, length, angle));
}
