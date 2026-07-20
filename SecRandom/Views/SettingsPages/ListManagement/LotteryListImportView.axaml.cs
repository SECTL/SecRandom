using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using MiniExcelLibs;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.Profiles;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Langs.SettingsPages.ListManagement.LotteryList.Resources;

namespace SecRandom.Views.SettingsPages.ListManagement;

public partial class LotteryListImportView : UserControl, INotifyPropertyChanged
{
    private Action<IReadOnlyList<Prize>> _importHandler;
    private readonly List<Dictionary<string, string>> _rows = [];
    private bool _canImport;
    private string? _countColumn;
    private string? _idColumn;
    private string? _nameColumn;
    private string _selectedFileName = LR.C_NoFileSelected;
    private string _statusText = LR.M_SelectFileFirst;
    private string? _tagsColumn;
    private string? _weightColumn;
    private string _targetListName = string.Empty;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;
    private readonly ILogger<LotteryListImportView> _logger =
        IAppHost.GetService<ILogger<LotteryListImportView>>();

    public LotteryListImportView()
        : this(string.Empty, _ => { })
    {
    }

    public LotteryListImportView(string targetListName, Action<IReadOnlyList<Prize>> importHandler)
    {
        TargetListName = targetListName;
        _importHandler = importHandler;
        DataContext = this;
        InitializeComponent();
    }

    public ObservableCollection<string> RequiredColumnOptions { get; } = [];
    public ObservableCollection<string> OptionalColumnOptions { get; } = [LR.C_NoneColumn];
    public ObservableCollection<ImportPreviewRow> PreviewRows { get; } = [];

    public string TargetListName
    {
        get => _targetListName;
        set
        {
            if (SetField(ref _targetListName, value))
                NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetListDescription)));
        }
    }

    public string TargetListDescription => string.Format(LR.C_TargetList, TargetListName);

    public Action<IReadOnlyList<Prize>> ImportHandler
    {
        get => _importHandler;
        set => _importHandler = value;
    }

    public Action? CloseHandler { get; set; }

    public string SelectedFileName
    {
        get => _selectedFileName;
        set => SetField(ref _selectedFileName, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public bool CanImport
    {
        get => _canImport;
        set => SetField(ref _canImport, value);
    }

    public string? IdColumn
    {
        get => _idColumn;
        set
        {
            if (SetField(ref _idColumn, value)) RefreshPreview();
        }
    }

    public string? NameColumn
    {
        get => _nameColumn;
        set
        {
            if (SetField(ref _nameColumn, value)) RefreshPreview();
        }
    }

    public string? WeightColumn
    {
        get => _weightColumn;
        set
        {
            if (SetField(ref _weightColumn, value)) RefreshPreview();
        }
    }

    public string? CountColumn
    {
        get => _countColumn;
        set
        {
            if (SetField(ref _countColumn, value)) RefreshPreview();
        }
    }

    public string? TagsColumn
    {
        get => _tagsColumn;
        set
        {
            if (SetField(ref _tagsColumn, value)) RefreshPreview();
        }
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private async void SelectFileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LR.C_FilePickerTitle,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LR.C_FilePickerTypeName)
                {
                    Patterns = ["*.xlsx", "*.xls", "*.csv"],
                    MimeTypes =
                    [
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "application/vnd.ms-excel",
                        "text/csv"
                    ]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file == null)
            return;

        try
        {
            await LoadFileAsync(file);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载奖品池导入文件失败：文件={FileName}。", file.Name);
            StatusText = string.Format(LR.M_LoadFailed, ex.Message);
            CanImport = false;
        }
    }

    private async Task LoadFileAsync(IStorageFile file)
    {
        var path = file.TryGetLocalPath();
        var temporaryPath = false;
        if (path is null)
        {
            path = await CopyToTemporaryFileAsync(file);
            temporaryPath = true;
        }

        List<Dictionary<string, string>> rows;
        try
        {
            rows = MiniExcel.Query(path, useHeaderRow: true)
                .Cast<IDictionary<string, object?>>()
                .Select(row => row.ToDictionary(pair => pair.Key, pair => ConvertCell(pair.Value)))
                .ToList();
        }
        finally
        {
            if (temporaryPath && File.Exists(path))
                File.Delete(path);
        }

        _rows.Clear();
        _rows.AddRange(rows);
        SelectedFileName = file.Name;
        RebuildColumnOptions(rows.FirstOrDefault()?.Keys ?? Enumerable.Empty<string>());
        AutoMapColumns();
        RefreshPreview();
        _logger.LogInformation("已加载奖品池导入文件：文件={FileName}，行数={RowCount}。", file.Name, rows.Count);
    }

    private static async Task<string> CopyToTemporaryFileAsync(IStorageFile file)
    {
        var extension = Path.GetExtension(file.Name);
        var path = Path.Combine(Path.GetTempPath(), $"SecRandom-{Guid.NewGuid():N}{extension}");
        await using var source = await file.OpenReadAsync();
        await using var target = File.Create(path);
        await source.CopyToAsync(target);
        return path;
    }

    private void RebuildColumnOptions(IEnumerable<string> columns)
    {
        RequiredColumnOptions.Clear();
        OptionalColumnOptions.Clear();
        OptionalColumnOptions.Add(LR.C_NoneColumn);

        foreach (var column in columns.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
        {
            RequiredColumnOptions.Add(column);
            OptionalColumnOptions.Add(column);
        }

        IdColumn = LR.C_NoneColumn;
        NameColumn = LR.C_NoneColumn;
        WeightColumn = LR.C_NoneColumn;
        CountColumn = LR.C_NoneColumn;
        TagsColumn = LR.C_NoneColumn;
    }

    private void AutoMapColumns()
    {
        IdColumn = RosterImportParser.FindBestColumn(RequiredColumnOptions, RosterImportParser.SplitKeywords(LR.K_IdColumns)) ?? LR.C_NoneColumn;
        NameColumn = RosterImportParser.FindBestColumn(RequiredColumnOptions, RosterImportParser.SplitKeywords(LR.K_NameColumns)) ?? NameColumn;
        WeightColumn = RosterImportParser.FindBestColumn(RequiredColumnOptions, RosterImportParser.SplitKeywords(LR.K_WeightColumns)) ?? LR.C_NoneColumn;
        CountColumn = RosterImportParser.FindBestColumn(RequiredColumnOptions, RosterImportParser.SplitKeywords(LR.K_CountColumns)) ?? LR.C_NoneColumn;
        TagsColumn = RosterImportParser.FindBestColumn(RequiredColumnOptions, RosterImportParser.SplitKeywords(LR.K_TagsColumns)) ?? LR.C_NoneColumn;
    }

    private PrizeRosterColumnMapping CurrentMapping => new(
        IsSelectedColumn(IdColumn) ? IdColumn : null,
        IsSelectedColumn(NameColumn) ? NameColumn : null,
        IsSelectedColumn(WeightColumn) ? WeightColumn : null,
        IsSelectedColumn(CountColumn) ? CountColumn : null,
        IsSelectedColumn(TagsColumn) ? TagsColumn : null);

    private void RefreshPreview()
    {
        PreviewRows.Clear();
        foreach (var row in _rows.Take(3))
            PreviewRows.Add(CreatePreviewRow(row));

        CanImport = _rows.Count > 0 && (IsSelectedColumn(IdColumn) || IsSelectedColumn(NameColumn));
        StatusText = _rows.Count == 0
            ? LR.M_SelectFileFirst
            : CanImport
                ? string.Format(LR.M_FileLoaded, _rows.Count)
                : LR.M_SelectRequiredColumns;
    }

    private ImportPreviewRow CreatePreviewRow(IReadOnlyDictionary<string, string> row)
    {
        var mapping = CurrentMapping;
        return new ImportPreviewRow(
            RosterImportParser.GetValue(row, mapping.Id),
            RosterImportParser.GetValue(row, mapping.Name),
            RosterImportParser.GetValue(row, mapping.Weight),
            RosterImportParser.GetValue(row, mapping.Count),
            string.Join(' ', RosterImportParser.SplitTags(RosterImportParser.GetValue(row, mapping.Tags))));
    }

    private void ImportButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var parseResult = RosterImportParser.ParsePrizes(_rows, CurrentMapping);
        var prizes = parseResult.Items;
        var duplicatedNames = parseResult.DuplicatedNames;

        if (duplicatedNames.Count > 0)
        {
            _logger.LogWarning("奖品池导入发现重复名称：重复名称数量={DuplicateNameCount}，有效行数={Count}。",
                duplicatedNames.Count, prizes.Count);
            _ = ConfirmDuplicateNamesAsync(prizes, duplicatedNames);
        }
        else
        {
            _logger.LogInformation("提交奖品池导入：有效行数={Count}。", prizes.Count);
            _importHandler(prizes);
        }
    }

    private async Task ConfirmDuplicateNamesAsync(List<Prize> prizes, IReadOnlyList<string> duplicatedNames)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_DuplicateTitle,
            Content = string.Format(LR.M_DuplicateContent, duplicatedNames.Count,
                string.Join('\n', duplicatedNames.Take(10))),
            PrimaryButtonText = LR.M_DuplicatePrimary,
            SecondaryButtonText = LR.M_DuplicateSecondary,
            CloseButtonText = LR.M_DuplicateClose,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));

        if (result == FAContentDialogResult.Primary)
        {
            _logger.LogInformation("奖品池导入保留重复名称：有效行数={Count}。", prizes.Count);
            _importHandler(prizes);
            return;
        }

        if (result == FAContentDialogResult.Secondary)
        {
            RosterImportParser.RenameDuplicatedPrizes(prizes);
            _logger.LogInformation("奖品池导入已自动处理重复名称：有效行数={Count}。", prizes.Count);
            _importHandler(prizes);
        }
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (CloseHandler is not null)
            CloseHandler();
        else
            SettingsView.Current?.CloseDrawer();
    }

    private static bool IsSelectedColumn(string? column)
    {
        return !string.IsNullOrWhiteSpace(column) && column != LR.C_NoneColumn;
    }

    private static string ConvertCell(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTime dateTime => dateTime.ToString(CultureInfo.CurrentCulture),
            _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty
        };
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public record ImportPreviewRow(string Id, string Name, string Weight, string Count, string Tags);
}
