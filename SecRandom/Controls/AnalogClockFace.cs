using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SecRandom.Controls;

public sealed class AnalogClockFace : Control
{
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

    public static readonly StyledProperty<AnalogClockMode> ModeProperty =
        AvaloniaProperty.Register<AnalogClockFace, AnalogClockMode>(nameof(Mode));

    public static readonly StyledProperty<IBrush?> SecondaryFaceBrushProperty =
        AvaloniaProperty.Register<AnalogClockFace, IBrush?>(nameof(SecondaryFaceBrush));

    static AnalogClockFace()
    {
        AffectsRender<AnalogClockFace>(
            TimeProperty,
            FaceBrushProperty,
            TickBrushProperty,
            HandBrushProperty,
            SecondHandBrushProperty,
            ModeProperty,
            SecondaryFaceBrushProperty);
    }

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

    public AnalogClockMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public IBrush? SecondaryFaceBrush
    {
        get => GetValue(SecondaryFaceBrushProperty);
        set => SetValue(SecondaryFaceBrushProperty, value);
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

        DrawFace(context, center, radius, 60, FaceBrush, ticks);
        if (Mode == AnalogClockMode.Stopwatch)
        {
            var secondaryRadius = radius * 0.28;
            var secondaryCenter = new Point(center.X, center.Y - radius * 0.43);
            DrawFace(context, secondaryCenter, secondaryRadius, 30, SecondaryFaceBrush, ticks);
            var secondaryMinuteAngle = (Time.Minute % 30 + Time.Second / 60d) * Math.PI / 15 - Math.PI / 2;
            DrawHand(context, hands, secondaryCenter, secondaryRadius * 0.7, secondaryMinuteAngle, 2.5);
            context.DrawEllipse(hands, null, secondaryCenter, 2.5, 2.5);

            var secondAngle = (Time.Second + Time.Millisecond / 1000d) * Math.PI / 30 - Math.PI / 2;
            DrawHand(context, seconds, center, radius * 0.76, secondAngle, 1.5);
            context.DrawEllipse(seconds, null, center, 4, 4);
            return;
        }

        var hourAngle = (Time.Hour % 12 + Time.Minute / 60d + Time.Second / 3600d) * Math.PI / 6 - Math.PI / 2;
        var minuteAngle = (Time.Minute + Time.Second / 60d) * Math.PI / 30 - Math.PI / 2;
        var secondHandAngle = (Time.Second + Time.Millisecond / 1000d) * Math.PI / 30 - Math.PI / 2;
        DrawHand(context, hands, center, radius * 0.48, hourAngle, 5);
        DrawHand(context, hands, center, radius * 0.7, minuteAngle, 3);
        DrawHand(context, seconds, center, radius * 0.76, secondHandAngle, 1.5);
        context.DrawEllipse(seconds, null, center, 4, 4);
    }

    private static void DrawFace(DrawingContext context, Point center, double radius, int divisions, IBrush? faceBrush, IBrush ticks)
    {
        context.DrawEllipse(faceBrush, new Pen(ticks, 1), center, radius, radius);
        for (var index = 0; index < divisions; index++)
        {
            var angle = index * Math.PI * 2 / divisions - Math.PI / 2;
            var major = divisions == 60 ? index % 5 == 0 : index % 5 == 0;
            var outer = PointAt(center, radius - 3, angle);
            var inner = PointAt(center, radius - (major ? radius * 0.26 : radius * 0.18), angle);
            context.DrawLine(new Pen(ticks, major ? 1.5 : 1), outer, inner);
        }
    }

    private static Point PointAt(Point center, double length, double angle) =>
        new(center.X + Math.Cos(angle) * length, center.Y + Math.Sin(angle) * length);

    private static void DrawHand(DrawingContext context, IBrush brush, Point center, double length, double angle, double thickness) =>
        context.DrawLine(new Pen(brush, thickness), center, PointAt(center, length, angle));
}

public enum AnalogClockMode
{
    Clock,
    Stopwatch
}
