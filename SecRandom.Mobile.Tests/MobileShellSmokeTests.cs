using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
        Styles.Add(new FluentTheme());
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
        var destinationButtons = controls.OfType<ToggleButton>().ToList();
        Assert.Equal(4, destinationButtons.Count);
        Assert.Single(destinationButtons, button => button.IsChecked == true);
        Assert.DoesNotContain(controls, control =>
            string.Equals(control.GetType().Assembly.GetName().Name, "FluentAvalonia", StringComparison.Ordinal));

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
}
