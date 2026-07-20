using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
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
        SelectedFactorOptions = new AvaloniaList<MultiSelectSettingOption>(FactorOptions.Where(option => option.IsSelected));
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
    public bool CanConfigureAdditionalFactors { get; private set; }
    public bool CanEditFactorSelection { get; private set; }
    public bool CanEditProtectedOperations { get; private set; }
    public string PasswordButtonText { get; private set; } = SR.C_SetPassword;
    public string TotpButtonText { get; private set; } = SR.C_SetTotp;
    public bool IsLockedOut { get; private set; }
    public string LockoutText { get; private set; } = string.Empty;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged { add => NotifyPropertyChanged += value; remove => NotifyPropertyChanged -= value; }
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
            CanConfigureAdditionalFactors = state.CanConfigureAdditionalFactors;
            CanEditFactorSelection = state.CanEditFactorSelection;
            CanEditProtectedOperations = state.CanEditProtectedOperations;
            PasswordButtonText = state.HasPassword ? SR.C_ManagePassword : SR.C_SetPassword;
            TotpButtonText = state.HasTotp ? SR.C_ResetTotp : SR.C_SetTotp;
            IsLockedOut = state.LockoutRemaining is not null;
            LockoutText = state.LockoutRemaining is { } remaining ? string.Format(SR.M_LockoutFormat, Math.Ceiling(remaining.TotalSeconds)) : string.Empty;
        }
        finally
        {
            _refreshing = false;
            foreach (var name in new[] { nameof(CanEnableSecurity), nameof(CanConfigureAdditionalFactors), nameof(CanEditFactorSelection), nameof(CanEditProtectedOperations), nameof(PasswordButtonText), nameof(TotpButtonText), nameof(IsLockedOut), nameof(LockoutText) })
                NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private void FactorSelection_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;

        var hasPassword = _securityService.GetUiState().HasPassword;
        foreach (var option in e.AddedItems.OfType<MultiSelectSettingOption>())
        {
            if (option != FactorOptions[0]) option.SetSelected(true);
        }

        foreach (var option in e.RemovedItems.OfType<MultiSelectSettingOption>())
        {
            if (option != FactorOptions[0]) option.SetSelected(false);
        }

        if (hasPassword && !SelectedFactorOptions.Contains(FactorOptions[0]))
            SelectedFactorOptions.Insert(0, FactorOptions[0]);
    }

    private async void ManagePassword_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dialog = new PasswordEditorWindow(_securityService.GetUiState().HasPassword);
        var result = await dialog.ShowDialog<PasswordEditorResult?>(owner);
        if (result is null) return;
        var saved = result.Remove ? await _securityService.RemovePasswordAsync(result.CurrentPassword) : await _securityService.SetPasswordAsync(result.NewPassword, result.CurrentPassword);
        if (saved) this.ShowSuccessToast(result.Remove ? SR.M_PasswordRemoved : SR.M_PasswordSaved); else this.ShowErrorToast(SR.M_PasswordSaveFailed);
        RefreshSecurityState();
    }

    private async void ManageTotp_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var secret = await _securityService.BeginTotpSetupAsync();
        if (secret is null) { this.ShowWarningToast(SR.M_SetPasswordFirst); return; }
        var code = await new TotpSetupWindow(secret).ShowDialog<string?>(owner);
        if (code is not null && await _securityService.ConfirmTotpAsync(secret, code)) this.ShowSuccessToast(SR.M_TotpSaved);
        else if (code is not null) this.ShowErrorToast(SR.M_TotpSaveFailed);
        RefreshSecurityState();
    }

    private async void ManageUsb_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var result = await new UsbBindingWindow(await _securityService.GetUsbBindingsAsync()).ShowDialog<UsbBindingResult?>(owner);
        if (result is null) return;
        var success = result.UnbindId is not null ? await _securityService.UnbindUsbAsync(result.UnbindId) : await _securityService.BindUsbAsync(result.RootPath!);
        if (success) this.ShowSuccessToast(SR.M_UsbUpdated); else this.ShowErrorToast(SR.M_UsbUpdateFailed);
        RefreshSecurityState();
    }
}
