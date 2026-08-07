using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Ursa.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Models;
using SecRandom.Services.Security;
using SecRandom.ViewModels;
using SR = SecRandom.Langs.SettingsPages.Security.Resources;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.security", FluentIcons.ShieldKeyholeFilled, "settings.general")]
public partial class SecuritySettingsPage : UserControl, INotifyPropertyChanged
{
    private readonly ISecurityService _securityService = IAppHost.GetService<ISecurityService>();
    private bool _refreshing;
    private bool _isSettingsSubscribed;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public SecuritySettingsPage()
    {
        Settings = ViewModel.Config.SecuritySettings;
        FactorOptions =
        [
            new(SR.S_Password, () => Settings.PasswordEnabled, value => Settings.PasswordEnabled = value),
            new(SR.S_Totp, () => Settings.TotpEnabled, value => Settings.TotpEnabled = value),
            new(SR.S_Usb, () => Settings.UsbBindingEnabled, value => Settings.UsbBindingEnabled = value)
        ];
        SelectedFactorOptions =
            new AvaloniaList<MultiSelectSettingOption>(FactorOptions.Where(option => option.IsSelected));
        SelectedFactorOptions.CollectionChanged += SelectedFactorOptionsOnCollectionChanged;
        DataContext = this;
        InitializeComponent();
        SubscribeSettings();
        RefreshSecurityState();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public SecuritySettingsConfig Settings { get; }
    public AvaloniaList<MultiSelectSettingOption> FactorOptions { get; }
    public AvaloniaList<MultiSelectSettingOption> SelectedFactorOptions { get; }
    public bool CanEnableSecurity { get; private set; }
    public bool IsSecurityEnabled { get; private set; }
    public bool HasPassword { get; private set; }
    public bool CanSetPassword => !HasPassword;
    public bool CanConfigureAdditionalFactors { get; private set; }
    public bool CanEditFactorSelection { get; private set; }
    public bool CanEditProtectedOperations { get; private set; }
    public string TotpButtonText { get; private set; } = SR.C_SetTotp;
    public bool IsLockedOut { get; private set; }
    public string LockoutText { get; private set; } = string.Empty;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SubscribeSettings();
        RefreshSecurityState();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (!_isSettingsSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        _isSettingsSubscribed = false;
    }

    private async void SelectedFactorOptionsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_refreshing)
            return;

        var state = _securityService.GetUiState();
        var requestedOptions = FactorOptions
            .Where(option => SelectedFactorOptions.Contains(option) ||
                             (ReferenceEquals(option, FactorOptions[0]) && state.HasPassword))
            .ToArray();
        if (FactorOptions.All(option => option.IsSelected == requestedOptions.Contains(option)))
        {
            RefreshSecurityState();
            return;
        }

        if (TopLevel.GetTopLevel(this) is not { } xamlRoot)
        {
            RefreshSecurityState();
            return;
        }

        await ApplySecuritySettingsUpdateAsync(
            xamlRoot,
            () =>
            {
                foreach (var option in FactorOptions)
                    option.SetSelected(requestedOptions.Contains(option));
            },
            SynchronizeSelectedFactorOptions);
    }

    private void SubscribeSettings()
    {
        if (_isSettingsSubscribed)
            return;

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        _isSettingsSubscribed = true;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_refreshing) return;
        ConfigHandler.Save();
        RefreshSecurityState();
    }

    private void RefreshSecurityState()
    {
        _refreshing = true;
        try
        {
            var state = _securityService.GetUiState();
            CanEnableSecurity = state.HasPassword;
            IsSecurityEnabled = state.SecurityEnabled;
            HasPassword = state.HasPassword;
            CanConfigureAdditionalFactors = state.CanConfigureAdditionalFactors;
            CanEditFactorSelection = state.CanEditFactorSelection;
            CanEditProtectedOperations = state.CanEditProtectedOperations;
            TotpButtonText = state.HasTotp ? SR.C_ResetTotp : SR.C_SetTotp;
            IsLockedOut = state.LockoutRemaining is not null;
            LockoutText = state.LockoutRemaining is { } remaining
                ? string.Format(SR.M_LockoutFormat, Math.Ceiling(remaining.TotalSeconds))
                : string.Empty;
            SynchronizeSelectedFactorOptions();
        }
        finally
        {
            _refreshing = false;
            foreach (var name in new[]
                     {
                          nameof(CanEnableSecurity), nameof(IsSecurityEnabled), nameof(HasPassword), nameof(CanSetPassword),
                          nameof(CanConfigureAdditionalFactors), nameof(CanEditFactorSelection), nameof(CanEditProtectedOperations),
                          nameof(TotpButtonText), nameof(IsLockedOut), nameof(LockoutText)
                     })
                NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private async void SecurityEnabled_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_refreshing || sender is not ToggleSwitch toggle || toggle.IsChecked is not { } requested ||
            requested == Settings.SecurityEnabled)
            return;

        if (TopLevel.GetTopLevel(this) is not { } xamlRoot)
        {
            RefreshSecurityState();
            return;
        }

        await ApplySecuritySettingsUpdateAsync(
            xamlRoot,
            () => Settings.SecurityEnabled = requested,
            () => toggle.IsChecked = Settings.SecurityEnabled);
    }

    private async void SecurityOption_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_refreshing || sender is not ToggleButton toggle || toggle.Tag is not string optionName ||
            toggle.IsChecked is not { } requested ||
            !TryGetSecurityOption(optionName, out var getValue, out var setValue))
            return;

        var current = getValue();
        if (requested == current)
            return;

        if (TopLevel.GetTopLevel(this) is not { } xamlRoot)
        {
            RefreshSecurityState();
            return;
        }

        await ApplySecuritySettingsUpdateAsync(
            xamlRoot,
            () => setValue(requested),
            () => toggle.IsChecked = current);
    }

    private async void SetPassword_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } xamlRoot) return;
        var result = await SecuritySetupDialogs.ShowPasswordEditorAsync(xamlRoot, hasPassword: false);
        if (result is null) return;
        await SavePasswordAsync(result);
    }

    private async void ChangePassword_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } xamlRoot) return;
        var result = await SecuritySetupDialogs.ShowPasswordEditorAsync(xamlRoot, hasPassword: true);
        if (result is null) return;
        await SavePasswordAsync(result);
    }

    private async void RemovePassword_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } xamlRoot) return;
        var currentPassword = await SecuritySetupDialogs.ShowPasswordRemovalAsync(xamlRoot);
        if (currentPassword is null) return;

        _refreshing = true;
        var removed = await _securityService.RemovePasswordAsync(currentPassword);
        _refreshing = false;
        if (removed) this.ShowSuccessToast(SR.M_PasswordRemoved);
        else this.ShowErrorToast(SR.M_PasswordSaveFailed);
        RefreshSecurityState();
    }

    private async Task SavePasswordAsync(PasswordEditorResult result)
    {
        _refreshing = true;
        var saved = await _securityService.SetPasswordAsync(result.NewPassword, result.CurrentPassword);
        _refreshing = false;
        if (saved) this.ShowSuccessToast(SR.M_PasswordSaved);
        else this.ShowErrorToast(SR.M_PasswordSaveFailed);
        RefreshSecurityState();
    }

    private async void ManageTotp_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } xamlRoot) return;
        var secret = await _securityService.BeginTotpSetupAsync(xamlRoot);
        if (secret is null)
        {
            if (!_securityService.GetUiState().HasPassword)
                this.ShowWarningToast(SR.M_SetPasswordFirst);
            return;
        }

        var code = await SecuritySetupDialogs.ShowTotpSetupAsync(xamlRoot, secret);
        if (code is not null && await _securityService.ConfirmTotpAsync(secret, code))
            this.ShowSuccessToast(SR.M_TotpSaved);
        else if (code is not null) this.ShowErrorToast(SR.M_TotpSaveFailed);
        else await _securityService.CancelTotpSetupAsync(secret);
        RefreshSecurityState();
    }

    private async void ManageUsb_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } xamlRoot) return;
        var result = await SecuritySetupDialogs.ShowUsbBindingAsync(xamlRoot,
            await _securityService.GetUsbDevicesAsync());
        if (result is null) return;
        var success = result.UnbindId is not null
            ? await _securityService.UnbindUsbAsync(xamlRoot, result.UnbindId)
            : await _securityService.BindUsbAsync(xamlRoot, result.DeviceId!);
        if (success) this.ShowSuccessToast(SR.M_UsbUpdated);
        else this.ShowErrorToast(SR.M_UsbUpdateFailed);
        RefreshSecurityState();
    }

    private async Task ApplySecuritySettingsUpdateAsync(TopLevel xamlRoot, Action update, Action restoreView)
    {
        _refreshing = true;
        try
        {
            restoreView();
            await _securityService.UpdateSecuritySettingsAsync(xamlRoot, update);
        }
        finally
        {
            _refreshing = false;
            RefreshSecurityState();
        }
    }

    private void SynchronizeSelectedFactorOptions()
    {
        var selectedOptions = FactorOptions.Where(option => option.IsSelected).ToArray();
        if (SelectedFactorOptions.SequenceEqual(selectedOptions))
            return;

        SelectedFactorOptions.Clear();
        foreach (var option in selectedOptions)
            SelectedFactorOptions.Add(option);
    }

    private bool TryGetSecurityOption(string optionName, out Func<bool> getValue, out Action<bool> setValue)
    {
        switch (optionName)
        {
            case nameof(SecuritySettingsConfig.RequireAllSelectedFactors):
                getValue = () => Settings.RequireAllSelectedFactors;
                setValue = value => Settings.RequireAllSelectedFactors = value;
                return true;
            case nameof(SecuritySettingsConfig.AllowSettingsPreview):
                getValue = () => Settings.AllowSettingsPreview;
                setValue = value => Settings.AllowSettingsPreview = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectOpenSettings):
                getValue = () => Settings.ProtectOpenSettings;
                setValue = value => Settings.ProtectOpenSettings = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectToggleMainWindow):
                getValue = () => Settings.ProtectToggleMainWindow;
                setValue = value => Settings.ProtectToggleMainWindow = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectToggleFloatingWindow):
                getValue = () => Settings.ProtectToggleFloatingWindow;
                setValue = value => Settings.ProtectToggleFloatingWindow = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectRestart):
                getValue = () => Settings.ProtectRestart;
                setValue = value => Settings.ProtectRestart = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectExit):
                getValue = () => Settings.ProtectExit;
                setValue = value => Settings.ProtectExit = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectRollCallStart):
                getValue = () => Settings.ProtectRollCallStart;
                setValue = value => Settings.ProtectRollCallStart = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectRollCallReset):
                getValue = () => Settings.ProtectRollCallReset;
                setValue = value => Settings.ProtectRollCallReset = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectQuickDrawStart):
                getValue = () => Settings.ProtectQuickDrawStart;
                setValue = value => Settings.ProtectQuickDrawStart = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectQuickDrawReset):
                getValue = () => Settings.ProtectQuickDrawReset;
                setValue = value => Settings.ProtectQuickDrawReset = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectLotteryStart):
                getValue = () => Settings.ProtectLotteryStart;
                setValue = value => Settings.ProtectLotteryStart = value;
                return true;
            case nameof(SecuritySettingsConfig.ProtectLotteryReset):
                getValue = () => Settings.ProtectLotteryReset;
                setValue = value => Settings.ProtectLotteryReset = value;
                return true;
            default:
                getValue = null!;
                setValue = null!;
                return false;
        }
    }
}
