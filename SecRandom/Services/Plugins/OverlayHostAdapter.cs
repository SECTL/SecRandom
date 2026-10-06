using Avalonia.Threading;
using SecRandom.Core.Abstraction.Services.Views;
using SecRandom.Core.Controls;
using SecRandom.Views;

namespace SecRandom.Services.Plugins;

/// <summary>
///     Desktop implementation of <see cref="IOverlayHostService" />. Forwards plugin requests to the overlay
///     layer of the running <see cref="MainView" />, following the same delegate-to-the-shell pattern as
///     <see cref="MainViewAdapter" />.
/// </summary>
public sealed class OverlayHostAdapter : IOverlayHostService
{
    private OverlayHost? _hookedLayer;

    public bool IsSupported => ResolveLayer() is not null;

    public IReadOnlyList<string> OpenOverlayIds => ResolveLayer()?.OpenOverlayIds ?? [];

    public event EventHandler<OverlayClosedEventArgs>? OverlayClosed;

    public string Show(OverlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var id = string.IsNullOrWhiteSpace(options.Id) ? Guid.NewGuid().ToString("N") : options.Id!;
        var normalized = options with { Id = id };
        var layer = ResolveLayer();

        if (layer is null)
        {
            // Nothing to show it in (mobile shell, headless, shutdown). Report the overlay as closed right
            // away so a plugin is not left waiting for a dismissal that can never happen.
            OverlayClosed?.Invoke(this, new OverlayClosedEventArgs(id, false));
            return id;
        }

        Hook(layer);
        RunOnUiThread(() => layer.Show(normalized));
        return id;
    }

    public void Close(string id)
    {
        if (ResolveLayer() is { } layer)
            RunOnUiThread(() => layer.Close(id));
    }

    public void CloseAll()
    {
        if (ResolveLayer() is { } layer)
            RunOnUiThread(layer.CloseAll);
    }

    private void Hook(OverlayHost layer)
    {
        if (ReferenceEquals(_hookedLayer, layer))
            return;

        if (_hookedLayer is not null)
            _hookedLayer.OverlayClosed -= OnOverlayClosed;

        _hookedLayer = layer;
        layer.OverlayClosed += OnOverlayClosed;
    }

    private void OnOverlayClosed(object? sender, OverlayClosedEventArgs e)
    {
        OverlayClosed?.Invoke(this, e);
    }

    private static OverlayHost? ResolveLayer()
    {
        return MainView.Current?.OverlayLayer;
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
