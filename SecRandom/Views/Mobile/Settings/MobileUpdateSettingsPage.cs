using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Mobile;
using SecRandom.Services.Mobile;
using SecRandom.Services.Updates;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.Views.Mobile.Settings;

/// <summary>
/// Shared update surface. Mobile hands verified APKs to the platform installer, while desktop
/// uses the signed-artifact update center for compatible package selection and installation.
/// </summary>
[PageInfo(MobilePageIds.Update, FluentIcons.ArrowSyncFilled, location: PageLocation.Bottom)]
public sealed partial class MobileUpdateSettingsPage : UserControl
{
    private readonly UpdateCenterService _desktopUpdateCenter;
    private readonly MobileUpdateService? _mobileUpdateService;
    private readonly bool _isMobile;
    private readonly bool _installerSupported;

    public MobileUpdateSettingsPage(
        UpdateCenterService desktopUpdateCenter,
        MobileUpdateService? mobileUpdateService = null,
        IMobileUpdateInstaller? updateInstaller = null)
    {
        _desktopUpdateCenter = desktopUpdateCenter;
        _mobileUpdateService = mobileUpdateService;
        _isMobile = updateInstaller is not null;
        _installerSupported = updateInstaller?.IsSupported ?? true;
        DataContext = this;
        InitializeComponent();
        RefreshSurface();
    }

    public UpdateCenterService DesktopUpdateCenter => _desktopUpdateCenter;

    private void RefreshSurface()
    {
        var useMobileInstaller = _isMobile && _installerSupported;
        SupportedUpdateSurface.IsVisible = !_isMobile || useMobileInstaller;
        UnsupportedUpdateSurface.IsVisible = _isMobile && !useMobileInstaller;
        if (_isMobile && !useMobileInstaller)
            return;

        if (useMobileInstaller)
        {
            UpdateStatusText.Text = string.IsNullOrEmpty(_mobileUpdateService!.Status)
                ? LR.M_UpdateSecurityNote
                : _mobileUpdateService.Status;
            CheckUpdatesButton.IsEnabled = !_mobileUpdateService.IsBusy;
            InstallUpdateButton.IsEnabled = _mobileUpdateService.IsUpdateAvailable && !_mobileUpdateService.IsBusy;
            DesktopArtifactSelector.IsVisible = false;
            return;
        }

        UpdateStatusText.Text = _desktopUpdateCenter.Status;
        CheckUpdatesButton.IsEnabled = _desktopUpdateCenter.CanCheck;
        InstallUpdateButton.IsEnabled = _desktopUpdateCenter.CanDownloadAndInstall || _desktopUpdateCenter.CanApplyUpdate;
        DesktopArtifactSelector.IsVisible = _desktopUpdateCenter.HasAvailableArtifacts;
    }

    private async void CheckUpdatesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isMobile)
            await _mobileUpdateService!.CheckAsync();
        else
            await _desktopUpdateCenter.CheckAsync();
        RefreshSurface();
    }

    private async void InstallUpdateButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isMobile)
        {
            await _mobileUpdateService!.DownloadAndInstallAsync();
        }
        else if (_desktopUpdateCenter.CanApplyUpdate)
        {
            await _desktopUpdateCenter.ApplyDownloadedUpdateAsync();
        }
        else
        {
            await _desktopUpdateCenter.DownloadAndInstallAsync();
        }
        RefreshSurface();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isMobile)
            _mobileUpdateService!.PropertyChanged += UpdateServiceOnPropertyChanged;
        else
            _desktopUpdateCenter.PropertyChanged += UpdateServiceOnPropertyChanged;
        RefreshSurface();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_isMobile)
            _mobileUpdateService!.PropertyChanged -= UpdateServiceOnPropertyChanged;
        else
            _desktopUpdateCenter.PropertyChanged -= UpdateServiceOnPropertyChanged;
    }

    private void UpdateServiceOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RefreshSurface);
}
