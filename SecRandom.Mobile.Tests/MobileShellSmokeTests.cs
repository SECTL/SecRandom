using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using SecRandom.Controls.Mobile;
using SecRandom.Views.Mobile;

[assembly: AvaloniaTestApplication(typeof(SecRandom.Mobile.Tests.MobileTestAppBuilder))]

namespace SecRandom.Mobile.Tests;

public static class MobileTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<MobileTestApplication>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class MobileTestApplication : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Resources["PageContainerWidth"] = 960d;
        Styles.Add(new FluentAvaloniaTheme
        {
            PreferSystemTheme = true,
            UseSystemFontOnWindows = true
        });
    }
}

public sealed class MobileShellSmokeTests
{
    [AvaloniaFact]
    public void NativeShellControlsLayoutAtPhoneSize()
    {
        var navigation = new TabStrip
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Items =
            {
                new TabStripItem { Content = "Draw" },
                new TabStripItem { Content = "History" },
                new TabStripItem { Content = "Overview" },
                new TabStripItem { Content = "Settings" }
            }
        };
        var card = new MobileCard
        {
            Content = new TextBlock { Text = "SecRandom" }
        };
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { card, navigation }
        };
        Grid.SetRow(navigation, 1);

        var window = new Window
        {
            Width = 390,
            Height = 844,
            Content = root
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(card.Bounds.Width > 0);
        Assert.True(navigation.Bounds.Height >= 56);

        var destinationItems = navigation.GetVisualDescendants().OfType<TabStripItem>().ToList();
        Assert.Equal(4, destinationItems.Count);
        Assert.All(destinationItems, item => Assert.True(item.Bounds.Width > 0));
        Assert.Empty(root.GetVisualDescendants().OfType<FAItemsRepeater>());

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        Assert.True(navigation.Bounds.Height >= 56);

        window.Close();
    }

    [AvaloniaFact]
    public void TabStripSwitchesTabsAtPhoneSize()
    {
        var selectedIndex = -1;
        var tabs = new TabStrip
        {
            Items =
            {
                new TabStripItem { Content = "Roll call" },
                new TabStripItem { Content = "Lottery" }
            },
            SelectedIndex = 1
        };
        tabs.SelectionChanged += (_, _) => selectedIndex = tabs.SelectedIndex;
        var window = new Window
        {
            Width = 390,
            Height = 844,
            Content = tabs
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, tabs.SelectedIndex);

        var items = tabs.GetVisualDescendants().OfType<TabStripItem>().ToArray();
        Assert.Equal(2, items.Length);

        tabs.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, selectedIndex);

        window.Close();
    }

    [AvaloniaFact]
    public void MobileAnimationsRemainSafeWhenControlsAttach()
    {
        var animated = new Border
        {
            Child = new TextBlock { Text = "SecRandom" }
        };
        var window = new Window
        {
            Width = 390,
            Height = 844,
            Content = animated
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        MobileAnimations.PlayPageEnter(animated);
        MobileAnimations.PlayResultReveal(animated);
        Dispatcher.UIThread.RunJobs();

        Assert.True(animated.IsAttachedToVisualTree());
        window.Close();
    }

    [AvaloniaFact]
    public void MobilePageScrollHasFiniteViewportAndMovableOffset()
    {
        var items = Enumerable.Range(0, 24)
            .Select(index => (Control)new MobileCard
            {
                MinHeight = 72,
                Content = new TextBlock { Text = $"Item {index}" }
            });
        var scroll = new ScrollViewer
        {
            Content = new StackPanel { Spacing = 14, Children = { } },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsScrollChainingEnabled = false,
            IsScrollInertiaEnabled = true
        };
        var panel = Assert.IsType<StackPanel>(scroll.Content);
        foreach (var item in items)
            panel.Children.Add(item);
        var window = new Window
        {
            Width = 390,
            Height = 844,
            Content = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Children =
                {
                    scroll,
                    new TabStrip
                    {
                        Items =
                        {
                            new TabStripItem { Content = "Draw" },
                            new TabStripItem { Content = "History" }
                        }
                    }
                }
            }
        };
        Grid.SetRow(((Grid)window.Content).Children[1], 1);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        scroll.Offset = new Vector(0, 240);
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.Offset.Y > 0);

        window.Close();
    }

    [AvaloniaFact]
    public void MobileTableSupportsPhoneLayoutAndAutomationTraversal()
    {
        var rows = Enumerable.Range(0, 8)
            .Select(index => new TableRow($"S{index}", $"Student {index}", $"Group {index % 2}"))
            .ToArray();
        var table = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserResizeColumns = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ItemsSource = rows,
            Height = 520,
            RowHeight = 48
        };
        table.Columns.Add(new DataGridTextColumn
        {
            Header = "ID",
            Binding = new Avalonia.Data.Binding(nameof(TableRow.Id)),
            Width = new DataGridLength(120)
        });
        table.Columns.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Avalonia.Data.Binding(nameof(TableRow.Name)),
            Width = new DataGridLength(260)
        });
        table.Columns.Add(new DataGridTextColumn
        {
            Header = "Group",
            Binding = new Avalonia.Data.Binding(nameof(TableRow.Group)),
            Width = new DataGridLength(180)
        });
        var window = new Window { Width = 390, Height = 844, Content = table };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(table.Bounds.Width > 0);
        Assert.True(table.Columns.Sum(column => column.ActualWidth) > table.Bounds.Width);
        var rootPeer = ControlAutomationPeer.CreatePeerForElement(window);
        Assert.NotNull(rootPeer);
        TraverseAutomationTree(rootPeer, []);

        window.Close();
    }

    private static void TraverseAutomationTree(AutomationPeer peer, HashSet<AutomationPeer> visited)
    {
        if (!visited.Add(peer))
            return;

        foreach (var child in peer.GetChildren())
            TraverseAutomationTree(child, visited);
    }

    private sealed record TableRow(string Id, string Name, string Group);
}
