namespace SecRandom.Core.Abstraction.Services;

/// <summary>
///     Runtime registry for plugin-contributed floating-window buttons. Buttons are registered during
///     plugin initialization (before the Host is built) and rendered by the floating window alongside
///     the built-in roll-call/quick-draw/lottery buttons. Registration is runtime-only and is never
///     persisted to configuration; visible plugin buttons are selected through the settings UI and
///     pruned automatically when a registration disappears.
/// </summary>
public interface IFloatingWindowButtonRegistry
{
    IReadOnlyList<FloatingWindowButtonDescriptor> Buttons { get; }

    event EventHandler? Changed;

    void Register(FloatingWindowButtonDescriptor button);

    bool Unregister(string id);
}
