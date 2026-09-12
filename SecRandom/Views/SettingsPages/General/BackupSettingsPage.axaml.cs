using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Models;
using SecRandom.Services.Auth;
using SecRandom.Services.Desktop;
using SecRandom.Core.Services.Archive;
using SecRandom.Services.ImportExport;
using SecRandom.Shared;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.SettingsPages.General.Backup.Resources;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.backup", FluentIcons.ArchiveFilled, "settings.general")]
public partial class BackupSettingsPage : UserControl, INotifyPropertyChanged
{
    private const string BackupDirectoryName = "backup";

    /// <summary>SECTL personal cloud storage dashboard, opened in the system browser.</summary>
    private const string CloudStoragePageUrl = "https://sectl.cn/dashboard/cloud";

    private string _backupUsageText = FormatSize(0);
    private bool _isSubscribed;
    private bool _isIncludeOptionsSubscribed;
    private bool _isCloudBusy;
    private bool _isCloudUploading;
    private bool _isCloudRefreshing;
    private bool _isCloudQuotaRefreshing;
    private bool _isAccountSubscribed;
    private bool _isCloudEventSubscribed;
    private string _cloudAccountText = string.Empty;
    private string _cloudQuotaText = string.Empty;
    private string _cloudListStatusText = string.Empty;
    private CancellationTokenSource? _cloudOperation;
    private readonly Dictionary<string, CloudBackupDescriptor> _cloudDescriptors = new(StringComparer.Ordinal);
    private event PropertyChangedEventHandler? NotifyPropertyChanged;
    private readonly ILogger<BackupSettingsPage> _logger =
        IAppHost.GetService<ILogger<BackupSettingsPage>>();
    private readonly IImportExportService _importExportService = IAppHost.GetService<IImportExportService>();
    private readonly SectlAuthService? _authService = IAppHost.TryGetService<SectlAuthService>();
    private readonly CloudBackupService? _cloudBackupService = IAppHost.TryGetService<CloudBackupService>();
    private IExternalLauncher ExternalLauncher { get; } = IAppHost.GetService<IExternalLauncher>();

    public BackupSettingsPage()
    {
        Settings = ViewModel.Config.Backup;
        IncludeOptions =
        [
            new(LR.S_Includes_Config, () => Settings.IncludeConfig,
                value => Settings.IncludeConfig = value),
            new(LR.S_Includes_List, () => Settings.IncludeList,
                value => Settings.IncludeList = value),
            new(LR.S_Includes_History, () => Settings.IncludeHistory,
                value => Settings.IncludeHistory = value),
            new(LR.S_Includes_Proofs, () => Settings.IncludeProofs,
                value => Settings.IncludeProofs = value),
            new(LR.S_Includes_Audio, () => Settings.IncludeAudio,
                value => Settings.IncludeAudio = value),
            new(LR.S_Includes_Cses, () => Settings.IncludeCses,
                value => Settings.IncludeCses = value),
            new(LR.S_Includes_Images, () => Settings.IncludeImages,
                value => Settings.IncludeImages = value),
            new(LR.S_Includes_Logs, () => Settings.IncludeLogs,
                value => Settings.IncludeLogs = value)
        ];
        SelectedIncludeOptions = BuildSelectedOptions(IncludeOptions);
        CloudIncludeOptions =
        [
            new(LR.S_Includes_Config, () => Settings.CloudIncludeConfig,
                value => Settings.CloudIncludeConfig = value),
            new(LR.S_Includes_List, () => Settings.CloudIncludeList,
                value => Settings.CloudIncludeList = value),
            new(LR.S_Includes_History, () => Settings.CloudIncludeHistory,
                value => Settings.CloudIncludeHistory = value),
            new(LR.S_Includes_Audio, () => Settings.CloudIncludeAudio,
                value => Settings.CloudIncludeAudio = value),
            new(LR.S_Includes_Cses, () => Settings.CloudIncludeCses,
                value => Settings.CloudIncludeCses = value),
            new(LR.S_Includes_Images, () => Settings.CloudIncludeImages,
                value => Settings.CloudIncludeImages = value)
        ];
        SelectedCloudIncludeOptions = BuildSelectedOptions(CloudIncludeOptions);
        DataContext = this;
        InitializeComponent();
        SubscribeSettings();
        RefreshBackups();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public BackupConfig Settings { get; }

    public ObservableCollection<BackupMetadata> Backups { get; } = [];
    public ObservableCollection<CloudBackupMetadata> CloudBackups { get; } = [];
    public AvaloniaList<MultiSelectSettingOption> IncludeOptions { get; }
    public AvaloniaList<MultiSelectSettingOption> SelectedIncludeOptions { get; }

    /// <summary>
    ///     Cloud backup content is its own selection; the cloud list has no log row because logs,
    ///     the device identity, and the voice cache can never be uploaded.
    /// </summary>
    public AvaloniaList<MultiSelectSettingOption> CloudIncludeOptions { get; }
    public AvaloniaList<MultiSelectSettingOption> SelectedCloudIncludeOptions { get; }

    /// <summary>
    ///     The cloud section only exists where the desktop SECTL account services are registered; the
    ///     mobile host has no account session, so it hides the whole section instead of failing.
    /// </summary>
    public bool IsCloudVisible => _cloudBackupService is not null && _authService is not null;

    public bool IsCloudSignedIn => _authService?.IsSignedIn == true;

    public bool IsCloudBusy
    {
        get => _isCloudBusy;
        private set
        {
            if (_isCloudBusy == value)
                return;

            _isCloudBusy = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Only the upload flow offers cancellation; restore keeps running once it starts.</summary>
    public bool IsCloudUploading
    {
        get => _isCloudUploading;
        private set
        {
            if (_isCloudUploading == value)
                return;

            _isCloudUploading = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    ///     Disables the quota refresh command while the usage endpoint is being queried; the quota
    ///     label itself switches to the loading text for the same window.
    /// </summary>
    public bool IsCloudQuotaRefreshing
    {
        get => _isCloudQuotaRefreshing;
        private set
        {
            if (_isCloudQuotaRefreshing == value)
                return;

            _isCloudQuotaRefreshing = value;
            OnPropertyChanged();
        }
    }

    public string CloudAccountText
    {
        get => _cloudAccountText;
        private set
        {
            if (_cloudAccountText == value)
                return;

            _cloudAccountText = value;
            OnPropertyChanged();
        }
    }

    public string CloudQuotaText
    {
        get => _cloudQuotaText;
        private set
        {
            if (_cloudQuotaText == value)
                return;

            _cloudQuotaText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    ///     Transient status shown next to the cloud list commands so a long list refresh or cleanup
    ///     purge still gives immediate feedback while both buttons are disabled.
    /// </summary>
    public string CloudListStatusText
    {
        get => _cloudListStatusText;
        private set
        {
            if (_cloudListStatusText == value)
                return;

            _cloudListStatusText = value;
            OnPropertyChanged();
        }
    }

    public string BackupUsageText
    {
        get => _backupUsageText;
        private set
        {
            if (_backupUsageText == value)
                return;

            _backupUsageText = value;
            OnPropertyChanged();
        }
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private SecRandom.Core.Services.Config.MainConfigHandler ConfigHandler { get; } =
        IAppHost.GetService<SecRandom.Core.Services.Config.MainConfigHandler>();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SubscribeSettings();
        SubscribeAccount();
        SubscribeCloudBackupEvents();
        if (IsCloudVisible)
        {
            // Entering the page always shows a current list and a current quota, so the page does not
            // depend on the user pressing a refresh command first.
            _ = RefreshCloudAsync();
            _ = RefreshCloudQuotaAsync();
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        UnsubscribeSettings();
        UnsubscribeIncludeOptions();
        UnsubscribeAccount();
        UnsubscribeCloudBackupEvents();
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
    }

    private void SubscribeSettings()
    {
        if (!_isSubscribed)
        {
            Settings.PropertyChanged += SettingsOnPropertyChanged;
            _isSubscribed = true;
        }

        SubscribeIncludeOptions();
    }

    private void UnsubscribeSettings()
    {
        if (!_isSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        _isSubscribed = false;
    }

    private void SubscribeIncludeOptions()
    {
        if (_isIncludeOptionsSubscribed)
            return;

        SelectedIncludeOptions.CollectionChanged += IncludeOptionsOnCollectionChanged;
        SelectedCloudIncludeOptions.CollectionChanged += CloudIncludeOptionsOnCollectionChanged;
        _isIncludeOptionsSubscribed = true;
    }

    private void UnsubscribeIncludeOptions()
    {
        if (!_isIncludeOptionsSubscribed)
            return;

        SelectedIncludeOptions.CollectionChanged -= IncludeOptionsOnCollectionChanged;
        SelectedCloudIncludeOptions.CollectionChanged -= CloudIncludeOptionsOnCollectionChanged;
        _isIncludeOptionsSubscribed = false;
    }

    private void RefreshBackups()
    {
        Backups.Clear();

        var backupDirectory = GetBackupDirectory();
        var directoryInfo = new DirectoryInfo(backupDirectory);
        var totalBytes = 0L;

        foreach (var file in directoryInfo.EnumerateFiles("*.zip").OrderByDescending(file => file.CreationTimeUtc))
        {
            totalBytes += file.Length;
            Backups.Add(new BackupMetadata
            {
                FileName = file.Name,
                FilePath = file.FullName,
                DateTime = file.CreationTime,
                Size = FormatSize(file.Length)
            });
        }

        BackupUsageText = FormatSize(totalBytes);
    }

    private void RefreshBackups_OnClick(object? sender, RoutedEventArgs e)
    {
        RefreshBackups();
    }

    private static AvaloniaList<MultiSelectSettingOption> BuildSelectedOptions(
        IEnumerable<MultiSelectSettingOption> options)
    {
        return new AvaloniaList<MultiSelectSettingOption>(options.Where(option => option.IsSelected));
    }

    private void IncludeOptionsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs _) =>
        ApplyIncludeSelection(IncludeOptions, SelectedIncludeOptions);

    private void CloudIncludeOptionsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs _) =>
        ApplyIncludeSelection(CloudIncludeOptions, SelectedCloudIncludeOptions);

    private void ApplyIncludeSelection(IEnumerable<MultiSelectSettingOption> options,
        IReadOnlyCollection<MultiSelectSettingOption> selected)
    {
        foreach (var option in options)
        {
            option.SetSelected(selected.Contains(option));
        }

        ConfigHandler.Save();
    }

    private void SubscribeAccount()
    {
        if (_authService is null || _isAccountSubscribed)
            return;

        _authService.StateChanged += AccountOnStateChanged;
        _isAccountSubscribed = true;
    }

    private void UnsubscribeAccount()
    {
        if (_authService is null || !_isAccountSubscribed)
            return;

        _authService.StateChanged -= AccountOnStateChanged;
        _isAccountSubscribed = false;
    }

    private void AccountOnStateChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            _ = RefreshCloudAsync();
            _ = RefreshCloudQuotaAsync();
        });

    private void SubscribeCloudBackupEvents()
    {
        if (_cloudBackupService is null || _isCloudEventSubscribed)
            return;

        _cloudBackupService.AutomaticBackupUploaded += CloudBackupServiceOnAutomaticBackupUploaded;
        _isCloudEventSubscribed = true;
    }

    private void UnsubscribeCloudBackupEvents()
    {
        if (_cloudBackupService is null || !_isCloudEventSubscribed)
            return;

        _cloudBackupService.AutomaticBackupUploaded -= CloudBackupServiceOnAutomaticBackupUploaded;
        _isCloudEventSubscribed = false;
    }

    /// <summary>
    ///     An automatic backup lands on a background thread and changes both the backup list and the
    ///     account quota, so an open page reloads them instead of showing stale values.
    /// </summary>
    private void CloudBackupServiceOnAutomaticBackupUploaded(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            _ = RefreshCloudAsync();
            _ = RefreshCloudQuotaAsync();
        });

    private void RefreshCloudAccountPresentation()
    {
        bool signedIn = IsCloudSignedIn;
        CloudAccountText = signedIn
            ? string.Format(LR.C_CloudBackup_SignedInAs,
                FirstNonBlank(_authService?.User?.ResolvedUserName, _authService?.Token?.UserId) ?? string.Empty)
            : LR.C_CloudBackup_NotSignedIn;
        OnPropertyChanged(nameof(IsCloudSignedIn));
    }

    /// <summary>
    ///     Reloads the cloud backup list. Cloud state is never cached in configuration; it is always
    ///     discovered from the signed-in account so several devices stay consistent.
    /// </summary>
    private async Task RefreshCloudAsync()
    {
        if (_cloudBackupService is null)
            return;

        RefreshCloudAccountPresentation();
        if (!IsCloudSignedIn)
        {
            ClearCloudBackups();
            CloudQuotaText = string.Empty;
            return;
        }

        if (_isCloudRefreshing)
            return;

        _isCloudRefreshing = true;
        try
        {
            IReadOnlyList<CloudBackupDescriptor> backups = await _cloudBackupService.ListAsync();
            _cloudDescriptors.Clear();
            CloudBackups.Clear();
            foreach (CloudBackupDescriptor descriptor in backups)
            {
                _cloudDescriptors[descriptor.BackupId] = descriptor;
                CloudBackups.Add(ToRow(descriptor));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取云端备份列表失败。");
            this.ShowErrorToast(LR.M_CloudRefreshFailed);
        }
        finally
        {
            _isCloudRefreshing = false;
        }
    }

    /// <summary>
    ///     Quota is fetched on demand only: the usage endpoint scans the whole account and is much
    ///     slower than the file list, so page loads, list refreshes, and mutations must not wait for it.
    /// </summary>
    private async Task RefreshCloudQuotaAsync()
    {
        if (_cloudBackupService is null || !IsCloudSignedIn || _isCloudQuotaRefreshing)
            return;

        IsCloudQuotaRefreshing = true;
        CloudQuotaText = LR.M_CloudQuotaLoading;
        try
        {
            SectlCloudQuota quota = await _cloudBackupService.GetQuotaAsync();
            CloudQuotaText = string.Format(LR.C_CloudBackup_QuotaUsage, FormatSize(quota.Used),
                FormatSize(quota.Total), FormatSize(quota.PlatformUsed));
        }
        catch (Exception quotaException)
        {
            _logger.LogWarning(quotaException, "获取云端空间用量失败。");
            CloudQuotaText = LR.M_CloudQuotaUnknown;
        }
        finally
        {
            IsCloudQuotaRefreshing = false;
        }
    }

    private void ClearCloudBackups()
    {
        _cloudDescriptors.Clear();
        CloudBackups.Clear();
    }

    private async void CloudSignIn_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_authService is null || _isCloudBusy)
            return;

        try
        {
            await _authService.SignInAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SECTL 账号登录失败。");
            await ShowErrorDialogAsync(LR.M_CloudSignedOut, ex.Message);
            return;
        }

        RefreshCloudAccountPresentation();
        await RefreshCloudAsync();
    }

    private async void CloudRefresh_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isCloudBusy)
            return;

        CloudListStatusText = LR.M_CloudRefreshing;
        try
        {
            await RefreshCloudAsync();
            await RefreshCloudQuotaAsync();
        }
        finally
        {
            CloudListStatusText = string.Empty;
        }
    }

    private async void CloudQuotaRefresh_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_cloudBackupService is null)
            return;
        if (!IsCloudSignedIn)
        {
            this.ShowWarningToast(LR.M_CloudSignInRequired);
            return;
        }

        await RefreshCloudQuotaAsync();
    }

    private void CloudStoragePage_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!ExternalLauncher.TryOpenUri(CloudStoragePageUrl))
            this.ShowErrorToast(LR.M_CloudStoragePageFailed);
    }

    private async void CloudUpload_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_cloudBackupService is null || _isCloudBusy)
            return;
        if (!IsCloudSignedIn)
        {
            this.ShowWarningToast(LR.M_CloudSignInRequired);
            return;
        }

        _cloudOperation = new CancellationTokenSource();
        SetCloudBusy(true, uploading: true);
        try
        {
            await _cloudBackupService.UploadAsync(cancellationToken: _cloudOperation.Token);
            this.ShowSuccessToast(LR.M_CloudUploadSuccess);
        }
        catch (OperationCanceledException)
        {
            this.ShowWarningToast(LR.M_CloudUploadCancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "云端备份上传失败。");
            await ShowErrorDialogAsync(LR.M_CloudUploadFailed, DescribeCloudUploadError(ex));
        }
        finally
        {
            EndCloudOperation();
            // The upload row reports its own state, so the list refresh is what tells the user
            // whether the new backup actually landed; it must run on success and on failure alike.
            await RefreshCloudAsync();
            _ = RefreshCloudQuotaAsync();
        }
    }

    private void CloudCancel_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            _cloudOperation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation already finished; there is nothing left to cancel.
        }
    }

    private async void CloudCleanup_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_cloudBackupService is null || _isCloudBusy)
            return;
        if (!IsCloudSignedIn)
        {
            this.ShowWarningToast(LR.M_CloudSignInRequired);
            return;
        }

        SetCloudBusy(true);
        CloudListStatusText = LR.M_CloudCleaning;
        try
        {
            int removed = await _cloudBackupService.PurgeIncompleteAsync();
            this.ShowSuccessToast(string.Format(LR.M_CloudCleanupSuccess, removed));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理不完整云端备份失败。");
            await ShowErrorDialogAsync(LR.M_CloudCleanupFailed, DescribeCloudError(ex));
        }
        finally
        {
            EndCloudOperation();
            await RefreshCloudAsync();
            CloudListStatusText = string.Empty;
        }
    }

    private async void CloudRestoreBackup_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_cloudBackupService is null || _isCloudBusy)
            return;
        if ((sender as Button)?.CommandParameter is not CloudBackupMetadata row)
            return;

        CloudBackupDescriptor? descriptor = FindCloudDescriptor(row.BackupId);
        if (descriptor is null || !descriptor.CanRestore)
        {
            this.ShowWarningToast(LR.M_CloudIncomplete);
            await RefreshCloudAsync();
            return;
        }

        if (!await ConfirmCloudRestoreAsync(row.DisplayName))
            return;

        _cloudOperation = new CancellationTokenSource();
        SetCloudBusy(true);
        string? archivePath = null;
        try
        {
            archivePath = await _cloudBackupService.DownloadAsync(descriptor, _cloudOperation.Token);
            ImportInspection inspection = await _importExportService.InspectCloudBackupAsync(archivePath);
            if (!inspection.IsSupportedV3)
            {
                await ShowErrorDialogAsync(LR.M_CloudRestoreFailed,
                    inspection.Warnings.FirstOrDefault() ?? LR.M_CloudIncomplete);
                return;
            }

            await _importExportService.ImportCloudBackupAsync(archivePath);
            _logger.LogInformation("已恢复云端备份：备份={BackupId}。", descriptor.BackupId);
            this.ShowSuccessToast(LR.M_CloudRestoreSuccess);
            SettingsView.Current?.RequestRestartApp();
        }
        catch (OperationCanceledException)
        {
            this.ShowWarningToast(LR.M_CloudDownloadFailed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复云端备份失败：备份={BackupId}。", descriptor.BackupId);
            await ShowErrorDialogAsync(LR.M_CloudRestoreFailed, DescribeCloudError(ex));
        }
        finally
        {
            EndCloudOperation();
            TryDeleteCloudArchive(archivePath);
        }
    }

    private async void CloudDeleteBackup_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_cloudBackupService is null || _isCloudBusy)
            return;
        if ((sender as Button)?.CommandParameter is not CloudBackupMetadata row)
            return;

        CloudBackupDescriptor? descriptor = FindCloudDescriptor(row.BackupId);
        if (descriptor is null)
        {
            await RefreshCloudAsync();
            return;
        }

        if (!await ConfirmCloudDeleteAsync(row.DisplayName))
            return;

        SetCloudBusy(true);
        try
        {
            await _cloudBackupService.DeleteAsync(descriptor);
            this.ShowSuccessToast(LR.M_CloudDeleteSuccess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除云端备份失败：备份={BackupId}。", descriptor.BackupId);
            await ShowErrorDialogAsync(LR.M_CloudDeleteFailed, DescribeCloudError(ex));
        }
        finally
        {
            EndCloudOperation();
            await RefreshCloudAsync();
        }
    }

    private CloudBackupDescriptor? FindCloudDescriptor(string backupId) =>
        _cloudDescriptors.GetValueOrDefault(backupId);

    private void SetCloudBusy(bool value, bool uploading = false)
    {
        IsCloudUploading = uploading;
        IsCloudBusy = value;
    }

    private void EndCloudOperation()
    {
        _cloudOperation?.Dispose();
        _cloudOperation = null;
        IsCloudUploading = false;
        IsCloudBusy = false;
    }

    private static CloudBackupMetadata ToRow(CloudBackupDescriptor descriptor) => new()
    {
        BackupId = descriptor.BackupId,
        DisplayName = descriptor.DisplayName,
        DateTime = descriptor.CreatedAt?.LocalDateTime ?? DateTime.MinValue,
        Size = FormatSize(descriptor.TotalBytes),
        IsComplete = descriptor.IsComplete,
        PartCount = descriptor.PartCount,
        StatusText = descriptor.IsComplete ? string.Empty : LR.C_CloudBackup_Incomplete
    };

    /// <summary>Maps cloud service error codes onto localized guidance instead of raw API text.</summary>
    private static string DescribeCloudError(Exception exception) => exception switch
    {
        SectlCloudStorageException { Code: "storage_exceeded" } => LR.M_CloudQuotaExceeded,
        SectlCloudStorageException { Code: "cloud_service_disabled" or "insufficient_scope" } => LR.M_CloudServiceDisabled,
        SectlCloudStorageException { Code: "invalid_token" or "unauthorized" } => LR.M_CloudSignedOut,
        SectlCloudStorageException { Code: "cloud_timeout" } => LR.M_CloudServerTimeout,
        SectlCloudStorageException { Code: "internal_error" or "upload_failed" or "invalid_response" } => LR.M_CloudServerError,
        InvalidOperationException => LR.M_CloudSignedOut,
        _ => exception.Message
    };

    /// <summary>
    ///     A timed-out upload may still have created parts on the server after the gateway gave up,
    ///     so the failure text points at the cleanup action instead of leaving that state unexplained.
    /// </summary>
    private static string DescribeCloudUploadError(Exception exception)
    {
        var detail = DescribeCloudError(exception);
        return exception is SectlCloudStorageException { Code: "cloud_timeout" or "internal_error" or "upload_failed" }
            ? $"{detail}\n{LR.M_CloudUploadCleanupHint}"
            : detail;
    }

    private static void TryDeleteCloudArchive(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // The staging archive stays under data/cache and is never part of an export root.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private async Task<bool> ConfirmCloudRestoreAsync(string displayName)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_CloudRestoreTitle,
            Content = string.Format(LR.M_CloudRestoreContent, displayName),
            PrimaryButtonText = LR.M_RestorePrimary,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result == FAContentDialogResult.Primary;
    }

    private async Task<bool> ConfirmCloudDeleteAsync(string displayName)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_CloudDeleteTitle,
            Content = string.Format(LR.M_CloudDeleteContent, displayName),
            PrimaryButtonText = LR.M_DeletePrimary,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result == FAContentDialogResult.Primary;
    }

    private async void ManualBackup_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = _importExportService.CreateManualBackup(GetSelectedDataRoots().ToList());
            RefreshBackups();
            _logger.LogInformation("已创建手动备份：文件={FileName}。", Path.GetFileName(path));
            this.ShowSuccessToast(string.Format(LR.M_BackupCreated, Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建手动备份失败。");
            await ShowErrorDialogAsync(LR.M_BackupFailed, ex.Message);
        }
    }

    private void ViewBackupFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var directory = GetBackupDirectory();
        if (!ExternalLauncher.TryOpenPath(directory))
            this.ShowErrorToast("无法打开备份目录。");
        _logger.LogInformation("已请求打开备份目录：路径={Path}。", directory);
    }

    private async void RestoreBackup_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not BackupMetadata backup)
            return;

        if (!File.Exists(backup.FilePath))
        {
            this.ShowWarningToast(LR.M_BackupFileMissing);
            RefreshBackups();
            return;
        }

        try
        {
            var inspection = await _importExportService.InspectAllDataAsync(backup.FilePath);
            if (!inspection.IsSupportedV3)
            {
                var version = string.IsNullOrWhiteSpace(inspection.ProducerVersion) ? "未识别" : inspection.ProducerVersion;
                var detail = inspection.Warnings.FirstOrDefault() ?? "该文件不是受支持的 SecRandom v3 数据归档。";
                await ShowErrorDialogAsync(LR.M_RestoreFailed, $"仅支持 SecRandom v3 数据归档。检测到的版本：{version}。\n{detail}");
                return;
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync(LR.M_RestoreFailed, ex.Message);
            return;
        }

        if (!await ConfirmRestoreAsync(backup.FileName))
            return;

        try
        {
            await _importExportService.RestoreBackupAsync(backup.FilePath);
            RefreshBackups();
            SettingsView.Current?.RequestRestartApp();
            _logger.LogInformation("已恢复备份：文件={FileName}。", backup.FileName);
            this.ShowSuccessToast(LR.M_RestoreSuccess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复备份失败：文件={FileName}。", backup.FileName);
            await ShowErrorDialogAsync(LR.M_RestoreFailed, ex.Message);
        }
    }

    private async void DeleteBackup_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not BackupMetadata backup)
            return;

        if (!await ConfirmDeleteAsync(backup.FileName))
            return;

        try
        {
            if (File.Exists(backup.FilePath))
                File.Delete(backup.FilePath);

            RefreshBackups();
            _logger.LogInformation("已删除备份：文件={FileName}。", backup.FileName);
            this.ShowSuccessToast(LR.M_DeleteSuccess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除备份失败：文件={FileName}。", backup.FileName);
            await ShowErrorDialogAsync(LR.M_DeleteFailed, ex.Message);
        }
    }

    private async Task<bool> ConfirmRestoreAsync(string fileName)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_RestoreTitle,
            Content = string.Format(LR.M_RestoreContent, fileName),
            PrimaryButtonText = LR.M_RestorePrimary,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result == FAContentDialogResult.Primary;
    }

    private async Task<bool> ConfirmDeleteAsync(string fileName)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_DeleteTitle,
            Content = string.Format(LR.M_DeleteContent, fileName),
            PrimaryButtonText = LR.M_DeletePrimary,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result == FAContentDialogResult.Primary;
    }

    private async Task ShowErrorDialogAsync(string title, string message)
    {
        await new FAContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = LR.C_Close,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private IEnumerable<string> GetSelectedDataRoots()
    {
        if (Settings.IncludeConfig)
        {
            yield return "config/settings.json";
            yield return "config/device-uuid.json";
        }
        if (Settings.IncludeList) yield return "list";
        if (Settings.IncludeHistory) yield return "history";
        if (Settings.IncludeProofs) yield return "proofs";

        if (Settings.IncludeAudio) yield return "audio";
        if (Settings.IncludeCses) yield return "CSES";
        if (Settings.IncludeImages) yield return "images";
        if (Settings.IncludeLogs) yield return "logs";
    }

    private static string GetBackupDirectory()
    {
        return Utils.GetDirectoryPath(BackupDirectoryName);
    }


    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {units[unit]}"
            : $"{size.ToString("0.#", CultureInfo.CurrentCulture)} {units[unit]}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
