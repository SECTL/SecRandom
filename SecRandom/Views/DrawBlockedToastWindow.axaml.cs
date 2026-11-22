using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace SecRandom.Views;

/// <summary>A small warning with no connection to draw results or notification settings.</summary>
internal sealed partial class DrawBlockedToastWindow : Window
{
    public DrawBlockedToastWindow()
    {
        InitializeComponent();
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
    }

    public void SetMessage(string message) => Notice.Message = message;

    public void PositionOnScreen(SecRandom.Views.FloatingWindow? anchor = null)
    {
        var floating = anchor ?? SecRandom.App.FloatingWindowForNotice;
        if (floating is not null)
        {
            PositionBesideFloatingWindow(floating);
            return;
        }

        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var source = desktop?.Windows.FirstOrDefault(window => !ReferenceEquals(window, this) && window.IsActive)
                     ?? desktop?.Windows.FirstOrDefault(window => !ReferenceEquals(window, this) && window.IsVisible);
        var screen = (source is null ? null : Screens.ScreenFromWindow(source)) ?? Screens.Primary;
        if (screen is null)
            return;

        var scale = screen.Scaling;
        var area = screen.WorkingArea;
        Width = Math.Min(360, Math.Max(1, area.Width / scale - 32));
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(Bounds.Height * scale);
        var margin = (int)Math.Round(16 * scale);
        Position = new PixelPoint(Math.Max(area.X, area.Right - width - margin),
            Math.Max(area.Y, area.Bottom - height - margin));
    }

    private void PositionBesideFloatingWindow(SecRandom.Views.FloatingWindow floating)
    {
        var screen = floating.Screens.ScreenFromWindow(floating) ?? floating.Screens.Primary;
        if (screen is null)
            return;

        var scale = floating.RenderScaling > 0 ? floating.RenderScaling : screen.Scaling;
        var area = screen.WorkingArea;
        var floatingWidth = Math.Max(1, (int)Math.Ceiling(floating.Bounds.Width * scale));
        var floatingHeight = Math.Max(1, (int)Math.Ceiling(floating.Bounds.Height * scale));
        var noticeWidth = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale));
        var noticeHeight = Math.Max(1, (int)Math.Ceiling(Bounds.Height * scale));
        var gap = Math.Max(8, (int)Math.Round(8 * scale));

        // Keep the notice on the side facing the screen center, then clamp it if the
        // floating window is too close to an edge or is currently hidden.
        var floatingCenter = floating.Position.X + floatingWidth / 2;
        var screenCenter = area.X + area.Width / 2;
        var preferRight = floatingCenter <= screenCenter;
        var rightX = floating.Position.X + floatingWidth + gap;
        var leftX = floating.Position.X - noticeWidth - gap;
        var x = preferRight ? rightX : leftX;
        if (x + noticeWidth > area.Right || x < area.X)
            x = preferRight ? leftX : rightX;

        var y = floating.Position.Y + (floatingHeight - noticeHeight) / 2;
        Position = new PixelPoint(
            Math.Clamp(x, area.X, Math.Max(area.X, area.Right - noticeWidth)),
            Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - noticeHeight)));
    }
}
