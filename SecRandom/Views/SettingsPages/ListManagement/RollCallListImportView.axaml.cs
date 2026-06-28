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
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using MiniExcelLibs;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Langs.SettingsPages.ListManagement.RollCallList.Resources;

namespace SecRandom.Views.SettingsPages.ListManagement;

public partial class RollCallListImportView : UserControl, INotifyPropertyChanged
{
    private readonly Action<IReadOnlyList<Student>> _importHandler;
    private readonly List<Dictionary<string, string>> _rows = [];
    private bool _canImport;
    private string? _genderColumn;
    private string? _groupColumn;
    private string? _idColumn;
    private string? _nameColumn;
    private string _selectedFileName = LR.C_NoFileSelected;
    private string _statusText = LR.M_SelectFileFirst;
    private string? _tagsColumn;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;
    private readonly ILogger<RollCallListImportView> _logger =
        IAppHost.GetService<ILogger<RollCallListImportView>>();

    public RollCallListImportView()
        : this(string.Empty, _ => { })
    {
    }

    public RollCallListImportView(string targetListName, Action<IReadOnlyList<Student>> importHandler)
    {
        TargetListDescription = string.Format(LR.C_TargetList, targetListName);
        _importHandler = importHandler;
        DataContext = this;
        InitializeComponent();
    }

    public ObservableCollection<string> RequiredColumnOptions { get; } = [];
    public ObservableCollection<string> OptionalColumnOptions { get; } = [LR.C_NoneColumn];
    public ObservableCollection<ImportPreviewRow> PreviewRows { get; } = [];

    public string TargetListDescription { get; }

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

    public string? GenderColumn
    {
        get => _genderColumn;
        set
        {
            if (SetField(ref _genderColumn, value)) RefreshPreview();
        }
    }

    public string? GroupColumn
    {
        get => _groupColumn;
        set
        {
            if (SetField(ref _groupColumn, value)) RefreshPreview();
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

    private async void SelectFileButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
            _logger.LogWarning(ex, "加载点名名单导入文件失败：文件={FileName}。", file.Name);
            StatusText = string.Format(LR.M_LoadFailed, ex.Message);
            CanImport = false;
        }
    }

    private async Task LoadFileAsync(IStorageFile file)
    {
        var path = file.TryGetLocalPath();
        if (path == null)
            path = await CopyToTemporaryFileAsync(file);

        var rows = MiniExcel.Query(path, useHeaderRow: true)
            .Cast<IDictionary<string, object?>>()
            .Select(row => row.ToDictionary(pair => pair.Key, pair => ConvertCell(pair.Value)))
            .ToList();

        _rows.Clear();
        _rows.AddRange(rows);
        SelectedFileName = file.Name;
        RebuildColumnOptions(rows.FirstOrDefault()?.Keys ?? Enumerable.Empty<string>());
        AutoMapColumns();
        RefreshPreview();
        _logger.LogInformation("已加载点名名单导入文件：文件={FileName}，行数={RowCount}。", file.Name, rows.Count);
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
        NameColumn = RequiredColumnOptions.Skip(1).FirstOrDefault() ?? RequiredColumnOptions.FirstOrDefault();
        GenderColumn = LR.C_NoneColumn;
        GroupColumn = LR.C_NoneColumn;
        TagsColumn = LR.C_NoneColumn;
    }

    private void AutoMapColumns()
    {
        IdColumn = FindBestColumn(GetKeywords(LR.K_IdColumns)) ?? LR.C_NoneColumn;
        NameColumn = FindBestColumn(GetKeywords(LR.K_NameColumns)) ?? NameColumn;
        GenderColumn = FindBestColumn(GetKeywords(LR.K_GenderColumns))
                       ?? LR.C_NoneColumn;
        GroupColumn = FindBestColumn(GetKeywords(LR.K_GroupColumns))
                      ?? LR.C_NoneColumn;
        TagsColumn = FindBestColumn(GetKeywords(LR.K_TagsColumns))
                     ?? LR.C_NoneColumn;
    }

    private static string[] GetKeywords(string keywords)
    {
        return keywords.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private string? FindBestColumn(IReadOnlyList<string> keywords)
    {
        var bestScore = 0;
        string? bestColumn = null;

        foreach (var column in RequiredColumnOptions)
        {
            var normalizedColumn = column.ToLowerInvariant();
            for (var i = 0; i < keywords.Count; i++)
            {
                var keyword = keywords[i].ToLowerInvariant();
                var score = normalizedColumn == keyword ? 100 - i : normalizedColumn.Contains(keyword) ? 50 - i : 0;
                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestColumn = column;
            }
        }

        return bestColumn;
    }

    private void RefreshPreview()
    {
        PreviewRows.Clear();
        foreach (var row in _rows.Take(3))
            PreviewRows.Add(CreatePreviewRow(row));

        CanImport = _rows.Count > 0 && IsSelectedColumn(NameColumn);
        StatusText = _rows.Count == 0
            ? LR.M_SelectFileFirst
            : CanImport
                ? string.Format(LR.M_FileLoaded, _rows.Count)
                : LR.M_SelectRequiredColumns;
    }

    private ImportPreviewRow CreatePreviewRow(IReadOnlyDictionary<string, string> row)
    {
        return new ImportPreviewRow(
            GetColumnValue(row, IdColumn),
            GetColumnValue(row, NameColumn),
            GetColumnValue(row, GenderColumn),
            GetColumnValue(row, GroupColumn),
            string.Join(' ', SplitTags(GetColumnValue(row, TagsColumn))));
    }

    private void ImportButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var students = _rows
            .Select(row => new Student
            {
                Id = GetColumnValue(row, IdColumn),
                Name = GetColumnValue(row, NameColumn),
                Gender = GetColumnValue(row, GenderColumn),
                Group = GetColumnValue(row, GroupColumn),
                Tags = string.Join(' ', SplitTags(GetColumnValue(row, TagsColumn))),
                Exists = true
            })
            .Where(student => !string.IsNullOrWhiteSpace(student.Name))
            .ToList();

        var duplicatedNames = students.GroupBy(student => student.Name)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicatedNames.Count > 0)
        {
            _logger.LogWarning("点名名单导入发现重复姓名：重复姓名数量={DuplicateNameCount}，有效行数={Count}。",
                duplicatedNames.Count, students.Count);
            _ = ConfirmDuplicateNamesAsync(students, duplicatedNames);
        }
        else
        {
            _logger.LogInformation("提交点名名单导入：有效行数={Count}。", students.Count);
            _importHandler(students);
        }
    }

    private async Task ConfirmDuplicateNamesAsync(List<Student> students, IReadOnlyList<string> duplicatedNames)
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
            _logger.LogInformation("点名名单导入保留重复姓名：有效行数={Count}。", students.Count);
            _importHandler(students);
            return;
        }

        if (result == FAContentDialogResult.Secondary)
        {
            RenameDuplicatedStudents(students);
            _logger.LogInformation("点名名单导入已自动处理重复姓名：有效行数={Count}。", students.Count);
            _importHandler(students);
        }
    }

    private static void RenameDuplicatedStudents(IEnumerable<Student> students)
    {
        var counts = new Dictionary<string, int>();

        foreach (var student in students)
        {
            var baseName = student.Name;
            counts.TryAdd(baseName, 0);
            counts[baseName]++;
            if (counts[baseName] > 1)
                student.Name = $"{baseName} ({counts[baseName]})";
        }
    }

    private void CancelButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SettingsView.Current?.CloseDrawer();
    }

    private static bool IsSelectedColumn(string? column)
    {
        return !string.IsNullOrWhiteSpace(column) && column != LR.C_NoneColumn;
    }

    private static string GetColumnValue(IReadOnlyDictionary<string, string> row, string? column)
    {
        if (!IsSelectedColumn(column) || column == null)
            return string.Empty;

        return row.GetValueOrDefault(column, string.Empty).Trim();
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

    private static IEnumerable<string> SplitTags(string rawTags)
    {
        if (string.IsNullOrWhiteSpace(rawTags))
            return [];

        foreach (var separator in new[] { '，', ',', '；', ';', '|', '/', '\\', '\n', '\t' })
            rawTags = rawTags.Replace(separator, ' ');

        return rawTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public record ImportPreviewRow(string Id, string Name, string Gender, string Group, string Tags);
}
