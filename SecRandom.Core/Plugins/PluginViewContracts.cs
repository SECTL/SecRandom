using SecRandom.Core.Views;

namespace SecRandom.Core.Plugins;

/// <summary>
/// Declares a plugin-owned logical view. The host controls its physical presentation.
/// </summary>
public sealed class PluginViewRegistration
{
    public required string PluginId { get; init; }
    public required string ViewId { get; init; }
    public required Type ViewType { get; init; }
    public ViewPresentation DefaultPresentation { get; init; } = ViewPresentation.Page;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PluginId))
            throw new ArgumentException("Plugin id is required.", nameof(PluginId));

        if (!ViewId.StartsWith($"plugin.{PluginId}.view.", StringComparison.Ordinal))
            throw new ArgumentException(
                $"Plugin view id must start with plugin.{PluginId}.view.",
                nameof(ViewId));

        if (!typeof(ViewBase).IsAssignableFrom(ViewType))
            throw new ArgumentException("Plugin view type must inherit ViewBase.", nameof(ViewType));

        if (ViewType.GetConstructor(Type.EmptyTypes) is null)
            throw new ArgumentException(
                "Plugin views must have a public parameterless constructor.",
                nameof(ViewType));
    }

}

/// <summary>
/// Restricts a plugin to displaying the logical views it owns.
/// </summary>
public interface IPluginViewService
{
    Task<IViewHandle> ShowAsync(
        string viewId,
        ViewShowOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<ViewCloseResult> CloseAsync(
        string viewId,
        ViewCloseReason reason = ViewCloseReason.Programmatic,
        object? result = null,
        CancellationToken cancellationToken = default);
}

public sealed class PluginViewService(string pluginId, IViewEngine viewEngine) : IPluginViewService
{
    private readonly string _prefix = BuildPrefix(pluginId);

    public Task<IViewHandle> ShowAsync(
        string viewId,
        ViewShowOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureOwned(viewId);
        if (!string.IsNullOrWhiteSpace(options?.HostId))
            throw new ArgumentException("Plugins may not target a specific host.", nameof(options));

        return viewEngine.ShowAsync(viewId, options, cancellationToken);
    }

    public Task<ViewCloseResult> CloseAsync(
        string viewId,
        ViewCloseReason reason = ViewCloseReason.Programmatic,
        object? result = null,
        CancellationToken cancellationToken = default)
    {
        EnsureOwned(viewId);
        return viewEngine.CloseAsync(viewId, reason, result, cancellationToken);
    }

    private static string BuildPrefix(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return $"plugin.{pluginId}.view.";
    }

    private void EnsureOwned(string viewId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewId);
        if (!viewId.StartsWith(_prefix, StringComparison.Ordinal))
            throw new ArgumentException("Plugins may only access their own registered views.", nameof(viewId));
    }
}
