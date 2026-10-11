using Avalonia.Controls;

namespace SecRandom.Core.Abstraction.Services.Views;

/// <summary>
///     Content a plugin wants to show on top of the host shell (a modal panel, a banner, an animation layer).
///     The plugin owns the <see cref="Content" /> control; the host owns where it is placed, how it is layered
///     and when it is removed.
/// </summary>
public sealed record OverlayOptions
{
    /// <summary>Caller supplied id. A random id is generated when omitted.</summary>
    public string? Id { get; init; }

    /// <summary>The visual to show. It is added to the host's overlay layer.</summary>
    public required Control Content { get; init; }

    /// <summary>Draw a dimming backdrop behind the content.</summary>
    public bool ShowBackdrop { get; init; } = true;

    /// <summary>Backdrop brush, as <c>#AARRGGBB</c>. Ignored when <see cref="ShowBackdrop" /> is false.</summary>
    public string BackdropColor { get; init; } = "#66000000";

    /// <summary>Close the overlay when the user clicks the backdrop.</summary>
    public bool DismissOnClickOutside { get; init; } = true;

    /// <summary>Close the overlay when the user presses Esc.</summary>
    public bool DismissOnEscape { get; init; } = true;

    /// <summary>Stretch the content to the whole layer instead of centering it.</summary>
    public bool StretchContent { get; init; }

    /// <summary>Whether this overlay takes input away from the shell below it.</summary>
    public bool BlockInput { get; init; } = true;
}

/// <summary>Raised when an overlay is removed, either by the host, the plugin or the user.</summary>
public sealed class OverlayClosedEventArgs(string id, bool closedByUser) : EventArgs
{
    /// <summary>Id of the overlay that closed.</summary>
    public string Id { get; } = id;

    /// <summary><c>true</c> when the user dismissed it (backdrop click / Esc).</summary>
    public bool ClosedByUser { get; } = closedByUser;
}

/// <summary>
///     Host overlay capability exposed to plugins: a plugin can push its own visual on top of the main shell
///     without owning a window. Resolvable through
///     <c>IAppHost.TryGetService&lt;IOverlayHostService&gt;()</c>.
/// </summary>
/// <remarks>
///     Calls are marshalled to the UI thread, so they may be made from any thread. On shells that have no
///     overlay layer (mobile, headless) <see cref="IsSupported" /> is <c>false</c> and
///     <see cref="Show" /> returns the generated id without displaying anything; plugins that must work
///     everywhere should fall back to their own window when unsupported.
/// </remarks>
public interface IOverlayHostService
{
    /// <summary><c>true</c> when the current shell can host plugin overlays.</summary>
    bool IsSupported { get; }

    /// <summary>Ids of the overlays currently shown, oldest first.</summary>
    IReadOnlyList<string> OpenOverlayIds { get; }

    /// <summary>Raised whenever an overlay is closed.</summary>
    event EventHandler<OverlayClosedEventArgs>? OverlayClosed;

    /// <summary>Show <paramref name="options" /> and return its id.</summary>
    string Show(OverlayOptions options);

    /// <summary>Close one overlay. Unknown ids are ignored.</summary>
    void Close(string id);

    /// <summary>Close every plugin overlay.</summary>
    void CloseAll();
}
