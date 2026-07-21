using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SecRandom.Controls.Mobile;
using SecRandom.Views.Mobile;

namespace SecRandom.Mobile.Tests;

public sealed class MobileNavigationBarTests
{
    [AvaloniaFact]
    public void TappingTheSelectedDestinationStillRaisesNavigation()
    {
        var navigation = new MobileNavigationBar();
        var window = new Avalonia.Controls.Window { Content = navigation };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var selected = new List<MobileDestination>();
        navigation.DestinationSelected += (_, destination) => selected.Add(destination);

        var draw = Assert.Single(navigation.GetVisualDescendants().OfType<ToggleButton>(),
            button => Equals(button.Tag, MobileDestination.Draw));
        draw.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(ToggleButton.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([MobileDestination.Draw], selected);
        window.Close();
    }
}
