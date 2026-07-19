using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Extensions.Registry;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.Config;
using SecRandom.Controls.AttachedSettings;
using SecRandom.Services.Linkage;
using SecRandom.Services.CrashRecovery;
using SecRandom.Services.Updates;
using SecRandom.Shared;
using SecRandom.Views;
using DebugResources = SecRandom.Langs.SettingsPages.Debug.DebugStrings;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.debug", FluentIcons.BugFilled, location: PageLocation.Bottom, isHide: true)]
public partial class DebugSettingsPage : UserControl, INotifyPropertyChanged
{
    private readonly MainConfigHandler _configHandler = IAppHost.GetService<MainConfigHandler>();
    private readonly CourseLinkageService _courseLinkage = IAppHost.GetService<CourseLinkageService>();
    private readonly UpdateCenterService _updateCenter = IAppHost.GetService<UpdateCenterService>();
    private string _updateDiagnostics = string.Empty;
    private string _linkageAndNotificationDiagnostics = string.Empty;
    private string _platformDiagnostics = string.Empty;
    private string _dataAndPathDiagnostics = string.Empty;
    private bool _isInternalSettingsEnabled;
    private bool _isPluginSettingsVisible;
    private bool _isUpdatingInternalSettingsToggle;
    private bool _isUpdatingPluginSettingsToggle;

    public DebugSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        InternalSettingsToggle.IsCheckedChanged += InternalSettingsToggle_OnIsCheckedChanged;
        PluginSettingsToggle.IsCheckedChanged += PluginSettingsToggle_OnIsCheckedChanged;
        RefreshDiagnostics();
    }

    public string UpdateDiagnostics
    {
        get => _updateDiagnostics;
        private set => SetDiagnostic(ref _updateDiagnostics, value, nameof(UpdateDiagnostics));
    }

    public string LinkageAndNotificationDiagnostics
    {
        get => _linkageAndNotificationDiagnostics;
        private set => SetDiagnostic(ref _linkageAndNotificationDiagnostics, value, nameof(LinkageAndNotificationDiagnostics));
    }

    public string PlatformDiagnostics
    {
        get => _platformDiagnostics;
        private set => SetDiagnostic(ref _platformDiagnostics, value, nameof(PlatformDiagnostics));
    }

    public string DataAndPathDiagnostics
    {
        get => _dataAndPathDiagnostics;
        private set => SetDiagnostic(ref _dataAndPathDiagnostics, value, nameof(DataAndPathDiagnostics));
    }

    public bool IsInternalSettingsEnabled
    {
        get => _isInternalSettingsEnabled;
        private set => SetDiagnostic(ref _isInternalSettingsEnabled, value, nameof(IsInternalSettingsEnabled));
    }

    public bool IsPluginSettingsVisible
    {
        get => _isPluginSettingsVisible;
        private set => SetDiagnostic(ref _isPluginSettingsVisible, value, nameof(IsPluginSettingsVisible));
    }

    public DebugResources Strings { get; } = new();

    private event PropertyChangedEventHandler? DiagnosticsPropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => DiagnosticsPropertyChanged += value;
        remove => DiagnosticsPropertyChanged -= value;
    }

    private void SendToast_OnClick(object? sender, RoutedEventArgs e)
    {
        this.ShowToast(DebugResources.Get("M_TestToast"));
    }

    private void OpenDrawer_Test(object? sender, RoutedEventArgs e)
    {
        SettingsView.Current?.OpenDrawer(this.FindResource("DrawerTest")!);
    }

    private void ShowCrashRecoveryWindow_OnClick(object? sender, RoutedEventArgs e)
    {
        string reportPath = CrashRecoveryRuntime.TryWriteCrashReport(
            new InvalidOperationException("SECRANDOM_DEBUG_CRASH_PROMPT"),
            [Path.Combine(Path.GetTempPath(), "SecRandom", "crashes")]);

        CrashRecoveryWindow window = new(
            new CrashRecoveryPromptOptions(reportPath, false),
            () =>
            {
                App.Current.Restart();
                return true;
            });
        window.Show();
    }

    private void ThrowDispatcherException_OnClick(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => throw new InvalidOperationException("SECRANDOM_DEBUG_DISPATCHER_CRASH"));
    }

    private void RestartProgram_OnClick(object? sender, RoutedEventArgs e)
    {
        App.Current.Restart();
    }

    private void HideDebugNavigation_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsView.Current?.HideDebugNavigationItem();
        SettingsView.Current?.NavigateToPage("settings.about");
    }

    private async void PluginSettingsToggle_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingPluginSettingsToggle || sender is not ToggleSwitch toggle)
            return;

        if (toggle.IsChecked != true)
        {
            SettingsView.Current?.SetPluginSettingsNavigationVisible(false);
            IsPluginSettingsVisible = false;
            return;
        }

        var result = await new FAContentDialog
        {
            Title = DebugResources.Get("M_ShowPluginSettings_Title"),
            Content = DebugResources.Get("M_ShowPluginSettings"),
            PrimaryButtonText = DebugResources.Get("C_Show"),
            CloseButtonText = DebugResources.Get("C_Cancel"),
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));

        if (result != FAContentDialogResult.Primary)
        {
            _isUpdatingPluginSettingsToggle = true;
            toggle.IsChecked = false;
            _isUpdatingPluginSettingsToggle = false;
            return;
        }

        SettingsView.Current?.SetPluginSettingsNavigationVisible(true);
        IsPluginSettingsVisible = true;
    }

    private async void InternalSettingsToggle_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingInternalSettingsToggle || sender is not ToggleSwitch toggle)
            return;

        if (toggle.IsChecked != true)
        {
            AttachedSettingsRegistryExtensions.UnregisterAttachedSettingsControl<BehindSceneAttachedSettingsControl>();
            IsInternalSettingsEnabled = false;
            return;
        }

        var result = await new FAContentDialog
        {
            Title = DebugResources.Get("M_InternalSettings_ConfirmTitle"),
            Content = new TextBlock
            {
                Text = DebugResources.Get("M_InternalSettings"),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            },
            PrimaryButtonText = DebugResources.Get("C_Enable"),
            CloseButtonText = DebugResources.Get("C_Cancel"),
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));

        if (result != FAContentDialogResult.Primary)
        {
            _isUpdatingInternalSettingsToggle = true;
            toggle.IsChecked = false;
            _isUpdatingInternalSettingsToggle = false;
            return;
        }

        AttachedSettingsRegistryExtensions.RegisterAttachedSettingsControl<BehindSceneAttachedSettingsControl>(
            SecRandom.Langs.Common.Resources.AttachedSettings_BehindScene);
        IsInternalSettingsEnabled = true;
    }

    private async void RefreshDiagnostics_OnClick(object? sender, RoutedEventArgs e)
    {
        await _courseLinkage.RefreshAsync();
        RefreshDiagnostics();
    }

    private void RefreshDiagnostics()
    {
        var updateSettings = _configHandler.Data.UpdateSettings;
        UpdateDiagnostics = $"{T("C_CurrentVersion")}: {_updateCenter.CurrentVersion}\n"
                            + $"{T("C_UpdateChannel")}: {updateSettings.UpdateChannel}\n"
                            + $"{T("C_CheckPhase")}: {_updateCenter.Phase}\n"
                            + $"{T("C_Status")}: {_updateCenter.Status}\n"
                            + $"{T("C_LastCheck")}: {FormatTime(updateSettings.LastCheckTime)}\n"
                            + $"{T("C_AvailableVersion")}: {EmptyAsDash(_updateCenter.AvailableVersion)}";

        var snapshot = _courseLinkage.Snapshot;
        var notification = _configHandler.Data.NotificationSettings.Default;
        LinkageAndNotificationDiagnostics = $"{T("C_LinkageSource")}: {_courseLinkage.Settings.DataSource}\n"
                                            + $"{T("C_CourseSource")}: {snapshot.Source}\n"
                                            + $"{T("C_CourseState")}: {snapshot.State}\n"
                                            + $"{T("C_SnapshotAvailable")}: {snapshot.IsAvailable}\n"
                                            + $"{T("C_CurrentCourse")}: {snapshot.CurrentCourse?.Name ?? "-"}\n"
                                            + $"{T("C_NextCourse")}: {snapshot.NextCourse?.Name ?? "-"}\n"
                                            + $"{T("C_DrawRestricted")}: {_courseLinkage.IsConfirmedNonClassTime}\n"
                                            + $"{T("C_SnapshotError")}: {EmptyAsDash(snapshot.Error)}\n\n"
                                            + $"{T("C_NotificationsEnabled")}: {notification.Enabled}\n"
                                            + $"{T("C_BuiltInNotification")}: {notification.UsesBuiltInNotificationService}\n"
                                            + $"{T("C_ExternalNotification")}: {notification.UsesExternalNotificationService}\n"
                                            + $"{T("C_NotificationFallback")}: {notification.UseBuiltInOnServiceFailure}\n"
                                            + $"{T("C_DisplayDuration")}: {notification.DisplayDuration}s";

        PlatformDiagnostics = $"{T("C_Branch")}: {GlobalConstants.Branch}\n"
                              + $"{T("C_Commit")}: {GlobalConstants.FullCommitHash}\n"
                              + $"{T("C_Version")}: {GlobalConstants.VersionLong}\n"
                              + $"{T("C_OperatingSystem")}: {RuntimeInformation.OSDescription}\n"
                              + $"{T("C_Architecture")}: {RuntimeInformation.OSArchitecture}\n"
                              + $"{T("C_Runtime")}: {RuntimeInformation.FrameworkDescription}\n"
                              + $"{T("C_AppDirectory")}: {AppContext.BaseDirectory}\n"
                              + $"{T("C_PackageRoot")}: {Utils.PackageRoot}\n"
                              + $"{T("C_DataDirectory")}: {Utils.DataRoot}\n"
                              + $"{T("C_MainWindowCreated")}: {MainView.Current is not null}\n"
                              + $"{T("C_SettingsWindowCreated")}: {SettingsView.Current is not null}";

        var dataRoot = Utils.DataRoot;
        DataAndPathDiagnostics = FormatPath(T("C_SettingsFile"), _configHandler.Data.ConfigFilePath, isDirectory: false)
                                 + FormatPath(T("C_DataDirectory"), dataRoot, isDirectory: true)
                                 + FormatPath(T("C_LogDirectory"), Path.Combine(dataRoot, "logs"), isDirectory: true)
                                 + FormatPath(T("C_BackupDirectory"), Path.Combine(dataRoot, "backup"), isDirectory: true)
                                 + FormatPath(T("C_PluginDirectory"), Path.Combine(dataRoot, "plugins"), isDirectory: true)
                                 + FormatPath(T("C_PluginConfigDirectory"), Path.Combine(dataRoot, "configs", "plugins"), isDirectory: true)
                                 + FormatPath(T("C_ProofDirectory"), Path.Combine(dataRoot, "proofs"), isDirectory: true)
                                 + FormatPath(T("C_UpdateDownloadDirectory"), Path.Combine(dataRoot, "updates", "downloads"), isDirectory: true)
                                 + FormatPath(T("C_CourseLinkageDirectory"), Path.Combine(dataRoot, "CSES"), isDirectory: true)
                                 + FormatPath(T("C_CrashDirectory"), Path.Combine(dataRoot, "crashes"), isDirectory: true);
    }

    private void SetDiagnostic<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        DiagnosticsPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string FormatTime(DateTime? value) => value?.ToString("G", CultureInfo.CurrentCulture) ?? "-";
    private static string EmptyAsDash(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
    private static string T(string key) => DebugResources.Get(key);
    private static string FormatPath(string name, string path, bool isDirectory)
    {
        var exists = isDirectory ? Directory.Exists(path) : File.Exists(path);
        return $"{name}: {T(exists ? "C_Exists" : "C_Missing")}\n{path}\n";
    }

}
