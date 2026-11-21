using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SecRandom.Core.Models.SubConfigs;

namespace SecRandom.Core.Controls;

/// <summary>
///     内置通知浮窗的四周倒计时环。
/// </summary>
/// <remarks>
///     一整条 6px 圆角描边，起点与悬浮窗长按闪抽的进度环相同：左上角之后的顶边上，顺时针推进。
///     剩余长度 = 进度 × 周长，缺口从起点顺时针扩大，所以读到的是「边框还剩多少」，
///     而不是四条各自收缩的碎条。描边颜色按窗口横向铺开（左端 = 第一个色标、右端 = 最后一个），
///     缺口处留一圈很淡的底槽，避免看起来像断线。控件只负责画，计时与取色都由宿主页面驱动。
/// </remarks>
public sealed class EdgeCountdownRing : Control
{
    /// <summary>描边粗细，与全屏通知渠道的四边进度条一致。</summary>
    private const double Thickness = 6;

    /// <summary>与浮窗 RootBorder 的 8px 圆角配套、略收一点以贴合边框内侧。</summary>
    private const double CornerRadius = 9;

    /// <summary>采样段长度：再短只会增加绘制开销，再长会让环形看起来有棱角。</summary>
    private const double SegmentLength = 3;

    private Color _startColor = NotificationCountdownPalette.DefaultStart;
    private Color _endColor = NotificationCountdownPalette.DefaultEnd;
    private bool _gradient = true;
    private double _progress = 1;

    private Size _builtSize;
    private double _builtRadius;
    private Point[] _points = [];
    private IBrush? _trackBrush;

    /// <summary>1 = 整圈完整，0 = 完全消失。</summary>
    public double Progress
    {
        get => _progress;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(clamped - _progress) < 0.0005)
                return;

            _progress = clamped;
            InvalidateVisual();
        }
    }

    /// <summary>
    ///     重建描边颜色：只有起始色与终止色两种，渐变时两者之间线性过渡，纯色时传入同一颜色。
    /// </summary>
    public void SetPalette(bool gradient, Color start, Color end)
    {
        _gradient = gradient;
        _startColor = NotificationCountdownPalette.ToOpaque(start);
        _endColor = NotificationCountdownPalette.ToOpaque(end);

        // 尺寸没变也要重建：颜色只缓存一次，绘制时不再逐帧分配画刷。
        _builtSize = default;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        var thickness = Math.Min(Thickness, Math.Min(size.Width, size.Height) / 4);
        var radius = Math.Min(CornerRadius, Math.Min(size.Width, size.Height) / 4);
        EnsureBuilt(size, thickness, radius);

        var count = _points.Length - 1;
        if (count <= 0)
            return;

        // 底槽：整圈低对比度，缺口处仍能看出边框位置，读作进度而不是断线。
        if (_trackBrush is not null)
        {
            var trackPen = new Pen(_trackBrush, thickness);
            context.DrawRectangle(
                null,
                trackPen,
                new Rect(size).Deflate(thickness / 2),
                radius,
                radius);
        }

        var visible = Math.Clamp(_progress, 0, 1);
        var tail = (1 - visible) * count; // 缺口长度（段数），缺口从起点顺时针扩大
        if (visible <= 0)
            return;

        // 颜色跟着「过去了多少时间」走：整圈同一时刻是一种颜色，从起始色平滑过渡到终止色。
        // 满圈时是起始色（蓝），时间走完时是终止色（红）；不是把蓝和红同时铺在左右两端。
        var elapsed = 1 - visible;
        var color = _gradient
            ? NotificationCountdownPalette.Sample(_startColor, _endColor, elapsed)
            : _startColor;
        var pen = new ImmutablePen(
            new ImmutableSolidColorBrush(Color.FromArgb(235, color.R, color.G, color.B)),
            thickness,
            null,
            PenLineCap.Round);

        // 只画剩余的那一段尾环：起点固定在左上角之后的顶边上，随时间顺时针被吃掉，
        // 与悬浮窗长按闪抽的 BorderedProgressRing（同一处起点、同样顺时针）方向一致。
        for (var i = 0; i < count; i++)
        {
            var segmentStart = Math.Max(i, tail);
            var segmentEnd = Math.Min(i + 1, count);
            if (segmentEnd - segmentStart <= 0.0001)
                continue;

            var from = segmentStart <= i ? _points[i] : Lerp(_points[i], _points[i + 1], segmentStart - i);
            var to = segmentEnd >= i + 1 ? _points[i + 1] : Lerp(_points[i], _points[i + 1], segmentEnd - i);
            context.DrawLine(pen, from, to);
        }
    }

    private void EnsureBuilt(Size size, double thickness, double radius)
    {
        if (_builtSize == size && Math.Abs(_builtRadius - radius) < 0.01 && _points.Length > 0)
            return;

        _builtSize = size;
        _builtRadius = radius;

        var rect = new Rect(size).Deflate(thickness / 2);
        var pieces = BuildPieces(rect, radius);

        var perimeter = 0.0;
        foreach (var piece in pieces)
            perimeter += piece.Length;

        if (perimeter <= 0)
        {
            _points = [];
            _trackBrush = null;
            return;
        }

        var count = Math.Clamp((int)Math.Round(perimeter / SegmentLength), 32, 480);
        var points = new Point[count + 1];
        for (var i = 0; i <= count; i++)
            points[i] = PointAt(pieces, i * perimeter / count);

        _points = points;
        _trackBrush = new SolidColorBrush(Color.FromArgb(36, 128, 128, 128));
    }

    /// <summary>
    ///     把圆角矩形拆成顺时针八段，起点与悬浮窗长按进度环（<see cref="BorderedProgressRing" />）一致：
    ///     左上角之后的顶边上。依次是顶边、右上角、右边、右下角、底边、左下角、左边、左上角，正好回到起点。
    /// </summary>
    private static List<Piece> BuildPieces(Rect rect, double radius)
    {
        var r = Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
        var quarter = Math.PI / 2;

        return
        [
            Piece.Line(new Point(rect.Left + r, rect.Top), new Point(rect.Right - r, rect.Top)),
            Piece.Arc(new Point(rect.Right - r, rect.Top + r), r, -quarter, quarter),
            Piece.Line(new Point(rect.Right, rect.Top + r), new Point(rect.Right, rect.Bottom - r)),
            Piece.Arc(new Point(rect.Right - r, rect.Bottom - r), r, 0, quarter),
            Piece.Line(new Point(rect.Right - r, rect.Bottom), new Point(rect.Left + r, rect.Bottom)),
            Piece.Arc(new Point(rect.Left + r, rect.Bottom - r), r, quarter, quarter),
            Piece.Line(new Point(rect.Left, rect.Bottom - r), new Point(rect.Left, rect.Top + r)),
            Piece.Arc(new Point(rect.Left + r, rect.Top + r), r, Math.PI, quarter)
        ];
    }

    private static Point PointAt(List<Piece> pieces, double distance)
    {
        foreach (var piece in pieces)
        {
            if (distance > piece.Length)
            {
                distance -= piece.Length;
                continue;
            }

            var t = piece.Length <= 0 ? 0 : Math.Clamp(distance / piece.Length, 0, 1);
            return piece.IsArc
                ? new Point(
                    piece.Center.X + piece.Radius * Math.Cos(piece.StartAngle + piece.Sweep * t),
                    piece.Center.Y + piece.Radius * Math.Sin(piece.StartAngle + piece.Sweep * t))
                : Lerp(piece.Start, piece.End, t);
        }

        return pieces[^1].End;
    }

    private static Point Lerp(Point start, Point end, double t)
    {
        return new Point(
            start.X + (end.X - start.X) * t,
            start.Y + (end.Y - start.Y) * t);
    }

    private readonly record struct Piece(
        Point Start,
        Point End,
        Point Center,
        double Radius,
        double StartAngle,
        double Sweep,
        double Length)
    {
        public bool IsArc => Sweep != 0;

        public static Piece Line(Point start, Point end)
        {
            return new Piece(start, end, default, 0, 0, 0, Distance(start, end));
        }

        public static Piece Arc(Point center, double radius, double startAngle, double sweep)
        {
            return new Piece(
                default,
                ArcEnd(center, radius, startAngle + sweep),
                center,
                radius,
                startAngle,
                sweep,
                radius * Math.Abs(sweep));
        }

        private static Point ArcEnd(Point center, double radius, double angle)
        {
            return new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
        }

        private static double Distance(Point start, Point end)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}