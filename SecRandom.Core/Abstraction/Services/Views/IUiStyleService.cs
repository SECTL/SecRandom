using Avalonia.Styling;

namespace SecRandom.Core.Abstraction.Services.Views;

/// <summary>
///     A plugin provided style pack: Avalonia styles to add to the host application and/or resource overrides
///     for the host's <c>DynamicResource</c> keys. Register it during <c>PluginBase.Initialize</c>:
///     <code>services.AddSingleton&lt;IUiStyleContribution, MyThemeContribution&gt;();</code>
/// </summary>
/// <remarks>
///     Styles are appended to <c>Application.Styles</c> in priority order, so a contribution is a normal
///     Avalonia style source and can retheme anything the host reaches through a style selector. Resources are
///     written into <c>Application.Resources</c> and therefore override host defaults for every consumer of
///     that key - this is the supported way to recolour the shell. Everything a contribution adds is removed
///     again on <see cref="IUiStyleService.Remove" />.
/// </remarks>
public interface IUiStyleContribution
{
    /// <summary>Stable unique id.</summary>
    string Id { get; }

    /// <summary>Human readable name, for diagnostics and plugin UI.</summary>
    string DisplayName { get; }

    /// <summary>Higher priority contributions are applied last and therefore win.</summary>
    int Priority => 0;

    /// <summary>Resource overrides keyed by host resource key. May be empty.</summary>
    IReadOnlyDictionary<string, object?> Resources { get; }

    /// <summary>Avalonia styles to add. May be empty.</summary>
    IReadOnlyList<IStyle> Styles { get; }

    /// <summary>Whether this contribution applies to the current theme variant.</summary>
    bool AppliesTo(ThemeVariant variant) => true;
}

/// <summary>
///     Host theming capability exposed to plugins: apply style packs and read or watch the active theme
///     variant, so a plugin can let its user restyle the host UI and adapt its own visuals to light/dark.
///     Resolvable through <c>IAppHost.TryGetService&lt;IUiStyleService&gt;()</c>.
/// </summary>
public interface IUiStyleService
{
    /// <summary>The host's current theme variant (<c>Light</c>, <c>Dark</c> or <c>Default</c>).</summary>
    ThemeVariant CurrentVariant { get; }

    /// <summary>Registered style packs, highest priority last.</summary>
    IReadOnlyList<IUiStyleContribution> Contributions { get; }

    /// <summary>Raised after style packs are re-applied (addition, removal or theme change).</summary>
    event EventHandler? Changed;

    /// <summary>Apply a style pack, replacing any existing one with the same id.</summary>
    void Add(IUiStyleContribution contribution);

    /// <summary>Remove a style pack and undo everything it added.</summary>
    bool Remove(string id);

    /// <summary>Re-apply all style packs. The host calls this on theme changes.</summary>
    void Refresh();
}
