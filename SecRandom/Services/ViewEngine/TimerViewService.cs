using SecRandom.Core.Views;
using SecRandom.ViewModels.MainPages;
using SecRandom.Views.MainPages;

namespace SecRandom.Services.ViewEngine;

public sealed class TimerViewService(IViewEngine viewEngine, IViewHostProvider hostProvider, TimerViewModel viewModel) : IDisposable
{
    internal const string ViewId = "main.timer";
    private TimerMiniWindow? _miniWindow;

    public Task ShowAsync(CancellationToken cancellationToken = default) => viewEngine.ShowAsync(
        ViewId,
        hostProvider is DesktopViewHostProvider
            ? new ViewShowOptions { ActivationPreference = ViewActivationPreference.NewHost }
            : null,
        cancellationToken);

    public void ShowMiniWindow()
    {
        if (_miniWindow is { IsVisible: true })
        {
            _miniWindow.Activate();
            return;
        }

        _miniWindow = new TimerMiniWindow(viewModel, RestoreFullWindow);
        _miniWindow.Closed += (_, _) => _miniWindow = null;
        _miniWindow.Show();
    }

    public void RestoreFullWindow()
    {
        _miniWindow?.AllowClose();
        _miniWindow?.Close();
        _miniWindow = null;
        _ = ShowAsync();
    }

    public void Dispose()
    {
        _miniWindow?.AllowClose();
        _miniWindow?.Close();
        _miniWindow = null;
        viewModel.Dispose();
    }
}
