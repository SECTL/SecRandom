using SecRandom.Core.Views;
using SecRandom.Views;
using SecRandom.Views.Mobile;

namespace SecRandom.Services.Mobile;

/// <summary>
/// Coordinates the independent mobile SettingsView without exposing the MVE host to child pages.
/// </summary>
public interface IMobileSettingsNavigator
{
    bool IsOpen { get; }

    Task OpenAsync(string? pageId = null);

    Task NavigateAsync(string pageId);

    Task GoBackAsync();

    Task CloseAsync();

    void Attach(SettingsView view);

    void Detach(SettingsView view);
}

internal sealed class MobileSettingsNavigator(IViewEngine viewEngine) : IMobileSettingsNavigator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SettingsView? _view;

    public bool IsOpen => _view is { IsClosed: false };

    public void Attach(SettingsView view)
    {
        _view = view;
    }

    public void Detach(SettingsView view)
    {
        if (ReferenceEquals(_view, view))
            _view = null;
    }

    public async Task OpenAsync(string? pageId = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await viewEngine.ShowAsync(MobilePageIds.Settings).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pageId))
                return;

            // ViewEngine creates views on the UI dispatcher, but Attach runs before ShowAsync
            // completes. Keep the coordinator serialized until that live view can receive its route.
            if (_view is not { IsClosed: false } view)
                throw new InvalidOperationException("The mobile settings view did not attach to the view engine.");

            await view.NavigateMobilePageAsync(pageId).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task NavigateAsync(string pageId) => OpenAsync(pageId);

    public Task GoBackAsync() => _view is { IsClosed: false } view
        ? view.NavigateMobileBackAsync()
        : Task.CompletedTask;

    public async Task CloseAsync() => await viewEngine.CloseAsync(MobilePageIds.Settings, ViewCloseReason.User);
}
