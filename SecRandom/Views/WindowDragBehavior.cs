using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Windowing;

namespace SecRandom.Views;

internal static class WindowDragBehavior
{
    public static void EnableExtendedTitleBarDrag(FAAppWindow window)
    {
        window.AddHandler(
            InputElement.PointerPressedEvent,
            (_, e) => TryBeginTitleBarDrag(window, e),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private static void TryBeginTitleBarDrag(FAAppWindow window, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed
            || e.GetPosition(window).Y >= window.TitleBar.Height
            || IsInteractive(e.Source))
            return;

        window.BeginMoveDrag(e);
        e.Handled = true;
    }

    private static bool IsInteractive(object? source)
    {
        for (var element = source as InputElement;
             element is not null;
             element = element.GetVisualParent() as InputElement)
        {
            if (element.Focusable)
                return true;
        }

        return false;
    }
}
