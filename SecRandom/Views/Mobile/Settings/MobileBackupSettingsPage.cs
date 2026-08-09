using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SecRandom.Core.Attributes;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.Archive;
using LR = SecRandom.Langs.Mobile.Resources;
using AvaloniaButton = Avalonia.Controls.Button;

namespace SecRandom.Views.Mobile.Settings;

/// <summary>
/// 备份页：全量数据 ZIP 与设置 envelope 的导出/导入，引擎为 Core 的
/// <see cref="DataArchiveService"/>（已注册于 AddCoreRuntimeServices，IArchivePostImportHooks
/// 默认 Null 实现足够，Core 内部已完成 config 与档案重载）。
/// 文件交换走系统 StorageProvider 选择器：Android SAF 下 IStorageFile 只能做
/// OpenRead/OpenWrite 流操作，不假设共享存储上的真实路径。导出先落本地临时文件再复制流，
/// 导入先复制流到临时文件再校验（v3 manifest），确认后才执行导入。
/// 忙时禁用全部操作按钮并显示进度文案；StorageProvider 不可用时优雅降级提示。
/// </summary>
[PageInfo(MobilePageIds.Backup, FluentIcons.ArchiveFilled, "settings.mobile.data")]
public sealed partial class MobileBackupSettingsPage : MobileSettingsPageBase
{
    private enum PendingImportKind
    {
        None,
        AllData,
        Settings
    }

    private readonly AvaloniaButton _exportAllButton;
    private readonly AvaloniaButton _importAllButton;
    private readonly AvaloniaButton _exportSettingsButton;
    private readonly AvaloniaButton _importSettingsButton;
    private readonly TextBlock _statusText;
    private readonly AvaloniaButton _confirmButton;
    private readonly AvaloniaButton _cancelButton;
    private readonly TextBlock _confirmDetail;
    private readonly TextBlock _confirmWarnings;
    private readonly Control _confirmHost;

    private bool _busy;
    private PendingImportKind _pendingKind = PendingImportKind.None;
    private string? _pendingTempPath;
    private bool _confirmAllowed;
    public MobileBackupSettingsPage(IMobileCapabilities capabilities)
        : base(capabilities)
    {
        InitializeComponent();
        _exportAllButton = this.FindControl<AvaloniaButton>("ExportAllButton")!;
        _importAllButton = this.FindControl<AvaloniaButton>("ImportAllButton")!;
        _exportSettingsButton = this.FindControl<AvaloniaButton>("ExportSettingsButton")!;
        _importSettingsButton = this.FindControl<AvaloniaButton>("ImportSettingsButton")!;
        _statusText = this.FindControl<TextBlock>("BackupStatusText")!;
        _confirmHost = this.FindControl<Control>("ImportConfirmationHost")!;
        _confirmButton = this.FindControl<AvaloniaButton>("ConfirmImportButton")!;
        _cancelButton = this.FindControl<AvaloniaButton>("CancelImportButton")!;
        _confirmDetail = this.FindControl<TextBlock>("ImportConfirmationDetail")!;
        _confirmWarnings = this.FindControl<TextBlock>("ImportConfirmationWarnings")!;

        // 页面在待确认导入期间被关闭（如返回/切换目的地）时清理临时文件，避免泄露。
        DetachedFromVisualTree += (_, _) =>
        {
            var tempPath = _pendingTempPath;
            HideImportConfirmation();
            TryDeleteFile(tempPath);
        };

    }

    private async void ExportAllButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await ExportAllDataAsync();

    private async void ImportAllButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await PickImportAsync(PendingImportKind.AllData);

    private async void ExportSettingsButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await ExportSettingsAsync();

    private async void ImportSettingsButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await PickImportAsync(PendingImportKind.Settings);

    private async void ConfirmImportButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await ConfirmImportAsync();

    private void CancelImportButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        CancelPendingImport();

    // ---- 导出 ----

    private async Task ExportAllDataAsync()
    {
        if (!TryBeginOperation(out var archive, out var storage, requireSave: true))
            return;

        string? tempPath = null;
        try
        {
            SetBusy(true, LR.M_ExportingAllData);
            tempPath = CreateTempPath(".zip");
            await archive.ExportAllDataAsync(tempPath);

            var target = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = LR.M_SaveAllDataTitle,
                SuggestedFileName = $"SecRandom-Data-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
                DefaultExtension = "zip",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType(LR.M_DataArchiveFileType) { Patterns = ["*.zip"] }]
            });
            if (target is null)
            {
                SetBusy(false, LR.M_ExportCancelled);
                return;
            }

            SetBusy(true, LR.M_WritingToTarget);
            await using (var input = File.OpenRead(tempPath))
            await using (var output = await target.OpenWriteAsync())
                await input.CopyToAsync(output);
            SetBusy(false, LR.M_AllDataExported);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetBusy(false, string.Format(LR.M_ExportFailed, exception.Message));
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private async Task ExportSettingsAsync()
    {
        if (!TryBeginOperation(out var archive, out var storage, requireSave: true))
            return;

        string? tempPath = null;
        try
        {
            SetBusy(true, LR.M_ExportingSettings);
            tempPath = CreateTempPath(".json");
            await archive.ExportSettingsAsync(tempPath);

            var target = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = LR.M_SaveSettingsTitle,
                SuggestedFileName = $"SecRandom-Settings-{DateTime.Now:yyyyMMdd-HHmmss}.json",
                DefaultExtension = "json",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType(LR.M_SettingsFileType) { Patterns = ["*.json"] }]
            });
            if (target is null)
            {
                SetBusy(false, LR.M_ExportCancelled);
                return;
            }

            SetBusy(true, LR.M_WritingToTarget);
            await using (var input = File.OpenRead(tempPath))
            await using (var output = await target.OpenWriteAsync())
                await input.CopyToAsync(output);
            SetBusy(false, LR.M_SettingsExported);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetBusy(false, string.Format(LR.M_ExportFailed, exception.Message));
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    // ---- 导入 ----

    private async Task PickImportAsync(PendingImportKind kind)
    {
        if (!TryBeginOperation(out var archive, out var storage, requireSave: false))
            return;

        var isAllData = kind == PendingImportKind.AllData;
        string? tempPath = null;
        try
        {
            SetBusy(true, LR.M_PickingBackupFile);
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = isAllData ? LR.M_PickAllDataTitle : LR.M_PickSettingsTitle,
                AllowMultiple = false,
                FileTypeFilter = isAllData
                    ? [new FilePickerFileType(LR.M_DataArchiveFileType) { Patterns = ["*.zip"] }]
                    : [new FilePickerFileType(LR.M_SettingsFileType) { Patterns = ["*.json"] }]
            });
            var file = files.Count > 0 ? files[0] : null;
            if (file is null)
            {
                SetBusy(false, LR.M_ImportCancelled);
                return;
            }

            SetBusy(true, LR.M_ReadingBackup);
            tempPath = CreateTempPath(isAllData ? ".zip" : ".json");
            try
            {
                await using (var input = await file.OpenReadAsync())
                await using (var output = File.Create(tempPath))
                    await input.CopyToAsync(output);

                SetBusy(true, LR.M_ValidatingBackup);
                var inspection = isAllData
                    ? await archive.InspectAllDataAsync(tempPath)
                    : await archive.InspectSettingsAsync(tempPath);

                // 校验通过与否都先展示检查结果；只有受支持的 v3 文件才允许确认导入。
                ShowImportConfirmation(kind, tempPath, inspection);
                SetBusy(false, inspection.IsSupportedV3
                    ? LR.M_ValidationPassed
                    : LR.M_ValidationUnsupported);
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetBusy(false, string.Format(LR.M_ReadOrValidateFailed, exception.Message));
        }
    }

    private void ShowImportConfirmation(PendingImportKind kind, string tempPath, ImportInspection inspection)
    {
        _pendingKind = kind;
        _pendingTempPath = tempPath;
        _confirmAllowed = inspection.IsSupportedV3;

        _confirmDetail.Text = string.Format(
            LR.M_SourceVersion,
            string.IsNullOrWhiteSpace(inspection.ProducerVersion) ? LR.M_UnrecognizedVersion : inspection.ProducerVersion)
            + "\n"
            + string.Format(
                LR.M_ImportFileCount,
                inspection.FileCount,
                (inspection.UncompressedBytes / 1024d / 1024d).ToString("F1"));
        _confirmWarnings.Text = string.Join("\n", inspection.Warnings);
        _confirmWarnings.IsVisible = inspection.Warnings.Count > 0;
        _confirmButton.IsEnabled = inspection.IsSupportedV3;
        _cancelButton.IsEnabled = true;
        _confirmHost.IsVisible = true;
        MobileAnimations.PlayResultReveal(_confirmHost);
    }

    private async Task ConfirmImportAsync()
    {
        var archive = IAppHost.TryGetService<DataArchiveService>();
        if (archive is null)
        {
            SetStatus(LR.M_BackupServiceUnavailable);
            return;
        }

        var tempPath = _pendingTempPath;
        var kind = _pendingKind;
        if (tempPath is null || kind == PendingImportKind.None)
            return;

        HideImportConfirmation();
        try
        {
            SetBusy(true, LR.M_Importing);
            var result = kind == PendingImportKind.AllData
                ? await archive.ImportAllDataAsync(tempPath)
                : await archive.ImportSettingsAsync(tempPath);

            var status = string.Format(LR.M_ImportCompleted, result.ImportedFiles);
            // ImportResult.Warnings 已包含校验与 post-import hooks 的非致命警告。
            if (result.Warnings.Count > 0)
                status += "\n" + string.Join("\n", result.Warnings);
            SetBusy(false, status);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetBusy(false, string.Format(LR.M_ImportFailed, exception.Message));
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private void CancelPendingImport()
    {
        var tempPath = _pendingTempPath;
        HideImportConfirmation();
        TryDeleteFile(tempPath);
        SetStatus(LR.M_ImportCancelled);
    }

    private void HideImportConfirmation()
    {
        _pendingKind = PendingImportKind.None;
        _pendingTempPath = null;
        _confirmAllowed = false;
        _confirmDetail.Text = string.Empty;
        _confirmWarnings.Text = string.Empty;
        _confirmWarnings.IsVisible = false;
        _confirmHost.IsVisible = false;
    }

    // ---- 状态与工具 ----

    private bool TryBeginOperation(out DataArchiveService archive, out IStorageProvider storage, bool requireSave)
    {
        archive = null!;
        storage = null!;
        if (_busy)
            return false;

        // 已有待确认的导入时，要求先确认或取消，避免覆盖 pending 状态并泄露临时文件。
        if (_pendingKind != PendingImportKind.None)
        {
            SetStatus(LR.M_PendingImportExists);
            return false;
        }

        var resolvedArchive = IAppHost.TryGetService<DataArchiveService>();
        if (resolvedArchive is null)
        {
            SetStatus(LR.M_BackupServiceUnavailable);
            return false;
        }

        var resolvedStorage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (resolvedStorage is null || (requireSave && !resolvedStorage.CanSave) || (!requireSave && !resolvedStorage.CanOpen))
        {
            SetStatus(LR.M_StorageUnavailable);
            return false;
        }

        archive = resolvedArchive;
        storage = resolvedStorage;
        return true;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        _exportAllButton.IsEnabled = !busy;
        _importAllButton.IsEnabled = !busy;
        _exportSettingsButton.IsEnabled = !busy;
        _importSettingsButton.IsEnabled = !busy;
        _confirmButton.IsEnabled = !busy && _confirmAllowed;
        _cancelButton.IsEnabled = !busy;
        if (status is not null)
            SetStatus(status);
    }

    private void SetStatus(string status) => _statusText.Text = status;

    private static string CreateTempPath(string extension)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SecRandom", "backup-transfer");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, Guid.NewGuid().ToString("N") + extension);
    }

    private static void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 临时文件清理失败不影响备份事务结果。
        }
    }
}
