using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Shared;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Interfaces;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Views.SettingsPages.Picking;

/// <summary>
/// Right-side drawer that batch-matches avatars for a selected member list or the
/// current prize pool from the first-level images of a picked directory. Each row can
/// re-pick an external image file with its own button. Non-local files (Android SAF) are
/// copied into the managed images folder on confirm so the record keeps its avatar.
/// Confirm applies only the active tab's rows (the selected roster or the prize pool).
/// </summary>
public partial class BatchAvatarDrawer : UserControl
{
    private static readonly Guid DrawImageSettingsId = Guid.Parse(GlobalConstants.DrawImageAttachedSettings);
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
    };

    private readonly IProfileService _profileService = IAppHost.GetService<IProfileService>();
    private readonly IProfileCatalogManager _catalogManager = IAppHost.GetService<IProfileCatalogManager>();

    private IReadOnlyList<BatchAvatarFileEntry> _availableFiles = [];
    private IReadOnlyList<string> _rosterNames = [];
    private IReadOnlyList<string> _prizePoolNames = [];
    private StudentList? _studentListSnapshot;
    private PrizeList? _prizeListSnapshot;
    private readonly ObservableCollection<BatchAvatarRow> _studentRows = [];
    private readonly ObservableCollection<BatchAvatarRow> _prizeRows = [];

    public BatchAvatarDrawer()
    {
        DataContext = this;
        InitializeComponent();

        _rosterNames = _catalogManager.GetStudentListNames().ToArray();
        RosterComboBox.ItemsSource = _rosterNames;
        SelectDefaultRoster();

        _prizePoolNames = _catalogManager.GetPrizeListNames().ToArray();
        PrizeComboBox.ItemsSource = _prizePoolNames;
        SelectDefaultPrizePool();

        UpdateGridAndSummary();
    }

    private void SelectDefaultRoster()
    {
        var currentName = _profileService.CurrentStudentList?.Name;
        if (!string.IsNullOrWhiteSpace(currentName) && _rosterNames.Contains(currentName))
            RosterComboBox.SelectedItem = currentName;
        else if (_rosterNames.Count > 0)
            RosterComboBox.SelectedIndex = 0;
    }

    private void SelectDefaultPrizePool()
    {
        var currentName = _profileService.CurrentPrizeList?.Name;
        if (!string.IsNullOrWhiteSpace(currentName) && _prizePoolNames.Contains(currentName))
            PrizeComboBox.SelectedItem = currentName;
        else if (_prizePoolNames.Count > 0)
            PrizeComboBox.SelectedIndex = 0;
    }

    private async void ChooseDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Langs.SettingsPages.Picking.Resources.C_BatchAvatarChooseDirectory,
            AllowMultiple = false
        });
        var folder = folders.FirstOrDefault();
        if (folder is null)
            return;

        var files = new List<BatchAvatarFileEntry>();
        try
        {
            await foreach (var item in folder.GetItemsAsync())
            {
                if (item is IStorageFile file && SupportedImageExtensions.Contains(Path.GetExtension(file.Name)))
                    files.Add(new BatchAvatarFileEntry(file, file.Name));
            }
        }
        catch
        {
            this.ShowErrorToast(Langs.SettingsPages.Picking.Resources.M_BatchAvatarNoDirectory);
            return;
        }

        _availableFiles = files;
        SelectedDirectoryTextBlock.Text = folder.TryGetLocalPath() ?? folder.Name;
        BuildStudentRows();
        BuildPrizeRows();
        UpdateGridAndSummary();
    }

    private async void ChooseFileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: BatchAvatarRow row })
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Langs.SettingsPages.Picking.Resources.C_BatchAvatarChooseFile,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图片文件")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"]
                },
                FilePickerFileTypes.All
            ]
        });
        var file = files.FirstOrDefault();
        if (file is null || !SupportedImageExtensions.Contains(Path.GetExtension(file.Name)))
            return;

        row.SetFile(file);
    }

    private void RosterComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (RosterComboBox.SelectedItem is not string rosterName)
            return;

        _studentListSnapshot = _catalogManager.LoadStudentList(rosterName);
        BuildStudentRows();
        UpdateGridAndSummary();
    }

    private void PrizeComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PrizeComboBox.SelectedItem is not string prizePoolName)
            return;

        _prizeListSnapshot = _catalogManager.LoadPrizeList(prizePoolName);
        BuildPrizeRows();
        UpdateGridAndSummary();
    }

    private void BuildStudentRows()
    {
        _studentRows.Clear();
        if (_studentListSnapshot is null)
            return;

        foreach (var student in _studentListSnapshot.Students)
        {
            var row = new BatchAvatarRow(student, BuildRecordName(student.Id, student.Name));
            var matched = FindMatch(student.Id, student.Name);
            if (matched is not null)
            {
                var entry = _availableFiles.First(item => string.Equals(item.Name, matched, StringComparison.OrdinalIgnoreCase));
                row.SetFile(entry.File);
            }

            _studentRows.Add(row);
        }
    }

    private void BuildPrizeRows()
    {
        _prizeRows.Clear();
        if (_prizeListSnapshot is null)
            return;

        foreach (var prize in _prizeListSnapshot.Prizes)
        {
            var row = new BatchAvatarRow(prize, BuildRecordName(prize.Id, prize.Name));
            var matched = FindMatch(prize.Id, prize.Name);
            if (matched is not null)
            {
                var entry = _availableFiles.First(item => string.Equals(item.Name, matched, StringComparison.OrdinalIgnoreCase));
                row.SetFile(entry.File);
            }

            _prizeRows.Add(row);
        }

        PrizeTabItem.IsEnabled = _prizeListSnapshot.Prizes.Count > 0;
    }

    private string? FindMatch(string recordId, string recordName)
    {
        foreach (var entry in _availableFiles)
        {
            var stem = Path.GetFileNameWithoutExtension(entry.Name);
            if (string.Equals(stem, recordId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(stem, recordName, StringComparison.OrdinalIgnoreCase) ||
                (recordId.Length > 0 && stem.StartsWith(recordId, StringComparison.OrdinalIgnoreCase)
                    && stem.Length > recordId.Length && IsNameSeparator(stem[recordId.Length])))
                return entry.Name;
        }

        return null;
    }

    private static bool IsNameSeparator(char character) => character is '-' or '_' or ' ' or '.';

    private static string BuildRecordName(string id, string name)
    {
        var parts = new[] { id, name }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return string.Join(" ", parts).Trim();
    }

    private bool IsRosterTab => AvatarTabStrip.SelectedIndex == 0;

    private void UpdateGridAndSummary()
    {
        var rows = IsRosterTab ? _studentRows : _prizeRows;
        RowsGrid.ItemsSource = rows;

        var matchedCount = rows.Count(row => row.IsSet);
        MatchSummaryTextBlock.Text = matchedCount == 0
            ? string.Empty
            : string.Format(Langs.SettingsPages.Picking.Resources.C_BatchAvatarMatchSummary, matchedCount);
    }

    private void AvatarTabStrip_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (RowsGrid is null)
            return;

        var isRosterTab = IsRosterTab;
        RosterSelector.IsVisible = isRosterTab;
        PrizeSelector.IsVisible = !isRosterTab;
        UpdateGridAndSummary();
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsView.Current?.CloseDrawer();
    }

    private async void ConfirmButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var rows = IsRosterTab ? _studentRows : _prizeRows;
        var selectedRows = rows.Where(row => row.IsSet).ToArray();
        if (selectedRows.Length == 0)
        {
            SettingsView.Current?.CloseDrawer();
            return;
        }

        foreach (var row in selectedRows)
        {
            if (row.SelectedFile is null)
            {
                this.ShowErrorToast(string.Format(Langs.SettingsPages.Picking.Resources.M_BatchAvatarFileNotFound, row.FileName));
                return;
            }
        }

        var imagesDirectory = Utils.GetDirectoryPath("images");
        var applied = 0;
        foreach (var row in selectedRows)
        {
            var path = await ResolveImagePathAsync(row.SelectedFile!, imagesDirectory);
            if (path is null)
                continue;

            row.Record.WriteAttachedObject(DrawImageSettingsId, new DrawImageAttachedSettings
            {
                IsAttachSettingsEnabled = true,
                ImagePath = path
            });
            applied++;
        }

        if (IsRosterTab)
        {
            if (_studentListSnapshot is not null)
                _catalogManager.SaveStudentList(_studentListSnapshot);
        }
        else if (_prizeListSnapshot is not null)
        {
            _catalogManager.SavePrizeList(_prizeListSnapshot);
        }

        if (applied > 0)
            this.ShowSuccessToast(string.Format(Langs.SettingsPages.Picking.Resources.M_BatchAvatarApplied, applied));

        SettingsView.Current?.CloseDrawer();
    }

    private static async Task<string?> ResolveImagePathAsync(IStorageFile file, string imagesDirectory)
    {
        var local = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(local) && File.Exists(local))
            return local;

        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (!SupportedImageExtensions.Contains(extension))
            return null;

        // Android SAF / non-local storage: copy into the managed images folder so the
        // record keeps its avatar after the external access is revoked.
        var managedPath = Path.Combine(imagesDirectory, $"{Guid.NewGuid():N}{extension}");
        try
        {
            await using var source = await file.OpenReadAsync();
            await using var target = File.Create(managedPath);
            await source.CopyToAsync(target);
            return managedPath;
        }
        catch
        {
            return null;
        }
    }
}

public partial class BatchAvatarRow : ObservableObject
{
    public BatchAvatarRow(IAttachableSettingsObject record, string recordName)
    {
        Record = record;
        RecordName = recordName;
    }

    public IAttachableSettingsObject Record { get; }
    public string RecordName { get; }
    internal IStorageFile? SelectedFile { get; private set; }

    [ObservableProperty] private bool _isSet;
    [ObservableProperty] private string _fileName = string.Empty;

    public void SetFile(IStorageFile file)
    {
        SelectedFile = file;
        FileName = file.Name;
        IsSet = true;
    }
}

internal sealed record BatchAvatarFileEntry(IStorageFile File, string Name);
