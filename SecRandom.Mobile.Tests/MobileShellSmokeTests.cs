using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using SecRandom.Mobile.Controls;
using SecRandom.Mobile.Views;

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
        Styles.Add(new FluentAvaloniaTheme
        {
            PreferSystemTheme = true,
            UseSystemFontOnWindows = true
        });
        Styles.Add(new StyleInclude(new Uri("avares://SecRandom.Mobile/Styles/MobileStyles.axaml"))
        {
            Source = new Uri("avares://SecRandom.Mobile/Styles/MobileStyles.axaml")
        });
    }
}

public sealed class MobileShellSmokeTests
{
    [AvaloniaFact]
    public void NativeShellControlsLoadStylesAndLayoutAtPhoneSize()
    {
        var navigation = new MobileNavigationBar();
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

        var app = Assert.IsType<MobileTestApplication>(Application.Current);
        Assert.True(app.TryGetResource("MobileCanvasBrush", ThemeVariant.Light, out _));
        Assert.True(card.Bounds.Width > 0);
        Assert.True(navigation.Bounds.Height >= 56);

        var controls = root.GetVisualDescendants().OfType<Control>().ToList();
        var navigationView = Assert.Single(controls.OfType<FANavigationView>());
        var destinationItems = controls.OfType<FANavigationViewItem>().ToList();
        Assert.Equal(4, navigationView.MenuItems.Count);
        Assert.NotEmpty(destinationItems);
        Assert.NotNull(navigationView.SelectedItem);

        app.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        Assert.True(app.TryGetResource("MobileCanvasBrush", ThemeVariant.Dark, out _));
        Assert.True(navigation.Bounds.Height >= 56);

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
        var scroll = MobileUi.CreateScroll(items);
        var window = new Window
        {
            Width = 390,
            Height = 844,
            Content = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                Children =
                {
                    new MobilePageHeader { Title = "Settings" },
                    scroll,
                    new MobileNavigationBar()
                }
            }
        };
        Grid.SetRow(scroll, 1);
        Grid.SetRow(((Grid)window.Content).Children[2], 2);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        scroll.Offset = new Vector(0, 240);
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.Offset.Y > 0);

        window.Close();
    }
}
