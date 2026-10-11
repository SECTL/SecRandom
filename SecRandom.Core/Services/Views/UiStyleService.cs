using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services.Views;

namespace SecRandom.Core.Services.Views;

/// <summary>
///     Default <see cref="IUiStyleService" />. Applies plugin style packs to the running
///     <see cref="Application" />: styles are appended to <c>Application.Styles</c> and resource overrides are
///     written into <c>Application.Resources</c>, where they win over host defaults for every
///     <c>DynamicResource</c> consumer. Everything applied is tracked so it can be undone on removal.
/// </summary>
public sealed class UiStyleService : IUiStyleService
{
    private readonly List<string> _appliedResourceKeys = [];
    private readonly List<IStyle> _appliedStyles = [];
    private readonly List<IUiStyleContribution> _contributions = [];
    private readonly object _gate = new();
    private readonly ILogger<UiStyleService>? _logger;
    private bool _hookedToTheme;

    public UiStyleService(IEnumerable<IUiStyleContribution>? contributions = null,
        ILogger<UiStyleService>? logger = null)
    {
        _logger = logger;

        if (contributions is null)
            return;

        foreach (var contribution in contributions)
        {
            _contributions.RemoveAll(existing => existing.Id == contribution.Id);
            _contributions.Add(contribution);
        }
    }

    public ThemeVariant CurrentVariant => ResolveApplication()?.ActualThemeVariant ?? ThemeVariant.Default;

    public IReadOnlyList<IUiStyleContribution> Contributions
    {
        get
        {
            lock (_gate)
            {
                return _contributions.OrderBy(contribution => contribution.Priority).ToArray();
            }
        }
    }

    public event EventHandler? Changed;

    public void Add(IUiStyleContribution contribution)
    {
        lock (_gate)
        {
            _contributions.RemoveAll(existing => existing.Id == contribution.Id);
            _contributions.Add(contribution);
        }

        Refresh();
    }

    public bool Remove(string id)
    {
        var removed = false;
        lock (_gate)
        {
            removed = _contributions.RemoveAll(contribution => contribution.Id == id) > 0;
        }

        if (removed)
            Refresh();

        return removed;
    }

    public void Refresh()
    {
        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private void Apply()
    {
        var application = ResolveApplication();
        if (application is null)
            return;

        HookThemeChanges(application);

        try
        {
            foreach (var style in _appliedStyles)
                application.Styles.Remove(style);
            _appliedStyles.Clear();

            foreach (var key in _appliedResourceKeys)
                application.Resources.Remove(key);
            _appliedResourceKeys.Clear();

            var variant = application.ActualThemeVariant;
            foreach (var contribution in Contributions)
            {
                if (!AppliesTo(contribution, variant))
                    continue;

                foreach (var style in contribution.Styles ?? [])
                {
                    application.Styles.Add(style);
                    _appliedStyles.Add(style);
                }

                if (contribution.Resources is not { Count: > 0 } resources)
                    continue;

                foreach (var pair in resources)
                {
                    application.Resources[pair.Key] = pair.Value;
                    _appliedResourceKeys.Add(pair.Key);
                }
            }
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "应用插件界面样式失败。");
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void HookThemeChanges(Application application)
    {
        if (_hookedToTheme)
            return;

        _hookedToTheme = true;
        application.ActualThemeVariantChanged += (_, _) => Refresh();
    }

    private static bool AppliesTo(IUiStyleContribution contribution, ThemeVariant variant)
    {
        try
        {
            return contribution.AppliesTo(variant);
        }
        catch
        {
            return false;
        }
    }

    private static Application? ResolveApplication()
    {
        if (Application.Current is { } application)
            return application;

        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is
            { } window
            ? Application.Current
            : null;
    }
}
