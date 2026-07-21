using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Mobile;

namespace SecRandom.Views.Mobile;

/// <summary>
/// Stable mobile navigation keys. Platform composition chooses which keys have a keyed control registration.
/// These keys do not select alternate view types at runtime.
/// </summary>
public static class MobilePageIds
{
    public const string Draw = "main.rollCall";
    public const string History = "main.history";
    public const string Overview = "main.overview";
    public const string Settings = "settings.mobile";
    public const string General = "settings.mobile.general";
    public const string Personalization = "settings.mobile.personalization";
    public const string ListManagement = "settings.mobile.listManagement";
    public const string DrawSettings = "settings.mobile.draw";
    public const string Backup = "settings.mobile.backup";
    public const string Update = "settings.mobile.update";
    public const string About = "settings.mobile.about";
}

public enum MobileDestination
{
    Draw,
    History,
    Overview,
    Settings
}

/// <summary>
/// Read-only mobile capability projection selected by platform DI at startup.
/// </summary>
public interface IMobileCapabilities
{
    bool IsLotteryEnabled { get; }
    bool SupportsInAppUpdate { get; }
}

internal sealed class MobileCapabilities(
    IFeatureAvailabilityService featureAvailability,
    IMobileUpdateInstaller updateInstaller) : IMobileCapabilities
{
    public bool IsLotteryEnabled => featureAvailability.IsLotteryEnabled;
    public bool SupportsInAppUpdate => updateInstaller.IsSupported;
}

/// <summary>
/// Mobile-shell navigation boundary for the three persistent root pages. Settings uses the independent MVE view.
/// </summary>
public interface IMobileNavigator
{
    MobileDestination CurrentDestination { get; }
    event EventHandler? DestinationChanged;

    void Attach(ContentControl outlet);
    void Detach(ContentControl outlet);
    Task<bool> NavigateRootAsync(MobileDestination destination);
    Task<bool> GoBackAsync();
    Task ResetToDrawAsync();
}

internal sealed class MobileNavigator(IServiceProvider services) : IMobileNavigator
{
    private readonly List<MobilePageEntry> _history = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ContentControl? _outlet;
    private MobileDestination _destination = MobileDestination.Draw;

    public MobileDestination CurrentDestination => _destination;
    public event EventHandler? DestinationChanged;

    public void Attach(ContentControl outlet)
    {
        ArgumentNullException.ThrowIfNull(outlet);
        if (_outlet is not null && !ReferenceEquals(_outlet, outlet))
            throw new InvalidOperationException("A mobile page outlet is already attached.");

        _outlet = outlet;
    }

    public void Detach(ContentControl outlet)
    {
        ArgumentNullException.ThrowIfNull(outlet);
        if (!ReferenceEquals(_outlet, outlet))
            return;

        outlet.Content = null;
        _history.Clear();
        _outlet = null;
    }

    public Task ResetToDrawAsync() => NavigateRootAsync(MobileDestination.Draw);

    public async Task<bool> NavigateRootAsync(MobileDestination destination)
    {
        if (destination == MobileDestination.Settings)
            return false;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await RunOnUiThreadAsync(() =>
            {
                var pageId = GetRootPageId(destination);
                var page = CreatePage(pageId);
                if (page is null || _outlet is null)
                    return false;

                _history.Clear();
                _history.Add(new MobilePageEntry(pageId, page));
                _outlet.Content = page;
                var destinationChanged = _destination != destination;
                _destination = destination;
                if (destinationChanged)
                    DestinationChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> GoBackAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await RunOnUiThreadAsync(() =>
            {
                if (_outlet is null || _history.Count <= 1)
                    return false;

                _history.RemoveAt(_history.Count - 1);
                _outlet.Content = _history[^1].Page;
                return true;
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private UserControl? CreatePage(string pageId) => services.GetKeyedService<UserControl>(pageId);

    private static bool TryGetDestination(string pageId, out MobileDestination destination)
    {
        destination = pageId switch
        {
            MobilePageIds.Draw => MobileDestination.Draw,
            MobilePageIds.History => MobileDestination.History,
            MobilePageIds.Overview => MobileDestination.Overview,
            _ => default
        };
        return pageId is MobilePageIds.Draw or MobilePageIds.History or MobilePageIds.Overview;
    }

    private static string GetRootPageId(MobileDestination destination) => destination switch
    {
        MobileDestination.Draw => MobilePageIds.Draw,
        MobileDestination.History => MobilePageIds.History,
        MobileDestination.Overview => MobilePageIds.Overview,
        _ => throw new ArgumentOutOfRangeException(nameof(destination))
    };

    private static Task<T> RunOnUiThreadAsync<T>(Func<T> action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
            return Task.FromResult(action());

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    private sealed record MobilePageEntry(string PageId, UserControl Page);
}

internal enum DrawSurface
{
    RollCall,
    Lottery
}
