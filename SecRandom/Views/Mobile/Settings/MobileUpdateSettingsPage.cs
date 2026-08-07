using Avalonia.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Mobile;
using SecRandom.Services.Mobile;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.Views.Mobile.Settings;

/// <summary>
/// Native update availability is decided by the platform installer. The stable surface
/// stays in AXAML while update service state is synchronized after each operation.
/// </summary>
[PageInfo(MobilePageIds.Update, FluentIcons.ArrowSyncFilled, location: PageLocation.Bottom)]
public sealed partial class MobileUpdateSettingsPage : MobileSettingsPageBase
{
    private readonly MobileUpdateService _updateService;
    private readonly bool _installerSupported;

    public MobileUpdateSettingsPage(
        MobileUpdateService updateService,
        IMobileUpdateInstaller updateInstaller,
        IMobileCapabilities capabilities)
        : base(capabilities)
    {
        _updateService = updateService;
        _installerSupported = updateInstaller.IsSupported;
        InitializeComponent();
        RefreshSurface();
    }

    private void RefreshSurface()
    {
        SupportedUpdateSurface.IsVisible = _installerSupported;
        UnsupportedUpdateSurface.IsVisible = !_installerSupported;
        if (!_installerSupported)
            return;

        UpdateStatusText.Text = string.IsNullOrEmpty(_updateService.Status)
            ? LR.M_UpdateSecurityNote
            : _updateService.Status;
        CheckUpdatesButton.IsEnabled = !_updateService.IsBusy;
        InstallUpdateButton.IsEnabled = _updateService.IsUpdateAvailable && !_updateService.IsBusy;
    }

    private async void CheckUpdatesButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await _updateService.CheckAsync();
        RefreshSurface();
    }

    private async void InstallUpdateButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await _updateService.DownloadAndInstallAsync();
        RefreshSurface();
    }
}
