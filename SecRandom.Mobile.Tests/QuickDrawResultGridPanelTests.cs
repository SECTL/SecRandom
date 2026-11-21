using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SecRandom.Core.Controls;

namespace SecRandom.Mobile.Tests;

/// <summary>
///     浮窗结果网格在真实布局系统里的落位：4 人 2×2、6 人横向 3×2、窄高 2×3，
///     并且最后一行卡片少时整行水平居中。
/// </summary>
public sealed class QuickDrawResultGridPanelTests
{
    [AvaloniaFact]
    public void 四人排成两行两列()
    {
        var panel = BuildPanel(4);
        var window = ShowInWindow(panel, 1280, 720);

        var items = panel.Children.ToArray();
        Assert.Equal(2, FirstRowCount(items));
        Assert.Equal(2, RowCount(items));

        window.Close();
    }

    [AvaloniaFact]
    public void 六人在横向窗口排成两行三列()
    {
        var panel = BuildPanel(6);
        var window = ShowInWindow(panel, 1280, 720);

        var items = panel.Children.ToArray();
        Assert.Equal(3, FirstRowCount(items));
        Assert.Equal(2, RowCount(items));

        window.Close();
    }

    [AvaloniaFact]
    public void 六人在窄高窗口排成三行两列()
    {
        var panel = BuildPanel(6);
        var window = ShowInWindow(panel, 800, 1200);

        var items = panel.Children.ToArray();
        Assert.Equal(2, FirstRowCount(items));
        Assert.Equal(3, RowCount(items));

        window.Close();
    }

    [AvaloniaFact]
    public void 最后一行不足时水平居中()
    {
        var panel = BuildPanel(3);
        var window = ShowInWindow(panel, 1280, 720);

        var items = panel.Children.ToArray();
        Assert.Equal(2, FirstRowCount(items));
        var last = items.OrderBy(item => item.Bounds.Y).Last();
        Assert.InRange(last.Bounds.Center.X, (1280.0 / 2) - 0.5, (1280.0 / 2) + 0.5);

        window.Close();
    }

    [AvaloniaFact]
    public void 窗口随内容伸缩时仍按最大宽度选择列数()
    {
        var panel = BuildPanel(6);
        var window = new Window
        {
            Width = 280,
            MinWidth = 280,
            MinHeight = 160,
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 1280,
            MaxHeight = 720,
            Content = panel
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, FirstRowCount(panel.Children.ToArray()));
        Assert.True(window.Bounds.Width > 280);

        window.Close();
    }

    private static QuickDrawResultGridPanel BuildPanel(int count)
    {
        var panel = new QuickDrawResultGridPanel();
        for (var index = 0; index < count; index++)
        {
            panel.Children.Add(new Border
            {
                Width = 180,
                Height = 80,
                Margin = new Thickness(4)
            });
        }

        return panel;
    }

    private static Window ShowInWindow(QuickDrawResultGridPanel panel, double width, double height)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = panel
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static int RowCount(Control[] items)
    {
        return items.Select(item => Math.Round(item.Bounds.Y, 1)).Distinct().Count();
    }

    private static int FirstRowCount(Control[] items)
    {
        var firstY = items.Min(item => item.Bounds.Y);
        return items.Count(item => Math.Abs(item.Bounds.Y - firstY) < 0.5);
    }
}