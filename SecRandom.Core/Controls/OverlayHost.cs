using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using SecRandom.Core.Abstraction.Services.Views;

namespace SecRandom.Core.Controls;

/// <summary>
///     Hosts plugin overlays inside the shell window. Add a single instance to the shell's root visual tree;
///     the <see cref="IOverlayHostService" /> adapter forwards plugin requests to it.
/// </summary>
public class OverlayHost : Panel
{
    private readonly List<OverlayEntry> _entries = [];

    /// <summary>Raised whenever an overlay disappears, whether the user dismissed it or a plugin closed it.</summary>
    public event EventHandler<OverlayClosedEventArgs>? OverlayClosed;

    /// <summary>Ids of the overlays currently on screen, oldest first.</summary>
    public IReadOnlyList<string> OpenOverlayIds => _entries.Select(entry => entry.Id).ToArray();

    /// <summary>True when no overlay is on screen.</summary>
    public bool IsEmpty => _entries.Count == 0;

    /// <summary>Shows <paramref name="options" /> and returns the overlay id used to close it again.</summary>
    public string Show(OverlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var id = string.IsNullOrWhiteSpace(options.Id) ? Guid.NewGuid().ToString("N") : options.Id!;

        // Re-showing the same id replaces the previous overlay instead of stacking on top of it.
        Close(id, false);

        var layer = new Panel
        {
            Background = options.ShowBackdrop ? ToBrush(options.BackdropColor) : Brushes.Transparent,
            Focusable = options.BlockInput,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            ClipToBounds = true
        };

        var content = options.Content;
        content.HorizontalAlignment = options.StretchContent
            ? Avalonia.Layout.HorizontalAlignment.Stretch
            : Avalonia.Layout.HorizontalAlignment.Center;
        content.VerticalAlignment = options.StretchContent
            ? Avalonia.Layout.VerticalAlignment.Stretch
            : Avalonia.Layout.VerticalAlignment.Center;
        layer.Children.Add(content);

        if (options.DismissOnClickOutside)
            layer.PointerPressed += (_, args) =>
            {
                // Only a press on the backdrop itself counts; presses inside the content bubble up with a
                // different source.
                if (!ReferenceEquals(args.Source, layer))
                    return;

                Close(id, true);
                args.Handled = true;
            };

        EventHandler<KeyEventArgs>? escapeHandler = null;
        var topLevel = TopLevel.GetTopLevel(this);
        if (options.DismissOnEscape && topLevel is not null)
        {
            escapeHandler = (_, args) =>
            {
                if (args.Key != Key.Escape)
                    return;

                Close(id, true);
                args.Handled = true;
            };

            // Tunnelling from the top level catches Escape wherever the focus happens to be inside the overlay.
            topLevel.AddHandler(KeyDownEvent, escapeHandler, RoutingStrategies.Tunnel);
        }

        Children.Add(layer);
        _entries.Add(new OverlayEntry(id, layer, escapeHandler, topLevel));

        if (options.BlockInput)
            layer.Focus();

        return id;
    }

    /// <summary>Closes the overlay with the given id. Unknown ids are ignored.</summary>
    public void Close(string id)
    {
        Close(id, false);
    }

    /// <summary>Closes every overlay.</summary>
    public void CloseAll()
    {
        foreach (var entry in _entries.ToArray())
            Close(entry.Id, false);
    }

    private void Close(string id, bool closedByUser)
    {
        var index = _entries.FindIndex(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));
        if (index < 0)
            return;

        var entry = _entries[index];
        _entries.RemoveAt(index);

        if (entry.EscapeHandler is not null && entry.TopLevel is not null)
            entry.TopLevel.RemoveHandler(KeyDownEvent, entry.EscapeHandler);

        Children.Remove(entry.Layer);
        OverlayClosed?.Invoke(this, new OverlayClosedEventArgs(id, closedByUser));
    }

    private static IBrush ToBrush(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && Color.TryParse(value, out var color)
            ? new SolidColorBrush(color)
            : Brushes.Transparent;
    }

    private sealed record OverlayEntry(string Id, Panel Layer, EventHandler<KeyEventArgs>? EscapeHandler,
        TopLevel? TopLevel);
}
