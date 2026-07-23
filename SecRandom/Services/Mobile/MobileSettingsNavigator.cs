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

    Task CloseAsync();
}

internal sealed class MobileSettingsNavigator(IViewEngine viewEngine) : IMobileSettingsNavigator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public bool IsOpen => SettingsView.Current is { IsClosed: false };

    public async Task OpenAsync(string? pageId = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await viewEngine.ShowAsync(MobilePageIds.Settings).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pageId))
                return;

            SettingsView.Current?.NavigateToPage(pageId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task NavigateAsync(string pageId)
    {
        if (SettingsView.Current is { IsClosed: false } settingsView)
        {
            settingsView.NavigateToPage(pageId);
            return Task.CompletedTask;
        }

        return OpenAsync(pageId);
    }

    public async Task CloseAsync() => await viewEngine.CloseAsync(MobilePageIds.Settings, ViewCloseReason.User);
}
