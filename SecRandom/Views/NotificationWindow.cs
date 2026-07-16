using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SecRandom.Core.Models.SubConfigs;

namespace SecRandom.Views;

public sealed class NotificationWindow : Window
{
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _content = new() { TextWrapping = TextWrapping.Wrap };
    private int _revision;

    public NotificationWindow()
    {
        CanResize = false;
        ShowInTaskbar = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Content = new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.Parse("#FF202020")),
            Child = new StackPanel
            {
                Spacing = 10,
                MinWidth = 280,
                MaxWidth = 560,
                Children =
                {
                    _title,
                    _content
                }
            }
        };
    }

    public void Show(
        string title,
        IReadOnlyCollection<string> items,
        bool animate,
        int autoCloseTime,
        NotificationChannelSettings settings)
    {
        _title.Text = title;
        _content.Text = string.Join(Environment.NewLine, items);
        Opacity = Math.Clamp(settings.Transparency, 20, 100) / 100d;
        Show();
        Position = GetPosition(settings);

        var revision = ++_revision;
        if (animate)
            _ = FadeInAsync(revision);
        if (autoCloseTime > 0)
            _ = HideAfterDelayAsync(revision, autoCloseTime);
    }

    private PixelPoint GetPosition(NotificationChannelSettings settings)
    {
        var area = ResolveScreen(settings).WorkingArea;
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var x = settings.WindowPosition switch
        {
            1 => area.Center.X - width / 2,
            2 => area.Center.X - width / 2,
            3 => area.X,
            4 => area.Right - width,
            _ => area.Center.X - width / 2
        };
        var y = settings.WindowPosition switch
        {
            1 => area.Y,
            2 => area.Bottom - height,
            3 => area.Center.Y - height / 2,
            4 => area.Center.Y - height / 2,
            _ => area.Center.Y - height / 2
        };

        return new PixelPoint(
            Math.Clamp(x + settings.HorizontalOffset, area.X, Math.Max(area.X, area.Right - width)),
            Math.Clamp(y + settings.VerticalOffset, area.Y, Math.Max(area.Y, area.Bottom - height)));
    }

    private Avalonia.Platform.Screen ResolveScreen(NotificationChannelSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.EnabledMonitor))
        {
            var selected = Screens.All.FirstOrDefault(screen => string.Equals(
                screen.DisplayName,
                settings.EnabledMonitor,
                StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
                return selected;
        }

        return Screens.Primary ?? Screens.All.First();
    }

    private async Task FadeInAsync(int revision)
    {
        var targetOpacity = Opacity;
        Opacity = 0;
        for (var step = 1; step <= 8 && revision == _revision; step++)
        {
            await Task.Delay(20);
            Opacity = targetOpacity * step / 8;
        }
    }

    private async Task HideAfterDelayAsync(int revision, int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 60)));
        if (revision == _revision)
            Hide();
    }
}
