using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Models;
using SecRandom.Shared;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.SettingsPages.General.Backup.Resources;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.backup", FluentIcons.ArchiveFilled, "settings.general")]
public partial class BackupSettingsPage : UserControl, INotifyPropertyChanged
{
    private const string BackupDirectoryName = "backup";

    private static readonly string[] KnownRestorePaths =
    [
        "config",
        "data/list",
        "data/history",
        "list",
        "history",
        "audio",
        "CSES",
        "cses",
        "images",
        "theme",
        "themes",
        "logs"
    ];

    private string _backupUsageText = FormatSize(0);
    private event PropertyChangedEventHandler? NotifyPropertyChanged;
    private readonly ILogger<BackupSettingsPage> _logger =
        IAppHost.GetService<ILogger<BackupSettingsPage>>();

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
            new(LR.S_Includes_Audio, () => Settings.IncludeAudio,
                value => Settings.IncludeAudio = value),
            new(LR.S_Includes_Cses, () => Settings.IncludeCses,
                value => Settings.IncludeCses = value),
            new(LR.S_Includes_Images, () => Settings.IncludeImages,
                value => Settings.IncludeImages = value),
            new(LR.S_Includes_Themes, () => Settings.IncludeThemes,
                value => Settings.IncludeThemes = value),
            new(LR.S_Includes_Logs, () => Settings.IncludeLogs,
                value => Settings.IncludeLogs = value)
        ];
        SelectedIncludeOptions = BuildSelectedOptions(IncludeOptions);
        DataContext = this;
        InitializeComponent();
        RefreshBackups();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public BackupConfig Settings { get; }

    public ObservableCollection<BackupMetadata> Backups { get; } = [];
    public AvaloniaList<MultiSelectSettingOption> IncludeOptions { get; }
    public AvaloniaList<MultiSelectSettingOption> SelectedIncludeOptions { get; }

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

    private void MultiSelect_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        foreach (var option in e.AddedItems.OfType<MultiSelectSettingOption>())
        {
            option.SetSelected(true);
        }

        foreach (var option in e.RemovedItems.OfType<MultiSelectSettingOption>())
        {
            option.SetSelected(false);
        }
    }

    private async void ManualBackup_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = CreateBackup("manual");
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
        OpenPath(GetBackupDirectory());
        _logger.LogInformation("已请求打开备份目录：路径={Path}。", GetBackupDirectory());
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

        if (!await ConfirmRestoreAsync(backup.FileName))
            return;

        try
        {
            CreateBackup("pre_restore");
            RestoreBackup(backup.FilePath);
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

    private string CreateBackup(string kind)
    {
        var backupDirectory = GetBackupDirectory();
        var backupPath = CreateBackupPath(backupDirectory, kind);
        var selectedRoots = GetSelectedDataRoots().ToList();

        if (selectedRoots.Count == 0)
            throw new InvalidOperationException(LR.M_NoBackupContentSelected);

        _logger.LogInformation("开始创建备份：类型={Kind}，文件={FileName}，包含项={Roots}。",
            kind, Path.GetFileName(backupPath), string.Join(",", selectedRoots));

        using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            foreach (var root in selectedRoots)
            {
                var sourceDirectory = GetDataRelativePath(root);
                if (!Directory.Exists(sourceDirectory))
                    continue;

                AddDirectoryToArchive(archive, sourceDirectory, root);
            }
        }

        TrimBackups();
        return backupPath;
    }

    private static string CreateBackupPath(string backupDirectory, string kind)
    {
        var baseName = $"SecRandom_{kind}_{DateTime.Now:yyyyMMdd_HHmmss}";
        var backupPath = Path.Combine(backupDirectory, $"{baseName}.zip");
        if (!File.Exists(backupPath))
            return backupPath;

        for (var i = 2;; i++)
        {
            backupPath = Path.Combine(backupDirectory, $"{baseName}_{i}.zip");
            if (!File.Exists(backupPath))
                return backupPath;
        }
    }

    private void RestoreBackup(string backupPath)
    {
        using var archive = ZipFile.OpenRead(backupPath);
        foreach (var entry in archive.Entries)
            ValidateEntryDestination(entry);

        var restorePaths = archive.Entries
            .Select(GetAllowedRestorePath)
            .Where(path => path != null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var restorePath in restorePaths)
        {
            var targetDirectory = GetDataRelativePath(restorePath);
            if (Directory.Exists(targetDirectory))
                Directory.Delete(targetDirectory, true);
        }

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            if (GetAllowedRestorePath(entry) == null)
                continue;

            var destinationPath = GetEntryDestinationPath(entry);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, true);
        }
    }

    private void TrimBackups()
    {
        if (Settings.AutoBackupMaxCount <= 0)
            return;

        var files = new DirectoryInfo(GetBackupDirectory())
            .EnumerateFiles("*.zip")
            .OrderByDescending(file => file.CreationTimeUtc)
            .Skip(Settings.AutoBackupMaxCount);

        foreach (var file in files)
            file.Delete();
    }

    private static void AddDirectoryToArchive(ZipArchive archive, string sourceDirectory, string entryRoot)
    {
        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
            var entryName = Path.Combine(entryRoot, relativePath).Replace(Path.DirectorySeparatorChar, '/');
            archive.CreateEntryFromFile(filePath, entryName, CompressionLevel.SmallestSize);
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
        if (Settings.IncludeConfig) yield return "config";
        if (Settings.IncludeList)
        {
            yield return "data/list";
            yield return "list";
        }

        if (Settings.IncludeHistory)
        {
            yield return "data/history";
            yield return "history";
        }

        if (Settings.IncludeAudio) yield return "audio";
        if (Settings.IncludeCses) yield return "CSES";
        if (Settings.IncludeImages) yield return "images";
        if (Settings.IncludeThemes) yield return "theme";
        if (Settings.IncludeLogs) yield return "logs";
    }

    private static string? GetAllowedRestorePath(ZipArchiveEntry entry)
    {
        var parts = entry.FullName
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (parts.Count == 0)
            return null;

        var relativePath = parts.Count >= 2 && parts[0].Equals("data", StringComparison.OrdinalIgnoreCase)
            ? $"{parts[0]}/{parts[1]}"
            : parts[0];

        return KnownRestorePaths.Contains(relativePath, StringComparer.OrdinalIgnoreCase) ? relativePath : null;
    }

    private static void ValidateEntryDestination(ZipArchiveEntry entry)
    {
        var restorePath = GetAllowedRestorePath(entry);
        if (string.IsNullOrWhiteSpace(entry.Name) || restorePath == null)
            return;

        var destinationPath = GetEntryDestinationPath(entry);
        if (!destinationPath.StartsWith(GetDirectoryWithSeparator(GetDataRelativePath(restorePath)),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(LR.M_InvalidBackupFile);
    }

    private static string GetEntryDestinationPath(ZipArchiveEntry entry)
    {
        return Path.GetFullPath(Path.Combine(GetDataDirectory(), entry.FullName));
    }

    private static string GetDataRelativePath(string relativePath)
    {
        return Path.Combine(GetDataDirectory(), relativePath);
    }

    private static string GetBackupDirectory()
    {
        return Utils.GetDirectoryPath(BackupDirectoryName);
    }

    private static string GetDataDirectory()
    {
        return Utils.GetDirectoryPath();
    }

    private static string GetDataDirectoryWithSeparator()
    {
        return GetDirectoryWithSeparator(GetDataDirectory());
    }

    private static string GetDirectoryWithSeparator(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        return fullPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullPath
            : fullPath + Path.DirectorySeparatorChar;
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

    private static void OpenPath(string path)
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        else if (OperatingSystem.IsLinux())
            Process.Start(new ProcessStartInfo("xdg-open", path) { UseShellExecute = false });
        else if (OperatingSystem.IsMacOS())
            Process.Start(new ProcessStartInfo("open", path) { UseShellExecute = false });
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
