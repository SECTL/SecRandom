using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SecRandom.Core.Controls;

/// <summary>
/// Tracks whether a top-level was most recently interacted with by touch so text controls can
/// use touch-sized command affordances without changing mouse and keyboard workflows.
/// </summary>
public sealed class TouchInputModeAssist
{
    public static readonly AttachedProperty<bool> IsTouchModeProperty =
        AvaloniaProperty.RegisterAttached<TouchInputModeAssist, Control, bool>("IsTouchMode", inherits: true);

    private static bool _initialized;

    public static bool GetIsTouchMode(Control control) => control.GetValue(IsTouchModeProperty);

    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((topLevel, args) =>
        {
            topLevel.SetValue(IsTouchModeProperty, args.Pointer.Type == PointerType.Touch);
        }, handledEventsToo: true);
    }
}
