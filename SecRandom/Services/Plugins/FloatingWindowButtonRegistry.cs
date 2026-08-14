using System;
using System.Collections.Generic;
using System.Linq;
using SecRandom.Core.Abstraction.Services;

namespace SecRandom.Services.Plugins;

/// <summary>
///     Runtime registry for plugin-contributed floating-window buttons. The registry is populated during
///     plugin initialization and never persisted; the floating window renders registered buttons and
///     prunes settings entries whose registration no longer exists.
/// </summary>
public sealed class FloatingWindowButtonRegistry : IFloatingWindowButtonRegistry
{
    private readonly List<FloatingWindowButtonDescriptor> _buttons = [];
    private readonly object _gate = new();

    public IReadOnlyList<FloatingWindowButtonDescriptor> Buttons
    {
        get
        {
            lock (_gate)
                return _buttons.ToArray();
        }
    }

    public event EventHandler? Changed;

    public void Register(FloatingWindowButtonDescriptor button)
    {
        ArgumentNullException.ThrowIfNull(button);
        if (string.IsNullOrWhiteSpace(button.Id))
            throw new ArgumentException("A floating-window button id is required.", nameof(button));

        lock (_gate)
        {
            var existing = _buttons.FindIndex(candidate => string.Equals(
                candidate.Id,
                button.Id,
                StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                _buttons[existing] = button;
            else
                _buttons.Add(button);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Unregister(string id)
    {
        bool removed;
        lock (_gate)
            removed = _buttons.RemoveAll(candidate => string.Equals(
                candidate.Id,
                id,
                StringComparison.OrdinalIgnoreCase)) > 0;

        if (removed)
            Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }
}
